using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Oxfall.Core.Net;

namespace Oxfall.Server
{
    /// <summary>
    /// The running game: one <see cref="ServerState"/> shared by every connection, guarded by a lock,
    /// and a loop that sends everyone a snapshot <see cref="Protocol.TickRate"/> times a second.
    /// </summary>
    public sealed class GameServer
    {
        private const int MaxMessageBytes = 4096;
        private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);
        private const float CorrectionInterval = 0.5f;

        private readonly ServerState _state;
        private readonly object _lock = new object();
        private readonly Dictionary<ushort, Connection> _connections = new Dictionary<ushort, Connection>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly ILogger _log;

        public GameServer(int seed, ILogger log)
        {
            _state = new ServerState(seed);
            _log = log;
        }

        public int Seed => _state.Seed;

        public int PlayerCount
        {
            get { lock (_lock) return _state.Count; }
        }

        private float Now => (float)_clock.Elapsed.TotalSeconds;

        /// <summary>Sends a snapshot to everyone, every tick, until stopped.</summary>
        public async Task RunTicks(CancellationToken stop)
        {
            var writer = new NetWriter(2048);
            var period = TimeSpan.FromSeconds(1.0 / Protocol.TickRate);
            using var timer = new PeriodicTimer(period);
            while (await timer.WaitForNextTickAsync(stop))
            {
                byte[] snapshot;
                Connection[] targets;
                lock (_lock)
                {
                    if (_connections.Count == 0) continue;
                    Messages.Snapshot(writer, Now, _state.Snapshot());
                    snapshot = writer.ToArray();
                    targets = new Connection[_connections.Count];
                    _connections.Values.CopyTo(targets, 0);
                }
                foreach (var c in targets) c.Send(snapshot);
            }
        }

        /// <summary>Serves one player from the handshake until they leave or break the protocol.</summary>
        public async Task Serve(WebSocket socket, string address, CancellationToken stop)
        {
            var connection = new Connection(socket);
            var sending = connection.RunSends(stop);
            var writer = new NetWriter();
            var buffer = new byte[MaxMessageBytes];
            ushort id = 0;
            try
            {
                // The first message must be Hello, and soon.
                using (var helloWait = CancellationTokenSource.CreateLinkedTokenSource(stop))
                {
                    helloWait.CancelAfter(HelloTimeout);
                    int n = await Receive(socket, buffer, helloWait.Token);
                    if (n <= 0 || Protocol.PeekType(buffer, n) != MessageType.Hello) return;
                    Messages.ReadHello(new NetReader(buffer, 1, n - 1), out var version, out var name);
                    if (!TryJoin(connection, writer, version, name, out id)) return;
                    _log.LogInformation("Player {Id} '{Name}' joined from {Address} ({Count} online)", id, name, address, PlayerCount);
                }

                float lastCorrection = -1f;
                while (!stop.IsCancellationRequested)
                {
                    int n = await Receive(socket, buffer, stop);
                    if (n <= 0) break;
                    switch (Protocol.PeekType(buffer, n))
                    {
                        case MessageType.Move:
                            Messages.ReadMove(new NetReader(buffer, 1, n - 1), out var pose, out bool teleport);
                            lock (_lock)
                            {
                                float now = Now;
                                if (_state.ApplyMove(id, pose, teleport, now) || now - lastCorrection < CorrectionInterval) break;
                                lastCorrection = now;
                                _state.TryGet(id, out var player);
                                Messages.Correction(writer, player.Pose);
                                connection.Send(writer.ToArray());
                            }
                            break;
                        default:
                            throw new ProtocolException("A client may not send " + buffer[0] + ".");
                    }
                }
            }
            catch (ProtocolException e)
            {
                _log.LogWarning("Player {Id} from {Address} broke the protocol: {Message}", id, address, e.Message);
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            finally
            {
                if (id != 0) Leave(id, writer);
                connection.Complete();
                await sending;
                if (socket.State == WebSocketState.Open)
                {
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                    catch (WebSocketException) { }
                }
            }
        }

        private bool TryJoin(Connection connection, NetWriter writer, ushort version, string name, out ushort id)
        {
            lock (_lock)
            {
                var player = _state.Join(version, name, Now, out var reason);
                if (player == null)
                {
                    Messages.Rejected(writer, reason);
                    connection.Send(writer.ToArray());
                    id = 0;
                    return false;
                }
                id = player.Id;
                Messages.Welcome(writer, player.Id, _state.Seed, Now);
                connection.Send(writer.ToArray());
                // The newcomer learns who is here; everyone else learns about the newcomer.
                foreach (var other in _state.Players)
                {
                    if (other.Id == player.Id) continue;
                    Messages.Joined(writer, other.Id, other.Name, other.Pose);
                    connection.Send(writer.ToArray());
                }
                Messages.Joined(writer, player.Id, player.Name, player.Pose);
                var joined = writer.ToArray();
                foreach (var c in _connections.Values) c.Send(joined);
                _connections[player.Id] = connection;
                return true;
            }
        }

        private void Leave(ushort id, NetWriter writer)
        {
            lock (_lock)
            {
                _state.Leave(id);
                _connections.Remove(id);
                Messages.Left(writer, id);
                var left = writer.ToArray();
                foreach (var c in _connections.Values) c.Send(left);
            }
            _log.LogInformation("Player {Id} left ({Count} online)", id, PlayerCount);
        }

        /// <summary>One whole binary message into the buffer; 0 when the client closed.</summary>
        private static async Task<int> Receive(WebSocket socket, byte[] buffer, CancellationToken token)
        {
            int count = 0;
            while (true)
            {
                if (count >= buffer.Length) throw new ProtocolException("Message too large.");
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), token);
                if (result.MessageType == WebSocketMessageType.Close) return 0;
                if (result.MessageType != WebSocketMessageType.Binary) throw new ProtocolException("Text frames are not used.");
                count += result.Count;
                if (result.EndOfMessage) return count;
            }
        }

        /// <summary>
        /// One client's outgoing messages, sent one at a time in order. A client that cannot keep up
        /// loses its oldest messages rather than holding up the server.
        /// </summary>
        private sealed class Connection
        {
            private readonly WebSocket _socket;
            private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

            public Connection(WebSocket socket) => _socket = socket;

            public void Send(byte[] message) => _outgoing.Writer.TryWrite(message);

            public void Complete() => _outgoing.Writer.TryComplete();

            public async Task RunSends(CancellationToken stop)
            {
                try
                {
                    await foreach (var message in _outgoing.Reader.ReadAllAsync(stop))
                    {
                        if (_socket.State != WebSocketState.Open) return;
                        await _socket.SendAsync(message, WebSocketMessageType.Binary, true, stop);
                    }
                }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
            }
        }
    }
}
