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
            var settlement = new Settlement(ground, Catalog);
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
            Assert.AreEqual(ActionResult.NotEnoughResources, Build(w, s, BuildOp.Upgrade(piece, BuildingTier.Twig), x, y, z),
                "metal takes fragments");
            s.Inventory.Add(ItemIds.MetalFragments, BuildCosts.MetalUpgrade);
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
            var fire = new Deployable(0, DeployableKind.Campfire, fx, ground.Height(fx, fz), fz, 30f);
            var r = PlayerActions.Build(w, s, 1, BuildOp.Deploy(fire), x, y, z, out var done);
            Assert.AreEqual(ActionResult.Ok, r);
            Assert.AreEqual(1, done.Id);
            Assert.AreEqual(1, w.DeployableCount);
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.Campfire));
            var floating = new Deployable(0, DeployableKind.Campfire, fx, ground.Height(fx, fz) + 5f, fz, 0f);
            Assert.AreEqual(ActionResult.Invalid, Build(w, s, BuildOp.Deploy(floating), x, y, z));
            Assert.AreEqual(ActionResult.TooClose, Build(w, s, BuildOp.Deploy(fire), x, y, z), "the spot is taken");
            var further = new Deployable(0, DeployableKind.Campfire, fx + 1.5f, ground.Height(fx + 1.5f, fz), fz, 0f);
            Assert.AreEqual(ActionResult.TooFar, Build(w, s, BuildOp.Deploy(further), x + 30f, y, z));
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
            Build(w, s, BuildOp.Deploy(new Deployable(0, DeployableKind.Campfire, x, w.Ground.Height(x, z), z, 0f)), x, y, z);

            // Through the wire, as a joining player gets it.
            var ops = BuildOps.Describe(w);
            var writer = new NetWriter();
            Messages.Built(writer, ops);
            var bytes = writer.ToArray();
            var read = new List<BuildOp>();
            Messages.ReadBuilt(new NetReader(bytes, 1, bytes.Length - 1), read);

            var copy = new Settlement(new IslandGround(Seed), Catalog);
            foreach (var op in read) Assert.IsTrue(BuildOps.Apply(copy, op), op.ToString());
            Assert.AreEqual(w.Grid.PieceCount, copy.Grid.PieceCount);
            Assert.AreEqual(5, copy.Grid.PieceCount);
            Assert.IsTrue(copy.Grid.TryGetTier(PieceRef.OnEdge(cell.West, PieceType.Wall), out var tier));
            Assert.AreEqual(BuildingTier.Stone, tier);
            Assert.IsTrue(copy.Grid.IsDoorOpen(cell.South));
            Assert.AreEqual(1, copy.DeployableCount);
            w.Grid.TryGetFoundationTop(east, out float top);
            copy.Grid.TryGetFoundationTop(east, out float copyTop);
            Assert.AreEqual(top, copyTop);

            copy.Clear();
            Assert.AreEqual(0, copy.Grid.PieceCount);
            Assert.AreEqual(0, copy.DeployableCount);
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

    public class DeployableTests
    {
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        /// <summary>A settlement and a survivor on the beach, with a spot on flat ground beside them.</summary>
        private static (Settlement w, Survivor s, float x, float y, float z) Setup()
        {
            var ground = new IslandGround(20261113);
            var w = new Settlement(ground, Catalog);
            var s = new Survivor(Catalog);
            var spawn = Spawns.Beach(ground, new System.Random(20261113 ^ 0x5f3759df));
            for (int r = 0; r < 40; r++)
            for (int i = 0; i < 16; i++)
            {
                float a = i * 0.3927f, x = spawn.X + (float)System.Math.Cos(a) * r, z = spawn.Z + (float)System.Math.Sin(a) * r;
                float h = ground.Height(x, z);
                if (h < 1.5f) continue;
                // Flat enough for a few things around.
                if (System.Math.Abs(ground.Height(x + 3f, z) - h) > 0.8f || System.Math.Abs(ground.Height(x - 3f, z) - h) > 0.8f) continue;
                return (w, s, x, h, z);
            }
            Assert.Fail("No flat ground near the beach.");
            return default;
        }

        private static Deployable At(DeployableKind kind, Settlement w, float x, float z) =>
            new Deployable(0, kind, x, w.Ground.Height(x, z), z, 0f);

        private static ActionResult Deploy(Settlement w, Survivor s, DeployableKind kind, float x, float z, float px, float py, float pz,
            int owner, out BuildOp done)
        {
            s.Inventory.Add(Deployables.ItemOf(kind), 1);
            return PlayerActions.Build(w, s, owner, BuildOp.Deploy(At(kind, w, x, z)), px, py, pz, out done);
        }

        [Test]
        public void ABoxHoldsThingsAndOnlyItsOpenerMovesThem()
        {
            var (w, s, x, y, z) = Setup();
            Assert.AreEqual(ActionResult.Ok, Deploy(w, s, DeployableKind.StorageBox, x + 1.5f, z, x, y, z, 7, out var done));
            int box = done.Id;
            Assert.AreEqual(7, done.Deployed.Owner, "the server marks who set it down");
            Assert.AreEqual(12, w.Container(box).SlotCount);

            s.Inventory.Add(ItemIds.Wood, 300);
            int woodSlot = Enumerable.Range(0, s.Inventory.SlotCount).First(i => s.Inventory[i].ItemId == ItemIds.Wood);
            Assert.AreEqual(ActionResult.NotOpen, PlayerActions.Put(w, s, woodSlot, -1), "it has to be opened first");
            Assert.AreEqual(ActionResult.TooFar, PlayerActions.Open(w, s, 0, box, x + 40f, y, z));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, s, 0, box, x, y, z));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Put(w, s, woodSlot, -1));
            Assert.AreEqual(300, w.Container(box).Count(ItemIds.Wood));
            Assert.AreEqual(0, s.Inventory.Count(ItemIds.Wood));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.MoveInBox(w, s, 0, 5));
            Assert.AreEqual(300, w.Container(box)[5].Amount);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Take(w, s, 5, 20));
            Assert.AreEqual(300, s.Inventory[20].Amount);
            Assert.IsTrue(w.Container(box).IsEmpty);

            // Picked up (only when empty) it goes back into the inventory.
            w.Container(box).Add(ItemIds.Stone, 10);
            Assert.AreEqual(ActionResult.NotEmpty, PlayerActions.Build(w, s, 7, BuildOp.Pickup(box), x, y, z, out _));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Take(w, s, 0, -1));
            Assert.AreEqual(ActionResult.NotYours, PlayerActions.Build(w, s, 8, BuildOp.Pickup(box), x, y, z, out _));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 7, BuildOp.Pickup(box), x, y, z, out _));
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.StorageBox));
            Assert.AreEqual(0, w.DeployableCount);
            Assert.AreEqual(ActionResult.NoPiece, PlayerActions.Take(w, s, 0, -1), "a box that went is closed");
            Assert.AreEqual(0, s.Looting);
        }

        [Test]
        public void AFurnaceTurnsOreIntoMetalWhileItsWoodLasts()
        {
            var (w, s, x, y, z) = Setup();
            Assert.AreEqual(ActionResult.Ok, Deploy(w, s, DeployableKind.Furnace, x + 1.5f, z, x, y, z, 1, out var done));
            int id = done.Id;
            var furnace = w.Container(id);
            Assert.AreEqual(ActionResult.NoFuel, PlayerActions.Build(w, s, 1, BuildOp.Light(id, true), x, y, z, out _));
            furnace.Add(ItemIds.Wood, 5);
            furnace.Add(ItemIds.MetalOre, 20);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 1, BuildOp.Light(id, true), x, y, z, out var lit));
            Assert.IsTrue(lit.Open);
            Assert.IsTrue(w.IsLit(id));

            var wentOut = new List<int>();
            // (Quarter seconds add up exactly.)
            for (int i = 0; i < 48; i++) w.Tick(0.25f, wentOut);
            // Five pieces of wood burn for ten seconds: five fragments, five charcoal, and it goes out.
            Assert.AreEqual(0, furnace.Count(ItemIds.Wood));
            Assert.AreEqual(5, furnace.Count(ItemIds.Charcoal));
            Assert.AreEqual(5, furnace.Count(ItemIds.MetalFragments));
            Assert.AreEqual(15, furnace.Count(ItemIds.MetalOre));
            Assert.IsFalse(w.IsLit(id));
            Assert.AreEqual(new[] { id }, wentOut.ToArray());
            Assert.AreEqual(ActionResult.NotEmpty, PlayerActions.Build(w, s, 1, BuildOp.Pickup(id), x, y, z, out _));
        }

        [Test]
        public void SleepingBagsWakeTheirOwnerUpEveryTwoMinutes()
        {
            var (w, s, x, y, z) = Setup();
            Assert.AreEqual(ActionResult.Ok, Deploy(w, s, DeployableKind.SleepingBag, x + 1.5f, z, x, y, z, 3, out var done));
            int bag = done.Id;
            Assert.AreEqual(1, w.BagsOf(3).Count());
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.RespawnInBag(w, s, 3, bag, 0f, out _), "only the dead wake up");
            s.Vitals.Damage(1000f);
            Assert.AreEqual(ActionResult.NotYours, PlayerActions.RespawnInBag(w, s, 4, bag, 0f, out _));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.RespawnInBag(w, s, 3, bag, 10f, out var at));
            Assert.AreEqual(done.Deployed.X, at.X);
            Assert.IsFalse(s.Vitals.IsDead);
            s.Vitals.Damage(1000f);
            Assert.AreEqual(ActionResult.NotReady, PlayerActions.RespawnInBag(w, s, 3, bag, 60f, out _));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.RespawnInBag(w, s, 3, bag, 10f + Deployables.BagCooldown, out _));
        }

        [Test]
        public void ACupboardKeepsOthersFromBuildingAroundIt()
        {
            var (w, s, x, y, z) = Setup();
            s.Inventory.Add(ItemIds.Wood, 5000);
            Assert.AreEqual(ActionResult.Ok, Deploy(w, s, DeployableKind.ToolCupboard, x + 1.5f, z, x, y, z, 1, out var done));
            int tc = done.Id;
            Assert.IsTrue(w.IsAuthorized(tc, 1), "whoever sets it down is on it");
            Assert.IsTrue(w.HasPrivilege(1, x, z));
            Assert.IsFalse(w.HasPrivilege(2, x, z));
            Assert.IsTrue(w.HasPrivilege(2, x + 60f, z), "far away anyone builds");

            // Player 2 can set nothing down here, nor build.
            Assert.AreEqual(ActionResult.BuildingBlocked, Deploy(w, s, DeployableKind.Campfire, x - 1.5f, z, x, y, z, 2, out _));
            var cell = BuildingGrid.CellAt(x - 3f, z);
            var blocked = PlayerActions.Build(w, s, 2, BuildOp.Foundation(cell, 0f), x, y, z, out _);
            Assert.IsTrue(blocked == ActionResult.BuildingBlocked || blocked == ActionResult.InWater || blocked == ActionResult.TooSteep,
                blocked.ToString());

            // Until they authorize themselves on it (it has no lock yet); then whoever is on it can clear it.
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 2, BuildOp.Authorize(tc, 999), x, y, z, out var auth));
            Assert.AreEqual(2, auth.Deployed.Owner, "the server decides who is authorized, not the message");
            Assert.IsTrue(w.HasPrivilege(2, x, z));
            Assert.AreEqual(ActionResult.NotYours, PlayerActions.Build(w, s, 5, BuildOp.ClearAuthorized(tc), x, y, z, out _));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 1, BuildOp.ClearAuthorized(tc), x, y, z, out _));
            Assert.IsFalse(w.HasPrivilege(1, x, z));
            Assert.AreEqual(0, w.AuthorizedOn(tc).Count);
        }

        [Test]
        public void ACopyOfTheSettlementKnowsTheFurnacesAndCupboards()
        {
            var (w, s, x, y, z) = Setup();
            Deploy(w, s, DeployableKind.Furnace, x + 1.5f, z, x, y, z, 1, out var furnace);
            Deploy(w, s, DeployableKind.ToolCupboard, x - 1.5f, z, x, y, z, 1, out var tc);
            w.Container(furnace.Id).Add(ItemIds.Wood, 10);
            w.SetLit(furnace.Id, true);
            w.Authorize(tc.Id, 9);

            var copy = new Settlement(w.Ground, Catalog);
            foreach (var op in BuildOps.Describe(w)) Assert.IsTrue(BuildOps.Apply(copy, op), op.ToString());
            Assert.AreEqual(2, copy.DeployableCount);
            Assert.IsTrue(copy.IsLit(furnace.Id));
            Assert.IsTrue(copy.IsAuthorized(tc.Id, 9));
            Assert.IsTrue(copy.IsAuthorized(tc.Id, 1));
            Assert.AreEqual(2, copy.AuthorizedOn(tc.Id).Count);
            Assert.IsTrue(copy.TryGet(furnace.Id, out var d) && d.Kind == DeployableKind.Furnace && d.Owner == 1);
        }

        [Test]
        public void TheOpenBoxIsSentToItsOpenerOnly()
        {
            var (w, s, x, y, z) = Setup();
            Deploy(w, s, DeployableKind.StorageBox, x + 1.5f, z, x, y, z, 1, out var done);
            var sync = new LootSync();
            var writer = new NetWriter();
            Assert.IsFalse(sync.Write(w, s, writer), "nothing open, nothing to say");
            w.Container(done.Id).Add(ItemIds.Stone, 50);
            PlayerActions.Open(w, s, 0, done.Id, x, y, z);
            Assert.IsTrue(sync.Write(w, s, writer));
            var slots = new List<(int, ItemStack)>();
            var bytes = writer.ToArray();
            Assert.AreEqual(MessageType.Loot, Protocol.PeekType(bytes, bytes.Length));
            int id = Messages.ReadLoot(new NetReader(bytes, 1, bytes.Length - 1), out int count, slots);
            Assert.AreEqual(done.Id, id);
            Assert.AreEqual(12, count);
            Assert.AreEqual(12, slots.Count, "everything when it opens");
            Assert.AreEqual(new ItemStack(ItemIds.Stone, 50), slots[0].Item2);
            Assert.IsFalse(sync.Write(w, s, writer));
            w.Container(done.Id).Add(ItemIds.Stone, 5);
            Assert.IsTrue(sync.Write(w, s, writer));
            bytes = writer.ToArray();
            Messages.ReadLoot(new NetReader(bytes, 1, bytes.Length - 1), out _, slots);
            Assert.AreEqual(1, slots.Count, "then only what changed");
            PlayerActions.Close(s);
            Assert.IsTrue(sync.Write(w, s, writer));
            bytes = writer.ToArray();
            Assert.AreEqual(0, Messages.ReadLoot(new NetReader(bytes, 1, bytes.Length - 1), out _, slots), "and that it closed");
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
                BuildOp.Deploy(new Deployable(12, DeployableKind.Campfire, 1.5f, 2.5f, -3.5f, 90f)),
                BuildOp.Door(edge, DoorKind.Metal),
                BuildOp.Upgrade(PieceRef.Foundation(new Cell(0, 0)), BuildingTier.Metal),
                BuildOp.PlaceLock(LockTarget.Door(edge), 4),
                BuildOp.PlaceLock(LockTarget.On(9), 4),
                BuildOp.SetCode(LockTarget.Door(edge), 1234, 4),
                BuildOp.SetCode(LockTarget.On(9), CodeLocks.Hidden, 4),
                BuildOp.EnterCode(LockTarget.On(9), 42),
                BuildOp.LockAuthorize(LockTarget.Door(edge), 8),
                BuildOp.SetLocked(LockTarget.On(9), true),
                BuildOp.RemoveLock(LockTarget.Door(edge)),
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
                Assert.AreEqual(op.Deployed.Id, back.Deployed.Id);
                Assert.AreEqual(op.Deployed.Yaw, back.Deployed.Yaw);
                Assert.AreEqual(op.Code, back.Code);
                Assert.AreEqual(op.Deployed.Owner, back.Deployed.Owner);
                if (op.IsLockOp) Assert.AreEqual(op.Lock, back.Lock);
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

namespace Oxfall.Core.Tests
{
    using System;
    using NUnit.Framework;
    using Oxfall.Core.Building;
    using Oxfall.Core.Items;
    using Oxfall.Core.Net;
    using Oxfall.Core.Rules;
    using Oxfall.Core.World;

    public class WorldSaveTests
    {
        private const string Token = "cccccccccccccccccccccccccccccccc";

        [Test]
        public void ASavedIslandComesBackAsItWas()
        {
            var world = new ServerWorld(20261113) { DevMode = true };
            var record = world.Join(Protocol.Version, "Ale", Token, 0f, out var player, out _, out _);
            ServerWorld.GiveTestKit(record.Survivor.Inventory);
            record.Survivor.Inventory.Move(0, 20);
            world.Act(player.Id, ActKind.Craft, 0, 0, "torch");
            record.Survivor.Vitals.Set(80f, 123f, 45f);

            // A floor where the player can reach, and a campfire.
            var p = player.Pose;
            Cell cell = default;
            bool found = false;
            for (int r = 0; r <= 4 && !found; r++)
            for (int dz = -r; dz <= r && !found; dz++)
            for (int dx = -r; dx <= r && !found; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                cell = BuildingGrid.CellAt(p.X + dx * 3f, p.Z + dz * 3f);
                found = world.Build(player.Id, BuildOp.Foundation(cell, 0f), out _) == ActionResult.Ok;
            }
            Assert.IsTrue(found, "somewhere to build near the beach");
            // Stand on the floor to do the rest.
            world.Settlement.Grid.TryGetFoundationTop(cell, out float top);
            world.Players.Place(player.Id, new Pose((cell.X + 0.5f) * 3f, top, (cell.Z + 0.5f) * 3f, 0f), 0.5f);
            p = player.Pose;
            Assert.AreEqual(ActionResult.Ok, world.Build(player.Id, BuildOp.Edge(cell.North, PieceType.Doorway), out _));
            Assert.AreEqual(ActionResult.Ok, world.Build(player.Id, BuildOp.Door(cell.North), out _));
            Assert.AreEqual(ActionResult.Ok, world.Build(player.Id, BuildOp.Deploy(new Deployable(0, DeployableKind.Campfire, p.X, top, p.Z, 0f)), out _));
            var box = world.Settlement.Add(new Deployable(0, DeployableKind.StorageBox, p.X + 1f, top, p.Z, 0f, record.Owner));
            world.Settlement.Container(box.Id).Add(ItemIds.MetalOre, 77);
            var tree = System.Linq.Enumerable.First(ResourceLayout.Generate(world.Ground), s => s.Type == Gathering.ResourceNodeType.Tree);
            world.Players.Place(player.Id, new Pose(tree.X + 1f, tree.Y, tree.Z, 0f), 1f);
            world.Hit(player.Id, tree.Id, ToolKind.Hatchet, 2f, out _);

            var bytes = world.Save();
            var loaded = new ServerWorld(20261113);
            Assert.IsTrue(loaded.Load(bytes, out var why), why);

            Assert.AreEqual(world.Settlement.Grid.PieceCount, loaded.Settlement.Grid.PieceCount);
            Assert.IsTrue(loaded.Settlement.Grid.HasDoor(cell.North));
            Assert.AreEqual(2, loaded.Settlement.DeployableCount);
            Assert.AreEqual(77, loaded.Settlement.Container(box.Id).Count(ItemIds.MetalOre), "what the box held is kept");
            Assert.IsTrue(loaded.Settlement.TryGet(box.Id, out var loadedBox) && loadedBox.Owner == record.Owner);
            Assert.AreEqual(record.Owner, loaded.Settlement.OwnerOf(PieceRef.Foundation(cell)), "who built it is kept");
            loaded.TryGetNode(tree.Id, out var node, out _, out _);
            world.TryGetNode(tree.Id, out var original, out _, out _);
            Assert.AreEqual(original.Remaining, node.Remaining, "the chopped tree stays chopped");

            var back = loaded.Join(Protocol.Version, "Ale", Token, 0f, out var again, out _, out _);
            var s = back.Survivor;
            for (int i = 0; i < s.Inventory.SlotCount; i++) Assert.AreEqual(record.Survivor.Inventory[i], s.Inventory[i], "slot " + i);
            Assert.AreEqual(1, s.Crafting.Jobs.Count);
            Assert.AreEqual(80f, s.Vitals.Health);
            Assert.AreEqual(new Pose(tree.X + 1f, tree.Y, tree.Z, 0f), again.Pose, "where they stood when saved");

            var newcomer = loaded.Join(Protocol.Version, "Bea", "dddddddddddddddddddddddddddddddd", 0f, out _, out _, out _);
            Assert.AreNotEqual(back.Owner, newcomer.Owner, "owner ids are not handed out twice");
        }

        [Test]
        public void AnotherWipesSaveIsNotLoaded()
        {
            var bytes = new ServerWorld(1).Save();
            Assert.IsFalse(new ServerWorld(2).Load(bytes, out var why));
            StringAssert.Contains("seed", why);
            Assert.IsFalse(new ServerWorld(1).Load(new byte[] { 1, 2, 3 }, out _));
            Assert.IsFalse(new ServerWorld(1).Load(new byte[] { (byte)'O', (byte)'X', (byte)'S', (byte)'V', 1, 0 }, out why));
        }

        [Test]
        public void TheWipeIsOnThursdayEvening()
        {
            var thursday = new DateTime(2026, 11, 12, 19, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(DayOfWeek.Thursday, thursday.DayOfWeek);
            Assert.AreEqual(thursday, WipeSchedule.LastWipe(new DateTime(2026, 11, 14, 1, 0, 0, DateTimeKind.Utc)));
            Assert.AreEqual(thursday, WipeSchedule.LastWipe(thursday));
            Assert.AreEqual(thursday.AddDays(-7), WipeSchedule.LastWipe(thursday.AddMinutes(-1)));
            Assert.AreEqual(thursday.AddDays(7), WipeSchedule.NextWipe(thursday.AddHours(30)));
            Assert.AreEqual(20261112, WipeSchedule.SeedFor(thursday));
        }
    }
}
