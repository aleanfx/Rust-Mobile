using System;
using System.Collections.Generic;

namespace Oxfall.Core.Net
{
    /// <summary>
    /// What the game client and the server say to each other. Every message is one WebSocket binary
    /// frame: a <see cref="MessageType"/> byte, then that message's fields in a fixed order. Both
    /// sides compile this same file, so they can never disagree on the format.
    /// </summary>
    public static class Protocol
    {
        /// <summary>Raised whenever a message changes shape; a client of another version is turned away.</summary>
        public const ushort Version = 2;

        /// <summary>Snapshots per second the server sends.</summary>
        public const int TickRate = 20;

        /// <summary>Moves per second a client sends.</summary>
        public const int SendRate = 15;

        public const int MaxNameBytes = 32;
        public const int MaxPlayers = 100;

        public static MessageType PeekType(byte[] data, int count)
        {
            if (count < 1) throw new ProtocolException("Empty message.");
            var type = (MessageType)data[0];
            if (!Enum.IsDefined(typeof(MessageType), type)) throw new ProtocolException("Unknown message " + data[0] + ".");
            return type;
        }

        /// <summary>Trims a chosen name to something showable: letters, digits, spaces; 2 to 16 characters.</summary>
        public static string CleanName(string name)
        {
            var chars = new List<char>();
            foreach (char c in name ?? "")
                if ((char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-') && chars.Count < 16) chars.Add(c);
            var clean = new string(chars.ToArray()).Trim();
            return clean.Length >= 2 ? clean : "Superviviente";
        }
    }

    public enum MessageType : byte
    {
        // Client to server
        Hello = 1,
        Move = 2,
        Hit = 3,

        // Server to client
        Welcome = 10,
        Rejected = 11,
        Joined = 12,
        Left = 13,
        Snapshot = 14,
        Correction = 15,
        NodeStates = 16,
        Gathered = 17,
    }

    /// <summary>Where a player stands and which way the body faces (degrees about up).</summary>
    public readonly struct Pose : IEquatable<Pose>
    {
        public readonly float X, Y, Z, Yaw;

        public Pose(float x, float y, float z, float yaw)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }

        public void Write(NetWriter w)
        {
            w.Float(X);
            w.Float(Y);
            w.Float(Z);
            w.Float(Yaw);
        }

        public static Pose Read(NetReader r) => new Pose(r.Float(), r.Float(), r.Float(), r.Float());

        public bool IsFinite => !float.IsNaN(X + Y + Z + Yaw) && !float.IsInfinity(X + Y + Z + Yaw);

        public float HorizontalDistance(Pose other)
        {
            float dx = X - other.X, dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Part way from a to b; yaw turns the short way round.</summary>
        public static Pose Lerp(Pose a, Pose b, float t)
        {
            float dyaw = ((b.Yaw - a.Yaw) % 360f + 540f) % 360f - 180f;
            return new Pose(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.Yaw + dyaw * t);
        }

        public bool Equals(Pose o) => X == o.X && Y == o.Y && Z == o.Z && Yaw == o.Yaw;
        public override bool Equals(object obj) => obj is Pose p && Equals(p);
        public override int GetHashCode() => HashCode(X, Y, Z, Yaw);
        public override string ToString() => $"({X:0.0}, {Y:0.0}, {Z:0.0}) {Yaw:0}°";

        private static int HashCode(float a, float b, float c, float d) =>
            ((a.GetHashCode() * 31 + b.GetHashCode()) * 31 + c.GetHashCode()) * 31 + d.GetHashCode();
    }

    /// <summary>The messages, each with its writer and reader. Readers start after the type byte.</summary>
    public static class Messages
    {
        // ---- Client to server

        public static void Hello(NetWriter w, string name)
        {
            w.Reset();
            w.Byte((byte)MessageType.Hello);
            w.UShort(Protocol.Version);
            w.String(name, Protocol.MaxNameBytes);
        }

        public static void ReadHello(NetReader r, out ushort version, out string name)
        {
            version = r.UShort();
            name = r.String();
        }

        /// <param name="teleport">The client moved the player on purpose (woke up on the beach).</param>
        public static void Move(NetWriter w, Pose pose, bool teleport)
        {
            w.Reset();
            w.Byte((byte)MessageType.Move);
            pose.Write(w);
            w.Bool(teleport);
        }

        public static void ReadMove(NetReader r, out Pose pose, out bool teleport)
        {
            pose = Pose.Read(r);
            teleport = r.Bool();
        }

        /// <summary>The player swung at a tree, rock or plant with this tool.</summary>
        public static void Hit(NetWriter w, int nodeId, byte tool)
        {
            w.Reset();
            w.Byte((byte)MessageType.Hit);
            w.Int(nodeId);
            w.Byte(tool);
        }

        public static void ReadHit(NetReader r, out int nodeId, out byte tool)
        {
            nodeId = r.Int();
            tool = r.Byte();
        }

        // ---- Server to client

        /// <summary>How much is left in some nodes (0: gone until it grows back).</summary>
        public static void NodeStates(NetWriter w, IReadOnlyList<(int Id, int Remaining)> nodes)
        {
            w.Reset();
            w.Byte((byte)MessageType.NodeStates);
            w.UShort((ushort)nodes.Count);
            foreach (var (id, remaining) in nodes)
            {
                w.Int(id);
                w.UShort((ushort)Math.Max(0, Math.Min(ushort.MaxValue, remaining)));
            }
        }

        public static void ReadNodeStates(NetReader r, List<(int Id, int Remaining)> into)
        {
            into.Clear();
            int n = r.UShort();
            for (int i = 0; i < n; i++) into.Add((r.Int(), r.UShort()));
        }

        /// <summary>What a hit gave this player.</summary>
        public static void Gathered(NetWriter w, int nodeId, IReadOnlyList<(string ItemId, int Amount)> items)
        {
            w.Reset();
            w.Byte((byte)MessageType.Gathered);
            w.Int(nodeId);
            w.Byte((byte)items.Count);
            foreach (var (item, amount) in items)
            {
                w.String(item, 64);
                w.UShort((ushort)amount);
            }
        }

        public static int ReadGathered(NetReader r, List<(string ItemId, int Amount)> into)
        {
            into.Clear();
            int id = r.Int();
            int n = r.Byte();
            for (int i = 0; i < n; i++) into.Add((r.String(), r.UShort()));
            return id;
        }

        public static void Welcome(NetWriter w, ushort playerId, int seed, float serverTime)
        {
            w.Reset();
            w.Byte((byte)MessageType.Welcome);
            w.UShort(playerId);
            w.Int(seed);
            w.Float(serverTime);
        }

        public static void ReadWelcome(NetReader r, out ushort playerId, out int seed, out float serverTime)
        {
            playerId = r.UShort();
            seed = r.Int();
            serverTime = r.Float();
        }

        public static void Rejected(NetWriter w, string reason)
        {
            w.Reset();
            w.Byte((byte)MessageType.Rejected);
            w.String(reason);
        }

        public static string ReadRejected(NetReader r) => r.String();

        public static void Joined(NetWriter w, ushort playerId, string name, Pose pose)
        {
            w.Reset();
            w.Byte((byte)MessageType.Joined);
            w.UShort(playerId);
            w.String(name, Protocol.MaxNameBytes);
            pose.Write(w);
        }

        public static void ReadJoined(NetReader r, out ushort playerId, out string name, out Pose pose)
        {
            playerId = r.UShort();
            name = r.String();
            pose = Pose.Read(r);
        }

        public static void Left(NetWriter w, ushort playerId)
        {
            w.Reset();
            w.Byte((byte)MessageType.Left);
            w.UShort(playerId);
        }

        public static ushort ReadLeft(NetReader r) => r.UShort();

        /// <summary>The server did not believe a move: the player is put back here.</summary>
        public static void Correction(NetWriter w, Pose pose)
        {
            w.Reset();
            w.Byte((byte)MessageType.Correction);
            pose.Write(w);
        }

        public static Pose ReadCorrection(NetReader r) => Pose.Read(r);

        /// <summary>Where every player is at <paramref name="serverTime"/>.</summary>
        public static void Snapshot(NetWriter w, float serverTime, IReadOnlyList<(ushort Id, Pose Pose)> players)
        {
            w.Reset();
            w.Byte((byte)MessageType.Snapshot);
            w.Float(serverTime);
            w.UShort((ushort)players.Count);
            foreach (var (id, pose) in players)
            {
                w.UShort(id);
                pose.Write(w);
            }
        }

        public static float ReadSnapshot(NetReader r, List<(ushort Id, Pose Pose)> into)
        {
            into.Clear();
            float time = r.Float();
            int n = r.UShort();
            if (n > Protocol.MaxPlayers) throw new ProtocolException("Too many players in a snapshot.");
            for (int i = 0; i < n; i++) into.Add((r.UShort(), Pose.Read(r)));
            return time;
        }
    }
}
