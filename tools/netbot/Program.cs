using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Oxfall.Core.Net;

// Usage: dotnet run --project tools/netbot -- [ws://localhost:7777/ws] [name] [seconds]
// Prints "[bot] Welcome ..." once in, then "[bot] Swung <player> <impact>" and "[bot] Gunshot <player>" as they come.
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string url = args.Length > 0 ? args[0] : "ws://localhost:7777/ws";
        string name = args.Length > 1 ? args[1] : "Bot";
        float seconds = args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 120f;
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), CancellationToken.None);
        var w = new NetWriter();
        Messages.Hello(w, name, Guid.NewGuid().ToString("N"));
        await socket.SendAsync(new ArraySegment<byte>(w.ToArray()), WebSocketMessageType.Binary, true, CancellationToken.None);
        var buffer = new byte[1 << 20];
        var end = DateTime.UtcNow.AddSeconds(seconds);
        Pose pose = default;
        bool welcomed = false;
        var nextMove = DateTime.UtcNow;
        while (DateTime.UtcNow < end && socket.State == WebSocketState.Open)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            int count = 0;
            try
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), cts.Token);
                    count += result.Count;
                } while (!result.EndOfMessage);
            }
            catch (OperationCanceledException) { count = 0; }
            if (count > 0)
            {
                var r = new NetReader(buffer, 0, count);
                var type = (MessageType)r.Byte();
                switch (type)
                {
                    case MessageType.Welcome:
                        Messages.ReadWelcome(r, out ushort id, out _, out _, out pose, out _);
                        welcomed = true;
                        Console.WriteLine($"[bot] Welcome as {id} at ({pose.X:0.0}, {pose.Y:0.0}, {pose.Z:0.0})");
                        break;
                    case MessageType.Rejected:
                        Console.WriteLine("[bot] Rejected");
                        return 1;
                    case MessageType.Swung:
                        Messages.ReadSwung(r, out ushort who, out var impact);
                        Console.WriteLine($"[bot] Swung {who} {impact}");
                        break;
                    case MessageType.Gunshot:
                        Console.WriteLine($"[bot] Gunshot {r.UShort()}");
                        break;
                }
            }
            // Stays where it is, saying so now and then so it is not taken for gone.
            if (welcomed && DateTime.UtcNow >= nextMove)
            {
                nextMove = DateTime.UtcNow.AddSeconds(1);
                Messages.Move(w, pose, false);
                await socket.SendAsync(new ArraySegment<byte>(w.ToArray()), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
        }
        Console.WriteLine("[bot] Done");
        return 0;
    }
}
