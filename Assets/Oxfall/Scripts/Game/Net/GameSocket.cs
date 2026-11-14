using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Oxfall.Game.Net
{
    /// <summary>
    /// A WebSocket the game polls once a frame: messages are queued as they arrive and handed out
    /// on the main thread, so nothing in the game runs on another thread. The web build uses the
    /// browser's socket; phones and the editor use .NET's.
    /// </summary>
    public abstract class GameSocket : IDisposable
    {
        public enum Status { Connecting, Open, Closed }

        public abstract Status State { get; }

        public abstract void Send(byte[] data, int length);

        /// <summary>The next message that arrived, or null when none is waiting.</summary>
        public abstract byte[] Receive();

        public abstract void Dispose();

        public static GameSocket Open(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new BrowserSocket(url);
#else
            return new DotNetSocket(url);
#endif
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    internal sealed class BrowserSocket : GameSocket
    {
        [DllImport("__Internal")] private static extern int OxfallSocketOpen(string url);
        [DllImport("__Internal")] private static extern int OxfallSocketState(int id);
        [DllImport("__Internal")] private static extern int OxfallSocketSend(int id, byte[] data, int length);
        [DllImport("__Internal")] private static extern int OxfallSocketNextLength(int id);
        [DllImport("__Internal")] private static extern int OxfallSocketReceive(int id, byte[] buffer, int maxLength);
        [DllImport("__Internal")] private static extern void OxfallSocketClose(int id);

        private int _id;

        public BrowserSocket(string url) => _id = OxfallSocketOpen(url);

        public override Status State
        {
            get
            {
                if (_id == 0) return Status.Closed;
                int s = OxfallSocketState(_id);
                return s == 0 ? Status.Connecting : s == 1 ? Status.Open : Status.Closed;
            }
        }

        public override void Send(byte[] data, int length)
        {
            if (_id != 0) OxfallSocketSend(_id, data, length);
        }

        public override byte[] Receive()
        {
            if (_id == 0) return null;
            int n = OxfallSocketNextLength(_id);
            if (n < 0) return null;
            var buffer = new byte[n];
            OxfallSocketReceive(_id, buffer, n);
            return buffer;
        }

        public override void Dispose()
        {
            if (_id == 0) return;
            OxfallSocketClose(_id);
            _id = 0;
        }
    }
#endif

    internal sealed class DotNetSocket : GameSocket
    {
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly ConcurrentQueue<byte[]> _incoming = new ConcurrentQueue<byte[]>();
        private readonly BlockingCollection<byte[]> _outgoing = new BlockingCollection<byte[]>(256);
        private volatile Status _state = Status.Connecting;

        public DotNetSocket(string url)
        {
            Task.Run(() => Run(new Uri(url)));
        }

        public override Status State => _state;

        public override void Send(byte[] data, int length)
        {
            if (_state != Status.Open) return;
            var copy = new byte[length];
            Array.Copy(data, copy, length);
            try { _outgoing.TryAdd(copy); }
            catch (InvalidOperationException) { } // closed meanwhile
        }

        public override byte[] Receive() => _incoming.TryDequeue(out var message) ? message : null;

        private async Task Run(Uri uri)
        {
            try
            {
                await _socket.ConnectAsync(uri, _stop.Token);
                _state = Status.Open;
                var sending = Task.Run(SendLoop);
                var buffer = new byte[8192];
                while (!_stop.IsCancellationRequested)
                {
                    int count = 0;
                    WebSocketReceiveResult result;
                    do
                    {
                        if (count == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), _stop.Token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        count += result.Count;
                    } while (!result.EndOfMessage);
                    var message = new byte[count];
                    Array.Copy(buffer, message, count);
                    _incoming.Enqueue(message);
                }
                await sending;
            }
            catch (Exception) { }
            finally
            {
                _state = Status.Closed;
                _outgoing.CompleteAdding();
            }
        }

        private async Task SendLoop()
        {
            try
            {
                foreach (var message in _outgoing.GetConsumingEnumerable(_stop.Token))
                    await _socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, _stop.Token);
            }
            catch (Exception) { }
        }

        public override void Dispose()
        {
            _stop.Cancel();
            _outgoing.CompleteAdding();
            try { _socket.Abort(); } catch (Exception) { }
            _socket.Dispose();
        }
    }
}
