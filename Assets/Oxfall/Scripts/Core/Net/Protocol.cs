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
        public const ushort Version = 5;

        /// <summary>Snapshots per second the server sends.</summary>
        public const int TickRate = 20;

        /// <summary>Moves per second a client sends.</summary>
        public const int SendRate = 15;

        public const int MaxNameBytes = 32;
        public const int MaxTokenBytes = 64;
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

        /// <summary>
        /// Whether a player's secret token looks like one the game makes (16 to 64 letters and digits).
        /// It is what keeps their survivor theirs between visits.
        /// </summary>
        public static bool IsValidToken(string token)
        {
            if (token == null || token.Length < 16 || token.Length > MaxTokenBytes) return false;
            foreach (char c in token)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
            return true;
        }
    }

    public enum MessageType : byte
    {
        // Client to server
        Hello = 1,
        Move = 2,
        Hit = 3,
        Act = 4,
        Build = 5,

        // Server to client
        Welcome = 10,
        Rejected = 11,
        Joined = 12,
        Left = 13,
        Snapshot = 14,
        Correction = 15,
        NodeStates = 16,
        Gathered = 17,
        Slots = 18,
        Crafts = 19,
        Crafted = 20,
        VitalsState = 21,
        Failed = 22,
        Built = 23,
        TimeOfDay = 24,
        Loot = 25,
    }

    /// <summary>What a player does to their own belongings (inside an <see cref="MessageType.Act"/> message).</summary>
    public enum ActKind : byte
    {
        MoveSlot = 1,
        QuickMove = 2,
        Split = 3,
        Craft = 4,
        CancelCraft = 5,
        Eat = 6,
        Drink = 7,
        Respawn = 8,
        /// <summary>Test servers only: a kit of tools and materials. A real server refuses it.</summary>
        DevGive = 9,
        /// <summary>Opens a box or furnace (a: its id).</summary>
        Open = 10,
        Close = 11,
        /// <summary>From the open box's slot a to inventory slot b (-1: wherever it fits).</summary>
        Take = 12,
        /// <summary>From inventory slot a to the open box's slot b (-1: wherever it fits).</summary>
        Put = 13,
        /// <summary>Within the open box, slot a onto slot b.</summary>
        MoveInBox = 14,
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

        /// <param name="token">The player's own secret, kept on the phone, that brings back their survivor.</param>
        public static void Hello(NetWriter w, string name, string token)
        {
            w.Reset();
            w.Byte((byte)MessageType.Hello);
            w.UShort(Protocol.Version);
            w.String(name, Protocol.MaxNameBytes);
            w.String(token ?? "", Protocol.MaxTokenBytes);
        }

        /// <summary>Reads a Hello. An older client's Hello ends after the version; name and token are then empty.</summary>
        public static void ReadHello(NetReader r, out ushort version, out string name, out string token)
        {
            version = r.UShort();
            name = token = "";
            if (version != Protocol.Version) return;
            name = r.String();
            token = r.String();
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

        /// <summary>Something done to the player's own belongings: a slot or index, or a recipe.</summary>
        public static void Act(NetWriter w, ActKind kind, int a = 0, int b = 0, string recipe = null)
        {
            w.Reset();
            w.Byte((byte)MessageType.Act);
            w.Byte((byte)kind);
            switch (kind)
            {
                case ActKind.MoveSlot:
                    w.Byte((byte)a);
                    w.Byte((byte)b);
                    break;
                case ActKind.QuickMove:
                case ActKind.Split:
                case ActKind.CancelCraft:
                case ActKind.Eat:
                    w.Byte((byte)a);
                    break;
                case ActKind.Craft:
                    w.String(recipe ?? "", 64);
                    break;
                case ActKind.Respawn:
                case ActKind.Open:
                    w.Int(a);
                    break;
                case ActKind.Take:
                case ActKind.Put:
                case ActKind.MoveInBox:
                    w.Byte(SlotByte(a));
                    w.Byte(SlotByte(b));
                    break;
            }
        }

        // A slot, or 255 for "anywhere" (-1).
        private static byte SlotByte(int slot) => slot < 0 ? (byte)255 : (byte)slot;
        private static int ByteSlot(byte b) => b == 255 ? -1 : b;

        public static ActKind ReadAct(NetReader r, out int a, out int b, out string recipe)
        {
            var kind = (ActKind)r.Byte();
            a = b = 0;
            recipe = null;
            switch (kind)
            {
                case ActKind.MoveSlot:
                    a = r.Byte();
                    b = r.Byte();
                    break;
                case ActKind.Respawn:
                case ActKind.Open:
                    a = r.Int();
                    break;
                case ActKind.Take:
                case ActKind.Put:
                case ActKind.MoveInBox:
                    a = ByteSlot(r.Byte());
                    b = ByteSlot(r.Byte());
                    break;
                case ActKind.QuickMove:
                case ActKind.Split:
                case ActKind.CancelCraft:
                case ActKind.Eat:
                    a = r.Byte();
                    break;
                case ActKind.Craft:
                    recipe = r.String();
                    break;
                case ActKind.Drink:
                case ActKind.DevGive:
                case ActKind.Close:
                    break;
                default:
                    throw new ProtocolException("Unknown action " + (byte)kind + ".");
            }
            return kind;
        }

        /// <summary>A change the player asks to make to what stands on the island.</summary>
        public static void Build(NetWriter w, Rules.BuildOp op)
        {
            w.Reset();
            w.Byte((byte)MessageType.Build);
            op.Write(w);
        }

        public static Rules.BuildOp ReadBuild(NetReader r) => Rules.BuildOp.Read(r);

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

        /// <param name="pose">Where the player is: where they were last time, or a beach for a newcomer.</param>
        /// <param name="owner">What marks the player's buildings, bags and cupboards as theirs (it stays the same between visits).</param>
        public static void Welcome(NetWriter w, ushort playerId, int seed, float serverTime, Pose pose, int owner)
        {
            w.Reset();
            w.Byte((byte)MessageType.Welcome);
            w.UShort(playerId);
            w.Int(seed);
            w.Float(serverTime);
            pose.Write(w);
            w.Int(owner);
        }

        public static void ReadWelcome(NetReader r, out ushort playerId, out int seed, out float serverTime, out Pose pose, out int owner)
        {
            playerId = r.UShort();
            seed = r.Int();
            serverTime = r.Float();
            pose = Pose.Read(r);
            owner = r.Int();
        }

        /// <summary>
        /// The box or furnace the player has open: its id, how many slots, and the slots that changed
        /// (all of them when just opened). An id of 0 says it closed (out of reach, or gone).
        /// </summary>
        public static void Loot(NetWriter w, int id, int slotCount, IReadOnlyList<(int Slot, Items.ItemStack Stack)> slots)
        {
            w.Reset();
            w.Byte((byte)MessageType.Loot);
            w.Int(id);
            w.Byte((byte)slotCount);
            w.Byte((byte)slots.Count);
            foreach (var (slot, stack) in slots) WriteStack(w, slot, stack);
        }

        public static int ReadLoot(NetReader r, out int slotCount, List<(int Slot, Items.ItemStack Stack)> into)
        {
            into.Clear();
            int id = r.Int();
            slotCount = r.Byte();
            int n = r.Byte();
            for (int i = 0; i < n; i++) into.Add(ReadStack(r));
            return id;
        }

        private static void WriteStack(NetWriter w, int slot, Items.ItemStack stack)
        {
            w.Byte((byte)slot);
            if (stack.IsEmpty)
            {
                w.String("");
                return;
            }
            w.String(stack.ItemId, 64);
            w.UShort((ushort)Math.Min(ushort.MaxValue, stack.Amount));
            w.Int(stack.SkinId);
        }

        private static (int, Items.ItemStack) ReadStack(NetReader r)
        {
            int slot = r.Byte();
            string id = r.String();
            if (id.Length == 0) return (slot, Items.ItemStack.Empty);
            int amount = r.UShort();
            int skin = r.Int();
            return (slot, amount > 0 ? new Items.ItemStack(id, amount, skin) : Items.ItemStack.Empty);
        }

        /// <summary>Inventory slots that changed: (slot, stack), an empty item id for an empty slot.</summary>
        public static void Slots(NetWriter w, IReadOnlyList<(int Slot, Items.ItemStack Stack)> slots)
        {
            w.Reset();
            w.Byte((byte)MessageType.Slots);
            w.Byte((byte)slots.Count);
            foreach (var (slot, stack) in slots)
            {
                w.Byte((byte)slot);
                if (stack.IsEmpty)
                {
                    w.String("");
                    continue;
                }
                w.String(stack.ItemId, 64);
                w.UShort((ushort)Math.Min(ushort.MaxValue, stack.Amount));
                w.Int(stack.SkinId);
            }
        }

        public static void ReadSlots(NetReader r, List<(int Slot, Items.ItemStack Stack)> into)
        {
            into.Clear();
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                int slot = r.Byte();
                string id = r.String();
                if (id.Length == 0)
                {
                    into.Add((slot, Items.ItemStack.Empty));
                    continue;
                }
                int amount = r.UShort();
                int skin = r.Int();
                into.Add((slot, amount > 0 ? new Items.ItemStack(id, amount, skin) : Items.ItemStack.Empty));
            }
        }

        /// <summary>The crafting queue as it is now: each job's recipe and seconds done.</summary>
        public static void Crafts(NetWriter w, IReadOnlyList<Crafting.CraftingQueue.Job> jobs)
        {
            w.Reset();
            w.Byte((byte)MessageType.Crafts);
            w.Byte((byte)jobs.Count);
            foreach (var job in jobs)
            {
                w.String(job.Recipe.Id, 64);
                w.Float(job.Elapsed);
            }
        }

        public static void ReadCrafts(NetReader r, List<(string RecipeId, float Elapsed)> into)
        {
            into.Clear();
            int n = r.Byte();
            if (n > Crafting.CraftingQueue.MaxJobs) throw new ProtocolException("Too many crafting jobs.");
            for (int i = 0; i < n; i++) into.Add((r.String(), r.Float()));
        }

        /// <summary>A job finished; <paramref name="lost"/> of what it made did not fit.</summary>
        public static void Crafted(NetWriter w, string recipeId, int lost)
        {
            w.Reset();
            w.Byte((byte)MessageType.Crafted);
            w.String(recipeId, 64);
            w.UShort((ushort)Math.Max(0, Math.Min(ushort.MaxValue, lost)));
        }

        public static string ReadCrafted(NetReader r, out int lost)
        {
            string id = r.String();
            lost = r.UShort();
            return id;
        }

        public static void Vitals(NetWriter w, float health, float food, float water)
        {
            w.Reset();
            w.Byte((byte)MessageType.VitalsState);
            w.Float(health);
            w.Float(food);
            w.Float(water);
        }

        public static void ReadVitals(NetReader r, out float health, out float food, out float water)
        {
            health = r.Float();
            food = r.Float();
            water = r.Float();
        }

        /// <summary>The island's clock (0 to 24), so everyone has the same day and night.</summary>
        public static void TimeOfDay(NetWriter w, float hours)
        {
            w.Reset();
            w.Byte((byte)MessageType.TimeOfDay);
            w.Float(hours);
        }

        public static float ReadTimeOfDay(NetReader r)
        {
            float hours = r.Float();
            if (float.IsNaN(hours) || hours < 0f || hours >= 24.001f) throw new ProtocolException("Bad time of day.");
            return hours;
        }

        /// <summary>Something the player asked for did not happen, and why.</summary>
        public static void Failed(NetWriter w, Rules.ActionResult why)
        {
            w.Reset();
            w.Byte((byte)MessageType.Failed);
            w.Byte((byte)why);
        }

        public static Rules.ActionResult ReadFailed(NetReader r) => (Rules.ActionResult)r.Byte();

        /// <summary>Changes made to what stands on the island, in order, to apply to every copy.</summary>
        public static void Built(NetWriter w, IReadOnlyList<Rules.BuildOp> ops, int start = 0, int count = -1)
        {
            if (count < 0) count = ops.Count - start;
            w.Reset();
            w.Byte((byte)MessageType.Built);
            w.UShort((ushort)count);
            for (int i = start; i < start + count; i++) ops[i].Write(w);
        }

        public static void ReadBuilt(NetReader r, List<Rules.BuildOp> into)
        {
            into.Clear();
            int n = r.UShort();
            for (int i = 0; i < n; i++) into.Add(Rules.BuildOp.Read(r));
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
