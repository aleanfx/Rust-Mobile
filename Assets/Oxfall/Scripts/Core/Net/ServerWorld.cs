using System;
using System.Collections.Generic;
using Oxfall.Core.Crafting;
using Oxfall.Core.Environment;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;
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
            Settlement = new Settlement(Ground, Catalog) { DropsLoot = true };
            // What a destroyed box held, spilled into a bag: everyone is told about the bag.
            Settlement.LootDropped += bag => Announcements.Add(BuildOp.Deploy(bag));
            // And about what came down with the floor under it.
            Settlement.DeployableFell += id => Announcements.Add(BuildOp.DeployableHealth(id, 0f));
            // A box that goes is closed for whoever had it open.
            Settlement.DeployableRemoved += id =>
            {
                foreach (var record in _records.Values)
                    if (record.Survivor.Looting == id) record.Survivor.Looting = 0;
            };
            _random = new Random(seed ^ 0x2c1b3c6d);
            var spawns = ResourceLayout.Generate(Ground);
            foreach (var spawn in spawns)
                _nodes[spawn.Id] = new Node { State = new ResourceNode(spawn.Id, spawn.Type), X = spawn.X, Y = spawn.Y, Z = spawn.Z };
            var obstacles = new Obstacles(spawns);
            Settlement.Obstacles = obstacles;
            Fauna = new Fauna(Ground, seed)
            {
                Blocked = Fauna.Barriers(Settlement.Grid, obstacles, Ground),
                WallBetween = Fauna.Walls(Settlement.Grid),
            };
            Fauna.Populate();
            Fauna.Guard(Fauna.Posts(Ground));
            // A fallen bandit drops what he had (everyone sees the bag through LootDropped).
            Fauna.Died += a =>
            {
                if (a.Kind == AnimalKind.Bandit) Settlement.DropBag(a.X, a.Y, a.Z, a.Yaw, LootTable.Bandit(_random));
            };
        }

        public ServerState Players { get; }
        public IslandGround Ground { get; }
        public ItemCatalog Catalog { get; }
        public RecipeBook Recipes { get; }
        public Settlement Settlement { get; }
        /// <summary>The island's day and night, the same for everyone (a new island starts in the morning).</summary>
        public DayCycle Day { get; } = new DayCycle(9f);
        public int Seed => Players.Seed;
        public int NodeCount => _nodes.Count;
        public IEnumerable<SurvivorRecord> Records => _records.Values;

        /// <summary>Seconds the world has run (the clock for sleeping bags).</summary>
        public float Time { get; private set; }

        /// <summary>Changes that happened on their own (a furnace burning out), to tell everyone; the caller empties it.</summary>
        public List<BuildOp> Announcements { get; } = new List<BuildOp>();

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
            else if (!record.Survivor.Vitals.IsDead) SleepersChanged = true;
            record.Name = player.Name;
            record.Survivor.Looting = 0;
            record.PlayerId = player.Id;
            _online[player.Id] = record;
            // Killed in their sleep (or gone while dead): they come back lying where they fell.
            if (record.Survivor.Vitals.IsDead) Players.SetDown(player.Id, true);
            return record;
        }

        private SurvivorRecord AddRecord(string token, int owner, Pose pose)
        {
            var record = new SurvivorRecord { Token = token, Owner = owner, Survivor = new Survivor(Catalog), Pose = pose };
            record.Survivor.Crafting.Completed += (recipe, lost) => record.Crafted.Add((recipe.Id, lost));
            record.Survivor.Vitals.Died += () => DropBag(record);
            _records.Add(token, record);
            _byOwner[owner] = record;
            return record;
        }

        /// <summary>A survivor died: what they carried goes into a bag where they fell, and everyone sees it.</summary>
        private void DropBag(SurvivorRecord record)
        {
            var pose = record.PlayerId != 0 && Players.TryGet(record.PlayerId, out var player) ? player.Pose : record.Pose;
            if (record.PlayerId != 0) Players.SetDown(record.PlayerId, true);
            if (PlayerActions.DropBag(Settlement, record.Survivor, record.Owner, pose.X, pose.Y, pose.Z, pose.Yaw, out var bag))
                Announcements.Add(BuildOp.Deploy(bag));
        }

        /// <summary>
        /// A player says what they now hold: bare hands, or something on their hotbar. Returns what
        /// everyone else is to see in their hands, or null when it is refused or nothing changed.
        /// </summary>
        public string Hold(ushort playerId, string itemId)
        {
            if (!Players.TryGet(playerId, out var player) || !_online.TryGetValue(playerId, out var record)) return null;
            itemId ??= "";
            if (itemId.Length > 0)
            {
                bool carried = false;
                var inventory = record.Survivor.Inventory;
                for (int i = 0; i < Inventory.HotbarSize && !carried; i++) carried = inventory[i].ItemId == itemId;
                if (!carried) return null;
            }
            if (itemId == player.Held) return null;
            player.Held = itemId;
            return itemId;
        }

        /// <summary>A player goes away; their survivor stays where they stood, asleep (see <see cref="Sleepers"/>).</summary>
        public void Leave(ushort playerId)
        {
            if (_online.TryGetValue(playerId, out var record))
            {
                if (Players.TryGet(playerId, out var player)) record.Pose = player.Pose;
                record.PlayerId = 0;
                record.Survivor.Looting = 0;
                _online.Remove(playerId);
                _arrows.Remove(playerId);
                _lastShot.Remove(playerId);
                _guns.Remove((playerId, ToolKind.Shotgun));
                _guns.Remove((playerId, ToolKind.Revolver));
                // Alive, they fall asleep where they stood.
                if (!record.Survivor.Vitals.IsDead) SleepersChanged = true;
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
            foreach (var y in node.State.Hit(tool, _random))
            {
                _yields.Add((y.ItemId, y.Amount));
                lost += record.Survivor.Inventory.Add(y.ItemId, y.Amount);
            }
            return _yields;
        }

        /// <summary>
        /// One player's blow at another with the tool in hand, judged here: both alive and online,
        /// within reach (with leeway for lag), not faster than the tool swings, and a tool they
        /// really have. The damage is taken off the target, who may die of it (and drop their bag).
        /// False when the blow does not count.
        /// </summary>
        public bool Strike(ushort attackerId, ushort targetId, ToolKind tool, float now, out float damage, out bool killed)
        {
            damage = 0f;
            killed = false;
            if (attackerId == targetId) return false;
            if (!Players.TryGet(attackerId, out var attacker) || !attacker.Placed) return false;
            if (!Players.TryGet(targetId, out var target) || !target.Placed) return false;
            if (!_online.TryGetValue(attackerId, out var a) || !_online.TryGetValue(targetId, out var t)) return false;
            if (a.Survivor.Vitals.IsDead || t.Survivor.Vitals.IsDead) return false;
            if (!Enum.IsDefined(typeof(ToolKind), tool) || !Combat.CanHurt(tool) || !a.Survivor.HasTool(tool)) return false;
            // A little faster than the swing itself, for messages that arrive bunched.
            if (now - attacker.LastHitTime < Combat.SwingSeconds(tool) * 0.75f) return false;
            float reach = Combat.MeleeReach(tool) + Combat.ReachSlack;
            if (attacker.Pose.HorizontalDistance(target.Pose) > reach || Math.Abs(attacker.Pose.Y - target.Pose.Y) > 2.2f) return false;

            attacker.LastHitTime = now;
            damage = t.Survivor.TakeHit(Combat.MeleeDamage(tool));
            killed = t.Survivor.Vitals.IsDead;
            return true;
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
                case ActKind.Wear: return PlayerActions.Wear(s, a);
                case ActKind.Split: return PlayerActions.Split(s, a);
                case ActKind.Craft:
                    return Recipes.TryGet(recipeId ?? "", out var recipe)
                        ? PlayerActions.Craft(s, recipe, Settlement, player.Pose.X, player.Pose.Y, player.Pose.Z) : ActionResult.Invalid;
                case ActKind.CancelCraft: return PlayerActions.CancelCraft(s, a);
                case ActKind.Eat: return PlayerActions.Eat(s, a);
                case ActKind.Drink: return PlayerActions.Drink(s, Ground, player.Pose.X, player.Pose.Y, player.Pose.Z);
                case ActKind.Respawn:
                {
                    if (a != 0)
                    {
                        // In one of their sleeping bags, if it is ready.
                        var bagResult = PlayerActions.RespawnInBag(Settlement, s, record.Owner, a, Time, out var bag);
                        if (bagResult == ActionResult.Ok)
                        {
                            Players.Place(playerId, new Pose(bag.X, bag.Y + 0.1f, bag.Z, bag.Yaw), player.LastMoveTime);
                            Players.SetDown(playerId, false);
                        }
                        return bagResult;
                    }
                    var r = PlayerActions.Respawn(s);
                    if (r == ActionResult.Ok)
                    {
                        Players.Place(playerId, SpawnPose(), player.LastMoveTime);
                        Players.SetDown(playerId, false);
                    }
                    return r;
                }
                case ActKind.Open: return PlayerActions.Open(Settlement, s, record.Owner, a, player.Pose.X, player.Pose.Y, player.Pose.Z);
                case ActKind.Close: return PlayerActions.Close(s);
                case ActKind.Take:
                case ActKind.TakeAll:
                {
                    int looting = s.Looting;
                    var r = kind == ActKind.Take ? PlayerActions.Take(Settlement, s, a, b) : PlayerActions.TakeAll(Settlement, s);
                    // The last thing out of a dead player's bag: the bag is gone for everyone.
                    if (r == ActionResult.Ok && looting != 0 && !Settlement.TryGet(looting, out _)) Announcements.Add(BuildOp.Pickup(looting));
                    return r;
                }
                case ActKind.Put: return PlayerActions.Put(Settlement, s, a, b);
                case ActKind.MoveInBox: return PlayerActions.MoveInBox(Settlement, s, a, b);
                case ActKind.DevGive:
                    if (!DevMode) return ActionResult.Invalid;
                    GiveTestKit(s.Inventory, a);
                    return ActionResult.Ok;
                case ActKind.DevSpawnAnimal:
                {
                    if (!DevMode || !Animals.IsKnown((AnimalKind)a)) return ActionResult.Invalid;
                    float yaw = player.Pose.Yaw * (float)(Math.PI / 180.0);
                    float x = player.Pose.X + (float)Math.Sin(yaw) * 7f, z = player.Pose.Z + (float)Math.Cos(yaw) * 7f;
                    Fauna.Add((AnimalKind)a, x, z, player.Pose.Yaw + 180f);
                    return ActionResult.Ok;
                }
                default: return ActionResult.Invalid;
            }
        }

        /// <summary>
        /// What the game's "give-all" test command hands out, for test servers (kit 1, "give-raid":
        /// satchel charges and what they are made from).
        /// </summary>
        public static void GiveTestKit(Inventory inventory, int kit = 0)
        {
            if (kit == 3)
            {
                // The metal age: a workbench and what its recipes need.
                inventory.Add(ItemIds.Workbench, 1);
                inventory.Add(ItemIds.Scrap, 120);
                inventory.Add(ItemIds.Gears, 3);
                inventory.Add(ItemIds.MetalSpring, 2);
                inventory.Add(ItemIds.MetalPipe, 2);
                inventory.Add(ItemIds.MetalFragments, 400);
                inventory.Add(ItemIds.Wood, 1000);
                inventory.Add(ItemIds.Cloth, 60);
                return;
            }
            if (kit == 2)
            {
                inventory.Add(ItemIds.Blunderbuss, 1);
                inventory.Add(ItemIds.Revolver, 1);
                inventory.Add(ItemIds.Shell, 40);
                inventory.Add(ItemIds.PistolBullet, 60);
                inventory.Add(ItemIds.Bandage, 3);
                inventory.Add(ItemIds.MedicalSyringe, 1);
                return;
            }
            if (kit == 1)
            {
                inventory.Add(ItemIds.SatchelCharge, 8);
                inventory.Add(ItemIds.Gunpowder, 80);
                inventory.Add(ItemIds.Sulfur, 40);
                inventory.Add(ItemIds.SulfurOre, 60);
                inventory.Add(ItemIds.Charcoal, 60);
                return;
            }
            inventory.Add(ItemIds.StoneHatchet, 1);
            inventory.Add(ItemIds.StonePickaxe, 1);
            inventory.Add(ItemIds.BuildingPlan, 1);
            inventory.Add(ItemIds.Campfire, 2);
            inventory.Add(ItemIds.Hammer, 1);
            inventory.Add(ItemIds.WoodenDoor, 1);
            inventory.Add(ItemIds.StorageBox, 1);
            inventory.Add(ItemIds.Furnace, 1);
            inventory.Add(ItemIds.SleepingBag, 1);
            inventory.Add(ItemIds.ToolCupboard, 1);
            inventory.Add(ItemIds.SheetMetalDoor, 1);
            inventory.Add(ItemIds.CodeLock, 2);
            inventory.Add(ItemIds.MetalHatchet, 1);
            inventory.Add(ItemIds.MetalPickaxe, 1);
            inventory.Add(ItemIds.WoodenSpear, 1);
            inventory.Add(ItemIds.WoodenBow, 1);
            inventory.Add(ItemIds.Arrow, 30);
            inventory.Add(ItemIds.HideHood, 1);
            inventory.Add(ItemIds.HideVest, 1);
            inventory.Add(ItemIds.HidePants, 1);
            inventory.Add(ItemIds.HideBoots, 1);
            inventory.Add(ItemIds.Wood, 1500);
            inventory.Add(ItemIds.Stone, 700);
            inventory.Add(ItemIds.MetalOre, 240);
            inventory.Add(ItemIds.Cloth, 40);
            inventory.Add(ItemIds.Leather, 40);
            inventory.Add(ItemIds.MetalFragments, 600);
        }

        /// <summary>Something a player builds or does to a building; <paramref name="done"/> is to be told to all.</summary>
        public ActionResult Build(ushort playerId, BuildOp op, out BuildOp done)
        {
            done = op;
            if (!_online.TryGetValue(playerId, out var record) || !Players.TryGet(playerId, out var player)) return ActionResult.Invalid;
            var p = player.Pose;
            return PlayerActions.Build(Settlement, record.Survivor, record.Owner, op, p.X, p.Y, p.Z, out done);
        }

        /// <summary>
        /// Time passes: furnaces burn, and for the players online crafting moves on, food and water
        /// run down, and a box left behind closes.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;
            Time += deltaSeconds;
            TickCharges();
            Day.Tick(deltaSeconds);
            _wentOut.Clear();
            _rotted.Clear();
            Settlement.Tick(deltaSeconds, _wentOut, _rotted);
            foreach (int id in _wentOut) Announcements.Add(BuildOp.Light(id, false));
            foreach (int id in _rotted) Announcements.Add(BuildOp.Pickup(id));
            TickFauna(deltaSeconds);
            foreach (var record in _online.Values)
            {
                var s = record.Survivor;
                if (s.Vitals.IsDead) continue;
                s.Crafting.Tick(deltaSeconds);
                bool found = Players.TryGet(record.PlayerId, out var player);
                float exertion = found && player.Speed > RunningSpeed ? 1.8f : 1f;
                s.Vitals.Tick(deltaSeconds, exertion);
                if (s.Looting != 0 && found && Settlement.TryGet(s.Looting, out var box))
                {
                    float dx = player.Pose.X - box.X, dz = player.Pose.Z - box.Z, reach = PlayerActions.UseReach + 2f;
                    if (dx * dx + dz * dz > reach * reach || Math.Abs(player.Pose.Y - box.Y) > reach) s.Looting = 0;
                    // Locked with a code this player does not know (a new code, or locked again): out.
                    else if (!Settlement.CanPass(LockTarget.On(box.Id), record.Owner)) s.Looting = 0;
                }
            }
        }

        private readonly List<int> _wentOut = new List<int>();
        private readonly List<int> _rotted = new List<int>();

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
