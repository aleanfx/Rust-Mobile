using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    public class PlayerActionsTests
    {
        private const int Seed = 20261113;
        private const float S = BuildingGrid.CellSize;

        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        /// <summary>A settlement, a well-off survivor, and a cell near the spawn where a floor fits.</summary>
        private static (Settlement settlement, Survivor survivor, Cell cell, float x, float y, float z) Setup()
        {
            var ground = new IslandGround(Seed);
            var settlement = new Settlement(ground);
            var survivor = new Survivor(Catalog);
            survivor.Inventory.Add(ItemIds.Wood, 5000);
            survivor.Inventory.Add(ItemIds.Stone, 3000);
            var spawn = Spawns.Beach(ground, new System.Random(Seed ^ 0x5f3759df));
            var near = BuildingGrid.CellAt(spawn.X, spawn.Z);
            for (int r = 1; r < 30; r++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                var cell = new Cell(near.X + dx, near.Z + dz);
                // Room for a neighbour on the east too.
                if (PlayerActions.FoundationTop(settlement, cell, out float top) != ActionResult.Ok) continue;
                if (PlayerActions.FoundationTop(settlement, new Cell(cell.X + 1, cell.Z), out _) != ActionResult.Ok) continue;
                float x = (cell.X + 0.5f) * S, z = (cell.Z - 0.6f) * S;
                return (settlement, survivor, cell, x, ground.Height(x, z), z);
            }
            Assert.Fail("No buildable cell near the spawn.");
            return default;
        }

        private static ActionResult Build(Settlement w, Survivor s, BuildOp op, float x, float y, float z, int owner = 1) =>
            PlayerActions.Build(w, s, owner, op, x, y, z, out _);

        [Test]
        public void AFoundationCostsWoodAndTheServerSetsItsHeight()
        {
            var (w, s, cell, x, y, z) = Setup();
            PlayerActions.FoundationTop(w, cell, out float top);
            var r = PlayerActions.Build(w, s, 1, BuildOp.Foundation(cell, 999f), x, y, z, out var done);
            Assert.AreEqual(ActionResult.Ok, r);
            Assert.AreEqual(top, done.Top, "the height the client sent is ignored");
            Assert.IsTrue(w.Grid.TryGetFoundationTop(cell, out float placed));
            Assert.AreEqual(top, placed);
            Assert.AreEqual(5000 - BuildCosts.TwigWood, s.Inventory.Count(ItemIds.Wood));
            Assert.AreEqual(1, w.OwnerOf(PieceRef.Foundation(cell)));
            Assert.AreEqual(ActionResult.Occupied, Build(w, s, BuildOp.Foundation(cell, 0f), x, y, z));
        }

        [Test]
        public void BuildingFarAwayOrWithoutWoodIsRefused()
        {
            var (w, s, cell, x, y, z) = Setup();
            Assert.AreEqual(ActionResult.TooFar, Build(w, s, BuildOp.Foundation(cell, 0f), x + 40f, y, z));
            var poor = new Survivor(Catalog);
            Assert.AreEqual(ActionResult.NotEnoughResources, Build(w, poor, BuildOp.Foundation(cell, 0f), x, y, z));
            Assert.AreEqual(0, w.Grid.PieceCount, "nothing was built");
            Assert.AreEqual(ActionResult.NeedsFoundation, Build(w, s, BuildOp.Edge(cell.North, PieceType.Wall), x, y, z));
            Assert.AreEqual(ActionResult.Invalid, Build(w, s, BuildOp.Edge(cell.North, PieceType.Roof), x, y, z));
        }

        [Test]
        public void OnlyTheBuilderCanHammerTheirPieces()
        {
            var (w, s, cell, x, y, z) = Setup();
            Build(w, s, BuildOp.Foundation(cell, 0f), x, y, z, owner: 1);
            var piece = PieceRef.Foundation(cell);
            var other = new Survivor(Catalog);
            other.Inventory.Add(ItemIds.Wood, 1000);
            Assert.AreEqual(ActionResult.NotYours, Build(w, other, BuildOp.Upgrade(piece, BuildingTier.Twig), x, y, z, owner: 2));
            Assert.AreEqual(ActionResult.NotYours, Build(w, other, BuildOp.Remove(piece), x, y, z, owner: 2));

            var r = PlayerActions.Build(w, s, 1, BuildOp.Upgrade(piece, BuildingTier.Twig), x, y, z, out var done);
            Assert.AreEqual(ActionResult.Ok, r);
            Assert.AreEqual(BuildingTier.Wood, done.Tier, "the next tier, whatever the client asked");
            Assert.AreEqual(5000 - BuildCosts.TwigWood - BuildCosts.WoodUpgrade, s.Inventory.Count(ItemIds.Wood));
            Build(w, s, BuildOp.Upgrade(piece, BuildingTier.Twig), x, y, z);
            Assert.AreEqual(ActionResult.MaxTier, Build(w, s, BuildOp.Upgrade(piece, BuildingTier.Twig), x, y, z));
        }

        [Test]
        public void DemolishingGivesHalfBackAndTheDoor()
        {
            var (w, s, cell, x, y, z) = Setup();
            Build(w, s, BuildOp.Foundation(cell, 0f), x, y, z);
            Build(w, s, BuildOp.Edge(cell.South, PieceType.Doorway), x, y, z);
            s.Inventory.Add(ItemIds.WoodenDoor, 1);
            Assert.AreEqual(ActionResult.Ok, Build(w, s, BuildOp.Door(cell.South), x, y, z));
            Assert.AreEqual(0, s.Inventory.Count(ItemIds.WoodenDoor));
            Assert.AreEqual(ActionResult.Ok, Build(w, s, BuildOp.ToggleDoor(cell.South, false), x, y, z));
            Assert.IsTrue(w.Grid.IsDoorOpen(cell.South));

            int wood = s.Inventory.Count(ItemIds.Wood);
            var r = PlayerActions.Build(w, s, 1, BuildOp.Remove(PieceRef.Foundation(cell)), x, y, z, out var done);
            Assert.AreEqual(ActionResult.Ok, r);
            Assert.AreEqual(BuildOpKind.Remove, done.Kind);
            Assert.AreEqual(0, w.Grid.PieceCount, "the doorway fell with the floor");
            Assert.AreEqual(wood + 2 * (BuildCosts.TwigWood / 2), s.Inventory.Count(ItemIds.Wood));
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.WoodenDoor));
            Assert.AreEqual(0, w.OwnerOf(PieceRef.Foundation(cell)), "no owner left for a piece that is gone");
        }

        [Test]
        public void ACampfireGoesOnTheGroundNearby()
        {
            var (w, s, cell, x, y, z) = Setup();
            s.Inventory.Add(ItemIds.Campfire, 2);
            var ground = w.Ground;
            float fx = x + 1.5f, fz = z;
            var fire = new Campfire(0, fx, ground.Height(fx, fz), fz, 30f);
            var r = PlayerActions.Build(w, s, 1, BuildOp.PlaceCampfire(fire), x, y, z, out var done);
            Assert.AreEqual(ActionResult.Ok, r);
            Assert.AreEqual(1, done.Fire.Id);
            Assert.AreEqual(1, w.CampfireCount);
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.Campfire));
            var floating = new Campfire(0, fx, ground.Height(fx, fz) + 5f, fz, 0f);
            Assert.AreEqual(ActionResult.Invalid, Build(w, s, BuildOp.PlaceCampfire(floating), x, y, z));
            Assert.AreEqual(ActionResult.TooFar, Build(w, s, BuildOp.PlaceCampfire(fire), x + 30f, y, z));
        }

        [Test]
        public void ACopyBuiltFromTheDescriptionMatches()
        {
            var (w, s, cell, x, y, z) = Setup();
            var east = new Cell(cell.X + 1, cell.Z);
            Build(w, s, BuildOp.Foundation(cell, 0f), x, y, z);
            Build(w, s, BuildOp.Foundation(east, 0f), x, y, z);
            Build(w, s, BuildOp.Edge(cell.West, PieceType.Wall), x, y, z);
            Build(w, s, BuildOp.Edge(cell.South, PieceType.Doorway), x, y, z);
            Build(w, s, BuildOp.Roof(cell), x, y, z);
            Build(w, s, BuildOp.Upgrade(PieceRef.OnEdge(cell.West, PieceType.Wall), BuildingTier.Twig), x, y, z);
            Build(w, s, BuildOp.Upgrade(PieceRef.OnEdge(cell.West, PieceType.Wall), BuildingTier.Twig), x, y, z);
            s.Inventory.Add(ItemIds.WoodenDoor, 1);
            s.Inventory.Add(ItemIds.Campfire, 1);
            Build(w, s, BuildOp.Door(cell.South), x, y, z);
            Build(w, s, BuildOp.ToggleDoor(cell.South, true), x, y, z);
            Build(w, s, BuildOp.PlaceCampfire(new Campfire(0, x, w.Ground.Height(x, z), z, 0f)), x, y, z);

            // Through the wire, as a joining player gets it.
            var ops = BuildOps.Describe(w);
            var writer = new NetWriter();
            Messages.Built(writer, ops);
            var bytes = writer.ToArray();
            var read = new List<BuildOp>();
            Messages.ReadBuilt(new NetReader(bytes, 1, bytes.Length - 1), read);

            var copy = new Settlement(new IslandGround(Seed));
            foreach (var op in read) Assert.IsTrue(BuildOps.Apply(copy, op), op.ToString());
            Assert.AreEqual(w.Grid.PieceCount, copy.Grid.PieceCount);
            Assert.AreEqual(5, copy.Grid.PieceCount);
            Assert.IsTrue(copy.Grid.TryGetTier(PieceRef.OnEdge(cell.West, PieceType.Wall), out var tier));
            Assert.AreEqual(BuildingTier.Stone, tier);
            Assert.IsTrue(copy.Grid.IsDoorOpen(cell.South));
            Assert.AreEqual(1, copy.CampfireCount);
            w.Grid.TryGetFoundationTop(east, out float top);
            copy.Grid.TryGetFoundationTop(east, out float copyTop);
            Assert.AreEqual(top, copyTop);

            copy.Clear();
            Assert.AreEqual(0, copy.Grid.PieceCount);
            Assert.AreEqual(0, copy.CampfireCount);
        }

        [Test]
        public void EatingTakesFromTheSlotAndDrinkingNeedsAPond()
        {
            var ground = new IslandGround(Seed);
            var s = new Survivor(Catalog);
            s.Vitals.Set(100f, 100f, 100f);
            s.Inventory.SetSlot(3, new ItemStack(ItemIds.Berries, 5));
            Assert.AreEqual(ActionResult.NotFood, PlayerActions.Eat(s, 0), "a rock");
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Eat(s, 3));
            Assert.AreEqual(4, s.Inventory[3].Amount);
            Assert.Greater(s.Vitals.Food, 100f);
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Eat(s, 99));

            Assume.That(ground.Ponds.Count, Is.GreaterThan(0));
            var pond = ground.Ponds[0];
            float water = s.Vitals.Water;
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Drink(s, ground, pond.X + pond.Radius + 1f, pond.Level + 0.5f, pond.Z));
            Assert.AreEqual(water + PlayerActions.DrinkWater, s.Vitals.Water, 1e-3);
            Assert.AreEqual(ActionResult.NoWater, PlayerActions.Drink(s, ground, pond.X + pond.Radius + 40f, pond.Level, pond.Z));
        }

        [Test]
        public void TheDeadCanOnlyWakeUpAndLoseWhatTheyCarried()
        {
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.Wood, 500);
            PlayerActions.Craft(s, RecipeBook.CreateDefault(Catalog).Get("campfire"));
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Respawn(s), "not dead");
            s.Vitals.Damage(1000f);
            Assert.AreEqual(ActionResult.Dead, PlayerActions.MoveSlot(s, 0, 1));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Respawn(s));
            Assert.IsFalse(s.Vitals.IsDead);
            Assert.AreEqual(0, s.Inventory.Count(ItemIds.Wood));
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.Rock));
            Assert.AreEqual(0, s.Crafting.Jobs.Count);
        }
    }

    public class BuildOpTests
    {
        [Test]
        public void EveryKindRoundTrips()
        {
            var edge = new Edge(3, -4, EdgeAxis.AlongZ);
            var ops = new[]
            {
                BuildOp.Foundation(new Cell(-2, 7), 4.25f),
                BuildOp.Edge(edge, PieceType.Doorway),
                BuildOp.Roof(new Cell(1, 1)),
                BuildOp.Upgrade(PieceRef.OnEdge(edge, PieceType.Wall), BuildingTier.Stone),
                BuildOp.Remove(PieceRef.Roof(new Cell(5, 6))),
                BuildOp.Door(edge),
                BuildOp.ToggleDoor(edge, true),
                BuildOp.PlaceCampfire(new Campfire(12, 1.5f, 2.5f, -3.5f, 90f)),
            };
            foreach (var op in ops)
            {
                var w = new NetWriter();
                Messages.Build(w, op);
                var bytes = w.ToArray();
                Assert.AreEqual(MessageType.Build, Protocol.PeekType(bytes, bytes.Length));
                var back = Messages.ReadBuild(new NetReader(bytes, 1, bytes.Length - 1));
                Assert.AreEqual(op.Kind, back.Kind);
                Assert.AreEqual(op.ToString(), back.ToString());
                Assert.AreEqual(op.Top, back.Top);
                Assert.AreEqual(op.Tier, back.Tier);
                Assert.AreEqual(op.Open, back.Open);
                Assert.AreEqual(op.Fire.Id, back.Fire.Id);
                Assert.AreEqual(op.Fire.Yaw, back.Fire.Yaw);
            }
        }

        [Test]
        public void NonsenseIsRefused()
        {
            Assert.Throws<ProtocolException>(() => BuildOp.Read(new NetReader(new byte[] { 99 })));
            // An edge with a made-up axis.
            var w = new NetWriter();
            w.Byte((byte)BuildOpKind.Edge);
            w.Int(0);
            w.Int(0);
            w.Byte(7);
            w.Byte((byte)PieceType.Wall);
            Assert.Throws<ProtocolException>(() => BuildOp.Read(new NetReader(w.ToArray())));
            // A floor a world away.
            w.Reset();
            w.Byte((byte)BuildOpKind.Foundation);
            w.Int(1_000_000);
            w.Int(0);
            w.Float(1f);
            Assert.Throws<ProtocolException>(() => BuildOp.Read(new NetReader(w.ToArray())));
        }
    }

    public class SurvivorSyncTests
    {
        [Test]
        public void OnlyWhatChangedIsSent()
        {
            var s = new Survivor(ItemCatalog.CreateDefault());
            var sync = new SurvivorSync();
            var w = new NetWriter();
            var slots = new List<(int Slot, ItemStack Stack)>();

            Assert.IsTrue(sync.Slots(s, w), "the starter kit");
            Messages.ReadSlots(Reader(w), slots);
            Assert.AreEqual(2, slots.Count);
            Assert.IsFalse(sync.Slots(s, w), "nothing new");

            s.Inventory.Move(0, 10);
            Assert.IsTrue(sync.Slots(s, w));
            Messages.ReadSlots(Reader(w), slots);
            CollectionAssert.AreEquivalent(new[] { 0, 10 }, slots.Select(p => p.Slot));
            Assert.IsTrue(slots.Single(p => p.Slot == 0).Stack.IsEmpty);
            Assert.AreEqual(ItemIds.Rock, slots.Single(p => p.Slot == 10).Stack.ItemId);

            // The copy on the phone ends up the same.
            var copy = new Survivor(ItemCatalog.CreateDefault());
            copy.Inventory.Clear();
            sync.Reset();
            sync.Slots(s, w);
            Messages.ReadSlots(Reader(w), slots);
            copy.Inventory.SetSlots(slots);
            for (int i = 0; i < s.Inventory.SlotCount; i++) Assert.AreEqual(s.Inventory[i], copy.Inventory[i]);
        }

        [Test]
        public void TheQueueIsSentWhenAJobStartsOrEnds()
        {
            var catalog = ItemCatalog.CreateDefault();
            var recipes = RecipeBook.CreateDefault(catalog);
            var s = new Survivor(catalog);
            s.Inventory.Add(ItemIds.Wood, 300);
            var sync = new SurvivorSync();
            var w = new NetWriter();
            Assert.IsTrue(sync.Crafts(s, w), "the empty queue, once");
            Assert.IsFalse(sync.Crafts(s, w));
            PlayerActions.Craft(s, recipes.Get("campfire"));
            Assert.IsTrue(sync.Crafts(s, w));
            s.Crafting.Tick(1f);
            Assert.IsFalse(sync.Crafts(s, w), "progress alone is not sent");

            var jobs = new List<(string RecipeId, float Elapsed)>();
            Messages.ReadCrafts(Reader(w), jobs);
            Assert.AreEqual(("campfire", 0f), jobs.Single());

            // A copy of the queue moves the bar but never finishes on its own.
            var mirror = new Survivor(catalog).Crafting;
            mirror.Mirror = true;
            mirror.Load(jobs.Select(j => (recipes.Get(j.RecipeId), j.Elapsed)));
            mirror.Tick(100f);
            Assert.AreEqual(1, mirror.Jobs.Count);
            Assert.AreEqual(1f, mirror.Jobs[0].Progress);

            s.Crafting.Tick(10f);
            Assert.IsTrue(sync.Crafts(s, w), "the job finished");
        }

        [Test]
        public void VitalsGoEveryFewSecondsOrWhenTheyJump()
        {
            var s = new Survivor(ItemCatalog.CreateDefault());
            var sync = new SurvivorSync();
            var w = new NetWriter();
            Assert.IsTrue(sync.Vitals(s, w, 0f));
            s.Vitals.Tick(1f);
            Assert.IsFalse(sync.Vitals(s, w, 1f), "a slow change waits");
            Assert.IsTrue(sync.Vitals(s, w, 2.5f));
            s.Vitals.Damage(1000f);
            Assert.IsTrue(sync.Vitals(s, w, 2.6f), "death goes at once");
            Messages.ReadVitals(Reader(w), out float health, out _, out _);
            Assert.AreEqual(0f, health);
        }

        private static NetReader Reader(NetWriter w)
        {
            var bytes = w.ToArray();
            return new NetReader(bytes, 1, bytes.Length - 1);
        }
    }

    public class ServerSurvivorTests
    {
        private const string TokenA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string TokenB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        [Test]
        public void ComingBackWithTheTokenBringsTheSurvivorBack()
        {
            var world = new ServerWorld(20261113);
            var record = world.Join(Protocol.Version, "Ale", TokenA, 0f, out var player, out var replaced, out _);
            Assert.AreEqual(0, replaced);
            record.Survivor.Inventory.Add(ItemIds.Wood, 77);
            var there = new Pose(player.Pose.X + 3f, player.Pose.Y, player.Pose.Z, 0f);
            world.Players.ApplyMove(player.Id, there, false, 1f);
            world.Leave(player.Id);
            Assert.AreEqual(0, world.Players.Count);

            var again = world.Join(Protocol.Version, "Ale", TokenA, 5f, out var back, out _, out _);
            Assert.AreSame(record, again);
            Assert.AreEqual(77, again.Survivor.Inventory.Count(ItemIds.Wood));
            Assert.AreEqual(there, back.Pose, "where they stood");

            var other = world.Join(Protocol.Version, "Bea", TokenB, 6f, out _, out _, out _);
            Assert.AreNotSame(record, other);
            Assert.AreNotEqual(record.Owner, other.Owner);
            Assert.IsNull(world.Join(Protocol.Version, "Mal", "bad token", 7f, out _, out _, out var why));
            Assert.IsNotNull(why);
        }

        [Test]
        public void TheSameTokenAgainReplacesTheOldSession()
        {
            var world = new ServerWorld(20261113);
            world.Join(Protocol.Version, "Ale", TokenA, 0f, out var first, out _, out _);
            world.Join(Protocol.Version, "Ale", TokenA, 1f, out var second, out var replaced, out _);
            Assert.AreEqual(first.Id, replaced);
            Assert.AreEqual(1, world.Players.Count);
            Assert.IsFalse(world.TryGetRecord(first.Id, out _));
            world.Leave(first.Id);
            Assert.IsTrue(world.TryGetRecord(second.Id, out _), "the old session leaving late changes nothing");
        }

        [Test]
        public void CraftingRunsOnTheServerAndWakingUpPicksABeach()
        {
            var world = new ServerWorld(20261113);
            var record = world.Join(Protocol.Version, "Ale", TokenA, 0f, out var player, out _, out _);
            record.Survivor.Inventory.Add(ItemIds.Wood, 100);
            Assert.AreEqual(ActionResult.Ok, world.Act(player.Id, ActKind.Craft, 0, 0, "campfire"));
            Assert.AreEqual(ActionResult.Invalid, world.Act(player.Id, ActKind.Craft, 0, 0, "nuke"));
            Assert.AreEqual(ActionResult.NotEnoughResources, world.Act(player.Id, ActKind.Craft, 0, 0, "campfire"));
            for (int i = 0; i < 120; i++) world.Tick(0.05f);
            Assert.AreEqual(1, record.Survivor.Inventory.Count(ItemIds.Campfire));
            Assert.AreEqual(("campfire", 0), record.Crafted.Single());

            float food = record.Survivor.Vitals.Food;
            world.Tick(60f);
            Assert.Less(record.Survivor.Vitals.Food, food, "hunger runs online");

            record.Survivor.Vitals.Damage(1000f);
            Assert.AreEqual(ActionResult.Dead, world.Act(player.Id, ActKind.Eat, 0, 0, null));
            Assert.AreEqual(ActionResult.Ok, world.Act(player.Id, ActKind.Respawn, 0, 0, null));
            Assert.IsFalse(record.Survivor.Vitals.IsDead);
            Assert.GreaterOrEqual(player.Pose.Y, 0.5f, "on dry land");
        }
    }
}
