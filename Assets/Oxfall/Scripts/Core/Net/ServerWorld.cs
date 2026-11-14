using System;
using System.Collections.Generic;
using Oxfall.Core.Crafting;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Net
{
    /// <summary>
    /// One player's survivor as the server keeps it, online or not: found again by their token
    /// when they come back, with what they carry and where they stood.
    /// </summary>
    public sealed class SurvivorRecord
    {
        public string Token { get; internal set; }
        public string Name { get; internal set; }
        /// <summary>Marks what they build as theirs (never 0, which means nobody).</summary>
        public int Owner { get; internal set; }
        public Survivor Survivor { get; internal set; }
        public Pose Pose { get; internal set; }
        /// <summary>The player id while online, 0 while away.</summary>
        public ushort PlayerId { get; internal set; }
        /// <summary>Jobs finished since last told: (recipe id, how many did not fit).</summary>
        public List<(string RecipeId, int Lost)> Crafted { get; } = new List<(string, int)>();
    }

    /// <summary>
    /// The shared island on the server, with no sockets in it so it can be tested on its own: the
    /// players (<see cref="ServerState"/>), every tree, rock and plant laid out from the seed exactly
    /// as the game lays them out, what players have built, and each survivor's belongings. Every
    /// action goes through <see cref="PlayerActions"/>, the same rules the game runs alone.
    /// </summary>
    public sealed partial class ServerWorld
    {
        /// <summary>How far a player can reach a node (the game's reach, plus room for lag).</summary>
        public const float HitReach = 6f;

        /// <summary>The quickest swing the game allows is 0.7 s; a little less is let through for jitter.</summary>
        public const float MinHitInterval = 0.5f;

        /// <summary>Faster than walking (4 m/s): sprinting, which makes food and water go faster.</summary>
        public const float RunningSpeed = 5.2f;

        private sealed class Node
        {
            public ResourceNode State;
            public float X, Y, Z;
        }

        private readonly Dictionary<int, Node> _nodes = new Dictionary<int, Node>();
        private readonly Dictionary<string, SurvivorRecord> _records = new Dictionary<string, SurvivorRecord>();
        private readonly Dictionary<ushort, SurvivorRecord> _online = new Dictionary<ushort, SurvivorRecord>();
        private readonly List<(int, int)> _changed = new List<(int, int)>();
        private readonly List<(string, int)> _yields = new List<(string, int)>();
        private readonly Random _random;
        private int _nextOwner = 1;

        public ServerWorld(int seed)
        {
            Players = new ServerState(seed);
            Ground = new IslandGround(seed);
            Catalog = ItemCatalog.CreateDefault();
            Recipes = RecipeBook.CreateDefault(Catalog);
            Settlement = new Settlement(Ground);
            _random = new Random(seed ^ 0x2c1b3c6d);
            foreach (var spawn in ResourceLayout.Generate(Ground))
                _nodes[spawn.Id] = new Node { State = new ResourceNode(spawn.Id, spawn.Type), X = spawn.X, Y = spawn.Y, Z = spawn.Z };
        }

        public ServerState Players { get; }
        public IslandGround Ground { get; }
        public ItemCatalog Catalog { get; }
        public RecipeBook Recipes { get; }
        public Settlement Settlement { get; }
        public int Seed => Players.Seed;
        public int NodeCount => _nodes.Count;
        public IEnumerable<SurvivorRecord> Records => _records.Values;

        /// <summary>
        /// For tests: everyone wakes up on the same beach (the one the game picks alone) and clients
        /// may move their player anywhere. Never on a real server.
        /// </summary>
        public bool DevMode
        {
            get => Players.AllowTeleports;
            set => Players.AllowTeleports = value;
        }

        public bool TryGetRecord(ushort playerId, out SurvivorRecord record) => _online.TryGetValue(playerId, out record);

        /// <summary>
        /// A player comes in with their token: their survivor as they left it, or a fresh one on a
        /// beach. If the same token is still online (a phone that dropped and came back), that old
        /// session is ended first: <paramref name="replaced"/> is its player id, or 0.
        /// </summary>
        public SurvivorRecord Join(ushort version, string name, string token, float now,
            out ServerState.Player player, out ushort replaced, out string reason)
        {
            player = null;
            replaced = 0;
            if (version == Protocol.Version && !Protocol.IsValidToken(token))
            {
                reason = "No se pudo identificar tu partida: vuelve a abrir el juego.";
                return null;
            }

            _records.TryGetValue(token ?? "", out var record);
            if (record != null && record.PlayerId != 0)
            {
                replaced = record.PlayerId;
                Leave(replaced);
            }
            var pose = record != null ? record.Pose : SpawnPose();
            player = Players.Join(version, name, pose, now, out reason);
            if (player == null)
            {
                replaced = 0;
                return null;
            }

            if (record == null) record = AddRecord(token, _nextOwner++, pose);
            record.Name = player.Name;
            record.PlayerId = player.Id;
            _online[player.Id] = record;
            return record;
        }

        private SurvivorRecord AddRecord(string token, int owner, Pose pose)
        {
            var record = new SurvivorRecord { Token = token, Owner = owner, Survivor = new Survivor(Catalog), Pose = pose };
            record.Survivor.Crafting.Completed += (recipe, lost) => record.Crafted.Add((recipe.Id, lost));
            _records.Add(token, record);
            return record;
        }

        /// <summary>A player goes away; their survivor stays, where they stood.</summary>
        public void Leave(ushort playerId)
        {
            if (_online.TryGetValue(playerId, out var record))
            {
                if (Players.TryGet(playerId, out var player)) record.Pose = player.Pose;
                record.PlayerId = 0;
                _online.Remove(playerId);
            }
            Players.Leave(playerId);
        }

        /// <summary>A beach to wake up on: a different one each time, or always the same one in dev mode.</summary>
        public Pose SpawnPose()
        {
            var random = DevMode ? new Random(Seed ^ 0x5f3759df) : _random;
            var (x, y, z, yaw) = Spawns.Beach(Ground, random);
            return new Pose(x, y, z, yaw);
        }

        public bool TryGetNode(int id, out ResourceNode node, out float x, out float z)
        {
            if (_nodes.TryGetValue(id, out var n))
            {
                node = n.State;
                x = n.X;
                z = n.Z;
                return true;
            }
            node = null;
            x = z = 0f;
            return false;
        }

        /// <summary>
        /// A player's swing at a node. What it gives goes into their inventory; returns it (empty when
        /// it gave nothing or was not allowed: too far, too fast, dead, no such node, or a tool they do
        /// not have). <paramref name="lost"/> is how much did not fit.
        /// </summary>
        public IReadOnlyList<(string ItemId, int Amount)> Hit(ushort playerId, int nodeId, ToolKind tool, float now, out int lost)
        {
            _yields.Clear();
            lost = 0;
            if (!Players.TryGet(playerId, out var player) || !player.Placed) return _yields;
            if (!_online.TryGetValue(playerId, out var record) || record.Survivor.Vitals.IsDead) return _yields;
            if (!_nodes.TryGetValue(nodeId, out var node)) return _yields;
            if (now - player.LastHitTime < MinHitInterval) return _yields;
            float dx = player.Pose.X - node.X, dz = player.Pose.Z - node.Z;
            if (dx * dx + dz * dz > HitReach * HitReach || Math.Abs(player.Pose.Y - node.Y) > HitReach) return _yields;
            if (!Enum.IsDefined(typeof(ToolKind), tool) || !record.Survivor.HasTool(tool)) return _yields;

            player.LastHitTime = now;
            foreach (var y in node.State.Hit(tool))
            {
                _yields.Add((y.ItemId, y.Amount));
                lost += record.Survivor.Inventory.Add(y.ItemId, y.Amount);
            }
            return _yields;
        }

        /// <summary>Something a player does to their own belongings.</summary>
        public ActionResult Act(ushort playerId, ActKind kind, int a, int b, string recipeId)
        {
            if (!_online.TryGetValue(playerId, out var record) || !Players.TryGet(playerId, out var player)) return ActionResult.Invalid;
            var s = record.Survivor;
            switch (kind)
            {
                case ActKind.MoveSlot: return PlayerActions.MoveSlot(s, a, b);
                case ActKind.QuickMove: return PlayerActions.QuickMove(s, a);
                case ActKind.Split: return PlayerActions.Split(s, a);
                case ActKind.Craft:
                    return Recipes.TryGet(recipeId ?? "", out var recipe) ? PlayerActions.Craft(s, recipe) : ActionResult.Invalid;
                case ActKind.CancelCraft: return PlayerActions.CancelCraft(s, a);
                case ActKind.Eat: return PlayerActions.Eat(s, a);
                case ActKind.Drink: return PlayerActions.Drink(s, Ground, player.Pose.X, player.Pose.Y, player.Pose.Z);
                case ActKind.Respawn:
                {
                    var r = PlayerActions.Respawn(s);
                    if (r == ActionResult.Ok) Players.Place(playerId, SpawnPose(), player.LastMoveTime);
                    return r;
                }
                case ActKind.DevGive:
                    if (!DevMode) return ActionResult.Invalid;
                    GiveTestKit(s.Inventory);
                    return ActionResult.Ok;
                default: return ActionResult.Invalid;
            }
        }

        /// <summary>What the game's "give-all" test command hands out, for test servers.</summary>
        public static void GiveTestKit(Inventory inventory)
        {
            inventory.Add(ItemIds.StoneHatchet, 1);
            inventory.Add(ItemIds.StonePickaxe, 1);
            inventory.Add(ItemIds.BuildingPlan, 1);
            inventory.Add(ItemIds.Campfire, 2);
            inventory.Add(ItemIds.Hammer, 1);
            inventory.Add(ItemIds.WoodenDoor, 1);
            inventory.Add(ItemIds.Wood, 1500);
            inventory.Add(ItemIds.Stone, 700);
            inventory.Add(ItemIds.MetalOre, 240);
            inventory.Add(ItemIds.Cloth, 40);
        }

        /// <summary>Something a player builds or does to a building; <paramref name="done"/> is to be told to all.</summary>
        public ActionResult Build(ushort playerId, BuildOp op, out BuildOp done)
        {
            done = op;
            if (!_online.TryGetValue(playerId, out var record) || !Players.TryGet(playerId, out var player)) return ActionResult.Invalid;
            var p = player.Pose;
            return PlayerActions.Build(Settlement, record.Survivor, record.Owner, op, p.X, p.Y, p.Z, out done);
        }

        /// <summary>Time passes for the players online: crafting moves on, food and water run down.</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;
            foreach (var record in _online.Values)
            {
                var s = record.Survivor;
                if (s.Vitals.IsDead) continue;
                s.Crafting.Tick(deltaSeconds);
                float exertion = Players.TryGet(record.PlayerId, out var player) && player.Speed > RunningSpeed ? 1.8f : 1f;
                s.Vitals.Tick(deltaSeconds, exertion);
            }
        }

        /// <summary>Grows depleted nodes back; returns the ones that came back this tick (id, amount).</summary>
        public IReadOnlyList<(int Id, int Remaining)> TickRespawn(float deltaSeconds)
        {
            _changed.Clear();
            foreach (var n in _nodes.Values)
                if (n.State.TickRespawn(deltaSeconds)) _changed.Add((n.State.Id, n.State.Remaining));
            return _changed;
        }

        /// <summary>Every node that is not full, for a player who has just joined.</summary>
        public List<(int Id, int Remaining)> NotFull()
        {
            var list = new List<(int, int)>();
            foreach (var n in _nodes.Values)
                if (n.State.Remaining < n.State.Capacity) list.Add((n.State.Id, n.State.Remaining));
            return list;
        }
    }
}
