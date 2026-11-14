using System;
using System.Collections.Generic;
using Oxfall.Core.Wildlife;

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
        public const ushort Version = 14;

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
        /// <summary>Longest a chat line may be, in characters.</summary>
        public const int MaxChatLength = 120;

        /// <summary>
        /// A chat line as it may be said: no control characters, runs of spaces made one, trimmed and
        /// cut to <see cref="MaxChatLength"/>. Null if nothing is left.
        /// </summary>
        public static string CleanChat(string text)
        {
            if (text == null) return null;
            var chars = new List<char>(Math.Min(text.Length, MaxChatLength));
            bool space = false;
            foreach (char c in text)
            {
                if (chars.Count >= MaxChatLength) break;
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    space = chars.Count > 0;
                    continue;
                }
                // Rich-text tags would let a line restyle the chat: angle brackets go.
                if (c == '<' || c == '>') continue;
                // Room for the space before it, and for both halves of an emoji; otherwise the line ends here.
                int need = (space ? 1 : 0) + (char.IsHighSurrogate(c) ? 2 : 1);
                if (chars.Count + need > MaxChatLength) break;
                if (space) chars.Add(' ');
                space = false;
                chars.Add(c);
            }
            return chars.Count == 0 ? null : new string(chars.ToArray());
        }

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
        /// <summary>A blow at another player (their id, the tool in hand).</summary>
        Strike = 6,
        /// <summary>A blow at an animal, living (to kill it) or dead (to cut it up): its id, the tool in hand.</summary>
        StrikeAnimal = 7,
        /// <summary>An arrow loosed from the bow: from where, which way (yaw and pitch, degrees).</summary>
        Shoot = 8,
        /// <summary>The player's oldest arrow still in the air landed: on what (none, a player, an animal) and which.</summary>
        ArrowHit = 9,
        /// <summary>Something said to everyone on the server. (Past 9 the server's own ids begin: more client ids carry on from 40.)</summary>
        Chat = 40,
        /// <summary>A blow at a sleeping survivor: their owner number, the tool in hand.</summary>
        StrikeSleeper = 41,
        /// <summary>A satchel charge thrown: what it stuck to, where, and which way that surface faces.</summary>
        Throw = 42,
        /// <summary>A gun fired: which kind, from where, which way (yaw and pitch, degrees), and the seed of its spray.</summary>
        Fire = 43,
        /// <summary>Pellets of the player's last shot hit something: what (a player, an animal, a sleeper), which, how many.</summary>
        GunHit = 44,
        /// <summary>The player reloads the gun of this kind before it is empty.</summary>
        Reload = 45,
        /// <summary>What the player now holds (an item id; empty for bare hands), whenever that changes.</summary>
        Hold = 46,
        /// <summary>Something asked of the player's team (<see cref="Rules.TeamOp"/>) and whom it is about.</summary>
        TeamAct = 47,

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
        /// <summary>Someone was hurt by someone: who, by whom, how much, and whether it killed them.</summary>
        Hurt = 26,
        /// <summary>Where the animals near a player are and what they are doing (a few times a second).</summary>
        Animals = 27,
        /// <summary>What a player's blow at an animal did (to that player alone).</summary>
        AnimalHit = 28,
        /// <summary>An animal hurt a player: who, what kind of animal, how much, and whether it killed them.</summary>
        Bitten = 29,
        /// <summary>Someone loosed an arrow: who, from where, which way (for everyone else to see it fly).</summary>
        Arrow = 30,
        /// <summary>A line of chat: who said it (0: the server), their name, what.</summary>
        ChatLine = 31,
        /// <summary>Every survivor asleep on the island (owner, name, where they lie), whenever that changes.</summary>
        Sleepers = 32,
        /// <summary>A sleeper was hurt: whose body, by whom (id and name), how much, and whether it died.</summary>
        SleeperHit = 33,
        /// <summary>What a player wears (head, chest, legs, feet; empty for nothing), whenever it changes.</summary>
        Outfit = 34,
        /// <summary>A charge stuck somewhere, fizzing: its id, whose, where, which way it faces, seconds left.</summary>
        ChargeArmed = 35,
        /// <summary>A charge went off: its id and where.</summary>
        Blast = 36,
        /// <summary>Someone fired a gun: who, which kind, from where, which way and the seed of its spray (for everyone else).</summary>
        Gunshot = 37,
        /// <summary>What a player holds (an item id; empty for bare hands), whenever that changes.</summary>
        Held = 38,
        /// <summary>The player's team (who is in it, where they are) and who asked them in, whenever that changes and once a second.</summary>
        TeamState = 39,

        // More of the server's own ids carry on from 60.

        /// <summary>A supply plane set off: its line across the island, where the crate comes down, and how long ago.</summary>
        Airdrop = 60,
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
        /// <summary>Everything in the open box that fits into the inventory.</summary>
        TakeAll = 15,
        /// <summary>Test servers only: an animal of kind a a few metres ahead of the player.</summary>
        DevSpawnAnimal = 16,
        /// <summary>Puts on the clothing in slot a, or takes off what is worn in slot a.</summary>
        Wear = 17,
        /// <summary>Test servers only: a supply plane now, dropping ahead of the player.</summary>
        DevAirdrop = 18,
    }

    /// <summary>Where a player stands and which way the body faces (degrees about up).</summary>
    /// <summary>A teammate as the others are told of them: who, their player id while online (0 away), where they last were.</summary>
    public readonly struct TeamMate
    {
        public readonly int Owner;
        public readonly ushort PlayerId;
        public readonly string Name;
        public readonly float X, Z;
        public readonly bool Online, Dead;

        public TeamMate(int owner, ushort playerId, string name, float x, float z, bool online, bool dead)
        {
            Owner = owner;
            PlayerId = playerId;
            Name = name;
            X = x;
            Z = z;
            Online = online;
            Dead = dead;
        }
    }

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
        /// <param name="pitch">How far the player looks up (positive) or down, in degrees: for others to see where they aim.</param>
        public static void Move(NetWriter w, Pose pose, bool teleport, float pitch = 0f)
        {
            w.Reset();
            w.Byte((byte)MessageType.Move);
            pose.Write(w);
            w.Bool(teleport);
            WritePitch(w, pitch);
        }

        public static void ReadMove(NetReader r, out Pose pose, out bool teleport, out float pitch)
        {
            pose = Pose.Read(r);
            teleport = r.Bool();
            pitch = ReadPitch(r);
        }

        /// <summary>A pitch in one byte: whole degrees, -89 to 89.</summary>
        private static void WritePitch(NetWriter w, float pitch)
        {
            float clamped = float.IsNaN(pitch) ? 0f : Math.Max(-89f, Math.Min(89f, pitch));
            w.Byte(unchecked((byte)(sbyte)Math.Round(clamped)));
        }

        private static float ReadPitch(NetReader r) => Math.Max(-89, Math.Min(89, (int)unchecked((sbyte)r.Byte())));

        /// <summary>What the player now holds: an item id, or empty for bare hands.</summary>
        public static void Hold(NetWriter w, string itemId)
        {
            w.Reset();
            w.Byte((byte)MessageType.Hold);
            w.String(itemId ?? "", 64);
        }

        public static string ReadHold(NetReader r) => r.String();

        /// <summary>What a player holds: their id and an item id (empty for bare hands).</summary>
        public static void Held(NetWriter w, ushort playerId, string itemId)
        {
            w.Reset();
            w.Byte((byte)MessageType.Held);
            w.UShort(playerId);
            w.String(itemId ?? "", 64);
        }

        public static void ReadHeld(NetReader r, out ushort playerId, out string itemId)
        {
            playerId = r.UShort();
            itemId = r.String();
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

        /// <summary>Where every player is at <paramref name="serverTime"/>, and who is down (dead).</summary>
        public static void Snapshot(NetWriter w, float serverTime, IReadOnlyList<(ushort Id, Pose Pose, bool Down, float Pitch)> players)
        {
            w.Reset();
            w.Byte((byte)MessageType.Snapshot);
            w.Float(serverTime);
            w.UShort((ushort)players.Count);
            foreach (var (id, pose, down, pitch) in players)
            {
                w.UShort(id);
                pose.Write(w);
                w.Bool(down);
                WritePitch(w, pitch);
            }
        }

        public static float ReadSnapshot(NetReader r, List<(ushort Id, Pose Pose, bool Down, float Pitch)> into)
        {
            into.Clear();
            float time = r.Float();
            int n = r.UShort();
            if (n > Protocol.MaxPlayers) throw new ProtocolException("Too many players in a snapshot.");
            for (int i = 0; i < n; i++) into.Add((r.UShort(), Pose.Read(r), r.Bool(), ReadPitch(r)));
            return time;
        }

        /// <summary>A blow at another player with the tool in hand.</summary>
        public static void Strike(NetWriter w, ushort target, byte tool)
        {
            w.Reset();
            w.Byte((byte)MessageType.Strike);
            w.UShort(target);
            w.Byte(tool);
        }

        public static void ReadStrike(NetReader r, out ushort target, out byte tool)
        {
            target = r.UShort();
            tool = r.Byte();
        }

        /// <summary>Someone hurt someone (attacker 0: nobody, as a fall or hunger).</summary>
        public static void Hurt(NetWriter w, ushort target, ushort attacker, float damage, bool killed)
        {
            w.Reset();
            w.Byte((byte)MessageType.Hurt);
            w.UShort(target);
            w.UShort(attacker);
            w.Byte((byte)Math.Min(255, Math.Max(0, (int)Math.Round(damage))));
            w.Bool(killed);
        }

        public static void ReadHurt(NetReader r, out ushort target, out ushort attacker, out int damage, out bool killed)
        {
            target = r.UShort();
            attacker = r.UShort();
            damage = r.Byte();
            killed = r.Bool();
        }
            // ---- Animals

        public static void StrikeAnimal(NetWriter w, int animal, byte tool)
        {
            w.Reset();
            w.Byte((byte)MessageType.StrikeAnimal);
            w.Int(animal);
            w.Byte(tool);
        }

        public static void ReadStrikeAnimal(NetReader r, out int animal, out byte tool)
        {
            animal = r.Int();
            tool = r.Byte();
        }

        /// <summary>The animals within <paramref name="range"/> of (x, z): where, which way, how fast, how hurt.</summary>
        public static void Animals(NetWriter w, IEnumerable<Animal> animals, float x, float z, float range)
        {
            w.Reset();
            w.Byte((byte)MessageType.Animals);
            int countAt = w.Length;
            w.UShort(0);
            int count = 0;
            float r2 = range * range;
            foreach (var a in animals)
            {
                float dx = a.X - x, dz = a.Z - z;
                if (dx * dx + dz * dz > r2) continue;
                if (count == ushort.MaxValue) break;
                w.Int(a.Id);
                w.Byte((byte)a.Kind);
                w.Byte((byte)a.State);
                w.Float(a.X);
                w.Float(a.Z);
                w.Byte((byte)((int)Math.Round(a.Yaw / 360f * 256f) & 0xFF));
                w.Byte((byte)Math.Min(255, (int)Math.Round(a.Speed * 20f)));
                w.Byte((byte)Math.Min(255, Math.Max(0, (int)Math.Round(a.Health / Wildlife.Animals.MaxHealth(a.Kind) * 255f))));
                w.Byte((byte)(a.Shots & 0xFF));
                count++;
            }
            w.PatchUShort(countAt, (ushort)count);
        }

        public static void ReadAnimals(NetReader r, List<AnimalSnap> into)
        {
            into.Clear();
            int count = r.UShort();
            for (int i = 0; i < count; i++)
            {
                int id = r.Int();
                var kind = (AnimalKind)r.Byte();
                var state = (AnimalState)r.Byte();
                float x = r.Float(), z = r.Float();
                float yaw = r.Byte() / 256f * 360f;
                float speed = r.Byte() / 20f;
                float health = r.Byte() / 255f;
                int shots = r.Byte();
                if (!Wildlife.Animals.IsKnown(kind)) continue;
                into.Add(new AnimalSnap(id, kind, state, x, z, yaw, speed, health, shots));
            }
        }

        public static void AnimalHit(NetWriter w, int animal, AnimalBlow blow, float damage)
        {
            w.Reset();
            w.Byte((byte)MessageType.AnimalHit);
            w.Int(animal);
            w.Byte((byte)blow);
            w.Byte((byte)Math.Min(255, Math.Max(0, (int)Math.Round(damage))));
        }

        public static void ReadAnimalHit(NetReader r, out int animal, out AnimalBlow blow, out int damage)
        {
            animal = r.Int();
            blow = (AnimalBlow)r.Byte();
            damage = r.Byte();
        }

        public static void Bitten(NetWriter w, ushort target, AnimalKind kind, float damage, bool killed)
        {
            w.Reset();
            w.Byte((byte)MessageType.Bitten);
            w.UShort(target);
            w.Byte((byte)kind);
            w.Byte((byte)Math.Min(255, Math.Max(0, (int)Math.Round(damage))));
            w.Bool(killed);
        }

        public static void ReadBitten(NetReader r, out ushort target, out AnimalKind kind, out int damage, out bool killed)
        {
            target = r.UShort();
            kind = (AnimalKind)r.Byte();
            damage = r.Byte();
            killed = r.Bool();
        }
            // ---- Arrows

        public static void Shoot(NetWriter w, float x, float y, float z, float yaw, float pitch)
        {
            w.Reset();
            w.Byte((byte)MessageType.Shoot);
            w.Float(x);
            w.Float(y);
            w.Float(z);
            w.Float(yaw);
            w.Float(pitch);
        }

        public static void ReadShoot(NetReader r, out float x, out float y, out float z, out float yaw, out float pitch)
        {
            x = r.Float();
            y = r.Float();
            z = r.Float();
            yaw = r.Float();
            pitch = r.Float();
        }

        public static void ArrowHit(NetWriter w, Rules.ArrowTarget kind, int target)
        {
            w.Reset();
            w.Byte((byte)MessageType.ArrowHit);
            w.Byte((byte)kind);
            w.Int(target);
        }

        public static void ReadArrowHit(NetReader r, out Rules.ArrowTarget kind, out int target)
        {
            kind = (Rules.ArrowTarget)r.Byte();
            target = r.Int();
        }

        public static void Arrow(NetWriter w, ushort shooter, float x, float y, float z, float yaw, float pitch)
        {
            w.Reset();
            w.Byte((byte)MessageType.Arrow);
            w.UShort(shooter);
            w.Float(x);
            w.Float(y);
            w.Float(z);
            w.Float(yaw);
            w.Float(pitch);
        }

        public static void ReadArrow(NetReader r, out ushort shooter, out float x, out float y, out float z, out float yaw, out float pitch)
        {
            shooter = r.UShort();
            x = r.Float();
            y = r.Float();
            z = r.Float();
            yaw = r.Float();
            pitch = r.Float();
        }

        // ---- Guns

        public static void Fire(NetWriter w, Items.ToolKind tool, float x, float y, float z, float yaw, float pitch, ushort seed)
        {
            w.Reset();
            w.Byte((byte)MessageType.Fire);
            w.Byte((byte)tool);
            w.Float(x);
            w.Float(y);
            w.Float(z);
            w.Float(yaw);
            w.Float(pitch);
            w.UShort(seed);
        }

        public static void ReadFire(NetReader r, out Items.ToolKind tool, out float x, out float y, out float z, out float yaw, out float pitch, out ushort seed)
        {
            tool = (Items.ToolKind)r.Byte();
            x = r.Float();
            y = r.Float();
            z = r.Float();
            yaw = r.Float();
            pitch = r.Float();
            seed = r.UShort();
        }

        public static void GunHit(NetWriter w, Rules.ArrowTarget kind, int target, int pellets)
        {
            w.Reset();
            w.Byte((byte)MessageType.GunHit);
            w.Byte((byte)kind);
            w.Int(target);
            w.Byte((byte)Math.Max(0, Math.Min(255, pellets)));
        }

        public static void ReadGunHit(NetReader r, out Rules.ArrowTarget kind, out int target, out int pellets)
        {
            kind = (Rules.ArrowTarget)r.Byte();
            target = r.Int();
            pellets = r.Byte();
        }

        public static void Reload(NetWriter w, Items.ToolKind tool)
        {
            w.Reset();
            w.Byte((byte)MessageType.Reload);
            w.Byte((byte)tool);
        }

        public static Items.ToolKind ReadReload(NetReader r) => (Items.ToolKind)r.Byte();

        public static void Gunshot(NetWriter w, ushort shooter, Items.ToolKind tool, float x, float y, float z, float yaw, float pitch, ushort seed)
        {
            w.Reset();
            w.Byte((byte)MessageType.Gunshot);
            w.UShort(shooter);
            w.Byte((byte)tool);
            w.Float(x);
            w.Float(y);
            w.Float(z);
            w.Float(yaw);
            w.Float(pitch);
            w.UShort(seed);
        }

        public static void ReadGunshot(NetReader r, out ushort shooter, out Items.ToolKind tool, out float x, out float y, out float z, out float yaw, out float pitch, out ushort seed)
        {
            shooter = r.UShort();
            ReadFire(r, out tool, out x, out y, out z, out yaw, out pitch, out seed);
        }

        // ---- Satchel charges

        public static void Throw(NetWriter w, Rules.ChargeTarget target, float x, float y, float z, float nx, float ny, float nz)
        {
            w.Reset();
            w.Byte((byte)MessageType.Throw);
            target.Write(w);
            w.Float(x);
            w.Float(y);
            w.Float(z);
            WriteDirection(w, nx, ny, nz);
        }

        public static void ReadThrow(NetReader r, out Rules.ChargeTarget target, out float x, out float y, out float z,
            out float nx, out float ny, out float nz)
        {
            target = Rules.ChargeTarget.Read(r);
            x = r.Float();
            y = r.Float();
            z = r.Float();
            ReadDirection(r, out nx, out ny, out nz);
        }

        public static void ChargeArmed(NetWriter w, Rules.LiveCharge c, float secondsLeft)
        {
            w.Reset();
            w.Byte((byte)MessageType.ChargeArmed);
            w.Int(c.Id);
            w.UShort(c.Thrower);
            w.Float(c.X);
            w.Float(c.Y);
            w.Float(c.Z);
            WriteDirection(w, c.NX, c.NY, c.NZ);
            w.Float(secondsLeft);
        }

        public static Rules.LiveCharge ReadChargeArmed(NetReader r, out float secondsLeft)
        {
            int id = r.Int();
            ushort thrower = r.UShort();
            float x = r.Float(), y = r.Float(), z = r.Float();
            ReadDirection(r, out float nx, out float ny, out float nz);
            secondsLeft = r.Float();
            return new Rules.LiveCharge(id, thrower, x, y, z, nx, ny, nz, Rules.ChargeTarget.Ground, secondsLeft);
        }

        public static void Blast(NetWriter w, int id, float x, float y, float z)
        {
            w.Reset();
            w.Byte((byte)MessageType.Blast);
            w.Int(id);
            w.Float(x);
            w.Float(y);
            w.Float(z);
        }

        public static void ReadBlast(NetReader r, out int id, out float x, out float y, out float z)
        {
            id = r.Int();
            x = r.Float();
            y = r.Float();
            z = r.Float();
        }

        /// <summary>A unit direction in three bytes (each axis to about 1%).</summary>
        private static void WriteDirection(NetWriter w, float x, float y, float z)
        {
            w.Byte(Signed(x));
            w.Byte(Signed(y));
            w.Byte(Signed(z));
        }

        private static void ReadDirection(NetReader r, out float x, out float y, out float z)
        {
            x = (sbyte)r.Byte() / 127f;
            y = (sbyte)r.Byte() / 127f;
            z = (sbyte)r.Byte() / 127f;
        }

        private static byte Signed(float v)
        {
            if (float.IsNaN(v)) v = 0f;
            return (byte)(sbyte)Math.Max(-127, Math.Min(127, (int)Math.Round(v * 127f)));
        }

        // ---- Chat

        /// <summary>A line said to everyone, or only to the speaker's team.</summary>
        public static void Chat(NetWriter w, string text, bool team = false)
        {
            w.Reset();
            w.Byte((byte)MessageType.Chat);
            w.String(text ?? "", 512);
            w.Byte((byte)(team ? 1 : 0));
        }

        public static string ReadChat(NetReader r) => ReadChat(r, out _);

        public static string ReadChat(NetReader r, out bool team)
        {
            string text = r.String();
            team = r.Remaining > 0 && r.Byte() != 0;
            return text;
        }

        public static void ChatLine(NetWriter w, ushort from, string name, string text, bool team = false)
        {
            w.Reset();
            w.Byte((byte)MessageType.ChatLine);
            w.UShort(from);
            w.String(name ?? "", 64);
            w.String(text ?? "", 512);
            w.Byte((byte)(team ? 1 : 0));
        }

        public static void ReadChatLine(NetReader r, out ushort from, out string name, out string text) =>
            ReadChatLine(r, out from, out name, out text, out _);

        public static void ReadChatLine(NetReader r, out ushort from, out string name, out string text, out bool team)
        {
            from = r.UShort();
            name = r.String();
            text = r.String();
            team = r.Remaining > 0 && r.Byte() != 0;
        }

        // ---- Supply drops

        /// <summary>A supply plane, as everyone is told of it: <paramref name="ago"/> is how long since it set off.</summary>
        public static void Airdrop(NetWriter w, World.Airdrop a, float ago)
        {
            w.Reset();
            w.Byte((byte)MessageType.Airdrop);
            w.Int(a.Id);
            w.Float(a.FromX);
            w.Float(a.FromZ);
            w.Float(a.ToX);
            w.Float(a.ToZ);
            w.Float(a.DropX);
            w.Float(a.DropZ);
            w.Float(a.GroundY);
            w.Float(ago);
        }

        /// <summary>Reads a supply plane onto this machine's clock (<paramref name="now"/>).</summary>
        public static World.Airdrop ReadAirdrop(NetReader r, float now)
        {
            int id = r.Int();
            float fx = r.Float(), fz = r.Float(), tx = r.Float(), tz = r.Float(), dx = r.Float(), dz = r.Float(), gy = r.Float();
            float ago = r.Float();
            return new World.Airdrop(id, fx, fz, tx, tz, dx, dz, gy, now - ago);
        }

        // ---- Teams

        public static void TeamAct(NetWriter w, Rules.TeamOp op, int arg)
        {
            w.Reset();
            w.Byte((byte)MessageType.TeamAct);
            w.Byte((byte)op);
            w.Int(arg);
        }

        public static void ReadTeamAct(NetReader r, out Rules.TeamOp op, out int arg)
        {
            op = (Rules.TeamOp)r.Byte();
            arg = r.Int();
        }

        /// <summary>A player's team as they are told it: none (id 0), or its leader and members; and who asked them in ("" for nobody).</summary>
        public static void TeamState(NetWriter w, int teamId, int leader, IReadOnlyList<TeamMate> mates, string inviteFrom)
        {
            w.Reset();
            w.Byte((byte)MessageType.TeamState);
            w.Int(teamId);
            w.Int(leader);
            int n = mates == null ? 0 : Math.Min(mates.Count, 16);
            w.Byte((byte)n);
            for (int i = 0; i < n; i++)
            {
                var m = mates[i];
                w.Int(m.Owner);
                w.UShort(m.PlayerId);
                w.String(m.Name ?? "", 64);
                w.Float(m.X);
                w.Float(m.Z);
                w.Byte((byte)((m.Online ? 1 : 0) | (m.Dead ? 2 : 0)));
            }
            w.String(inviteFrom ?? "", 64);
        }

        public static void ReadTeamState(NetReader r, out int teamId, out int leader, List<TeamMate> into, out string inviteFrom)
        {
            teamId = r.Int();
            leader = r.Int();
            into.Clear();
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                int owner = r.Int();
                ushort playerId = r.UShort();
                string name = r.String();
                float x = r.Float(), z = r.Float();
                byte flags = r.Byte();
                into.Add(new TeamMate(owner, playerId, name, x, z, (flags & 1) != 0, (flags & 2) != 0));
            }
            inviteFrom = r.String();
        }

        // ---- Clothing

        /// <summary>The item ids worn in each of an inventory's wear slots, head to feet ("" for none).</summary>
        public static string[] Worn(Items.Inventory inventory, string[] into = null)
        {
            into = into ?? new string[Items.Inventory.WearSlots];
            for (int i = 0; i < into.Length; i++)
            {
                int slot = inventory.SlotFor((Items.WearSlot)(i + 1));
                into[i] = slot >= 0 && !inventory[slot].IsEmpty ? inventory[slot].ItemId : "";
            }
            return into;
        }

        public static void Outfit(NetWriter w, ushort playerId, string[] worn)
        {
            w.Reset();
            w.Byte((byte)MessageType.Outfit);
            w.UShort(playerId);
            w.Byte((byte)worn.Length);
            foreach (var id in worn) w.String(id ?? "", 64);
        }

        public static void ReadOutfit(NetReader r, out ushort playerId, string[] into)
        {
            playerId = r.UShort();
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                string id = r.String();
                if (i < into.Length) into[i] = id;
            }
            for (int i = n; i < into.Length; i++) into[i] = "";
        }

        // ---- Sleepers

        public static void Sleepers(NetWriter w, IReadOnlyList<SurvivorRecord> sleepers)
        {
            w.Reset();
            w.Byte((byte)MessageType.Sleepers);
            int count = Math.Min(sleepers.Count, MaxSleepers);
            w.UShort((ushort)count);
            for (int i = 0; i < count; i++)
            {
                var s = sleepers[i];
                w.Int(s.Owner);
                w.String(s.Name ?? "", Protocol.MaxNameBytes);
                w.Float(s.Pose.X);
                w.Float(s.Pose.Y);
                w.Float(s.Pose.Z);
                w.Byte((byte)((int)Math.Round(s.Pose.Yaw / 360f * 256f) & 0xFF));
            }
        }

        /// <summary>More than this many asleep at once are not all drawn (the first ones are).</summary>
        public const int MaxSleepers = 400;

        public static void ReadSleepers(NetReader r, List<SleeperSnap> into)
        {
            into.Clear();
            int count = r.UShort();
            for (int i = 0; i < count; i++)
            {
                int owner = r.Int();
                string name = r.String();
                float x = r.Float(), y = r.Float(), z = r.Float();
                into.Add(new SleeperSnap(owner, name, x, y, z, r.Byte() * (360f / 256f)));
            }
        }

        public static void StrikeSleeper(NetWriter w, int owner, byte tool)
        {
            w.Reset();
            w.Byte((byte)MessageType.StrikeSleeper);
            w.Int(owner);
            w.Byte(tool);
        }

        public static void ReadStrikeSleeper(NetReader r, out int owner, out byte tool)
        {
            owner = r.Int();
            tool = r.Byte();
        }

        public static void SleeperHit(NetWriter w, int owner, ushort attacker, string by, float damage, bool killed)
        {
            w.Reset();
            w.Byte((byte)MessageType.SleeperHit);
            w.Int(owner);
            w.UShort(attacker);
            w.String(by ?? "", Protocol.MaxNameBytes);
            w.Byte((byte)Math.Min(255, Math.Max(0, (int)Math.Round(damage))));
            w.Bool(killed);
        }

        public static void ReadSleeperHit(NetReader r, out int owner, out ushort attacker, out string by, out int damage, out bool killed)
        {
            owner = r.Int();
            attacker = r.UShort();
            by = r.String();
            damage = r.Byte();
            killed = r.Bool();
        }
    }
}
