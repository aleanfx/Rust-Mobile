using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    public class RaidTests
    {
        private const int Seed = 20261113;
        private const float S = BuildingGrid.CellSize;
        private const string TokenA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string TokenB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        // ---- Sulfur and gunpowder

        [Test]
        public void SulfurRocksGiveOreTheFurnaceTurnsIntoSulfur()
        {
            Assert.AreEqual(ItemIds.SulfurOre, GatherTable.MainItem(ResourceNodeType.Sulfur));
            Assert.Greater(GatherTable.PerHit(ResourceNodeType.Sulfur, ToolKind.Pickaxe), GatherTable.PerHit(ResourceNodeType.Sulfur, ToolKind.Rock));
            Assert.Greater(GatherTable.PerHit(ResourceNodeType.Sulfur, ToolKind.MetalPickaxe), GatherTable.PerHit(ResourceNodeType.Sulfur, ToolKind.Pickaxe));

            var ground = new IslandGround(Seed);
            var spawns = ResourceLayout.Generate(ground);
            int sulfur = spawns.Count(n => n.Type == ResourceNodeType.Sulfur);
            Assert.Greater(sulfur, 15, "enough sulfur rocks on the island");
            Assert.Less(sulfur, spawns.Count(n => n.Type == ResourceNodeType.Stone), "rarer than stone");

            var w = new Settlement(ground, Catalog);
            var furnace = w.Add(new Deployable(0, DeployableKind.Furnace, 0f, 0f, 0f, 0f));
            var box = w.Container(furnace.Id);
            box.Add(ItemIds.Wood, 20);
            box.Add(ItemIds.SulfurOre, 5);
            box.Add(ItemIds.MetalOre, 5);
            w.SetLit(furnace.Id, true);
            for (int i = 0; i < 40; i++) w.Tick(0.5f);
            Assert.AreEqual(5, box.Count(ItemIds.Sulfur), "all the sulfur ore smelted");
            Assert.AreEqual(5, box.Count(ItemIds.MetalFragments), "and the metal ore beside it");
            Assert.AreEqual(0, box.Count(ItemIds.SulfurOre));
        }

        [Test]
        public void GunpowderAndChargesAreCrafted()
        {
            var recipes = RecipeBook.CreateDefault(Catalog);
            Assert.IsTrue(recipes.TryGet("gunpowder", out var powder));
            Assert.AreEqual(ItemIds.Gunpowder, powder.OutputItemId);
            Assert.IsTrue(powder.Ingredients.Any(i => i.ItemId == ItemIds.Charcoal));
            Assert.IsTrue(powder.Ingredients.Any(i => i.ItemId == ItemIds.Sulfur));
            Assert.IsTrue(recipes.TryGet("satchel_charge", out var satchel));
            Assert.IsTrue(satchel.Ingredients.Any(i => i.ItemId == ItemIds.Gunpowder));
            var def = Catalog.Get(ItemIds.SatchelCharge);
            Assert.AreEqual(ToolKind.Explosive, def.Tool);
            Assert.Greater(def.MaxStack, 1, "charges stack");
            Assert.AreEqual(0f, Combat.MeleeDamage(ToolKind.Explosive), "not a club");
        }

        // ---- Blasts

        private static (BuildingGrid grid, Cell cell) Floor()
        {
            var grid = new BuildingGrid();
            var cell = new Cell(0, 0);
            grid.PlaceFoundation(cell, 1f);
            return (grid, cell);
        }

        private static int ChargesToFell(BuildingGrid grid, ChargeTarget target, System.Func<bool> standing)
        {
            int n = 0;
            while (standing() && n < 20)
            {
                Assert.IsTrue(Explosives.Blast(grid, target, null), "charge " + n);
                n++;
            }
            return n;
        }

        [TestCase(BuildingTier.Twig, 1)]
        [TestCase(BuildingTier.Wood, 2)]
        [TestCase(BuildingTier.Stone, 4)]
        [TestCase(BuildingTier.Metal, 8)]
        public void AWallFallsToAsManyChargesAsItsTierIsHard(BuildingTier tier, int charges)
        {
            var (grid, cell) = Floor();
            grid.PlaceEdge(cell.South, PieceType.Wall);
            var wall = PieceRef.OnEdge(cell.South, PieceType.Wall);
            if (tier != BuildingTier.Twig) grid.Upgrade(wall, tier);
            Assert.AreEqual(charges, Explosives.ChargesFor(grid.Health(wall)));
            Assert.AreEqual(charges, ChargesToFell(grid, ChargeTarget.OnPiece(wall), () => grid.Exists(wall)));
            Assert.IsTrue(grid.HasFoundation(cell), "the floor under it stands");
        }

        [TestCase(DoorKind.Wood, 1)]
        [TestCase(DoorKind.Metal, 2)]
        public void ADoorIsTheWeakSpotAndItsLockGoesWithIt(DoorKind kind, int charges)
        {
            var w = new Settlement(new IslandGround(Seed), Catalog);
            var grid = w.Grid;
            var cell = new Cell(0, 0);
            grid.PlaceFoundation(cell, 1f);
            grid.PlaceEdge(cell.South, PieceType.Doorway);
            var frame = PieceRef.OnEdge(cell.South, PieceType.Doorway);
            grid.Upgrade(frame, BuildingTier.Stone);
            grid.PlaceDoor(cell.South, kind);
            Assert.IsTrue(w.AddLock(LockTarget.Door(cell.South), 1));
            Assert.AreEqual(DoorKinds.MaxHealth(kind), grid.DoorHealth(cell.South));

            Assert.AreEqual(charges, ChargesToFell(grid, ChargeTarget.OnDoor(cell.South), () => grid.HasDoor(cell.South)));
            Assert.IsFalse(w.HasLock(LockTarget.Door(cell.South)), "the lock is gone with the door");
            Assert.AreEqual(500f, grid.Health(frame), "the stone frame is untouched");

            // One more on the empty doorway eats into the frame.
            Assert.IsTrue(Explosives.Blast(grid, ChargeTarget.OnDoor(cell.South), null));
            Assert.AreEqual(500f - Explosives.BlastDamage, grid.Health(frame));
        }

        [Test]
        public void WhatFallsOnTheServerFallsTheSameOnEveryPhone()
        {
            // Two copies: the server's, and a phone's that only applies what it is told.
            var (server, cell) = Floor();
            var (phone, _) = Floor();
            var east = new Cell(1, 0);
            foreach (var g in new[] { server, phone })
            {
                g.PlaceFoundation(east, 1f);
                g.PlaceEdge(cell.South, PieceType.Wall);
                g.PlaceEdge(cell.West, PieceType.Wall);
                g.PlaceRoof(cell);
                g.Upgrade(PieceRef.Foundation(cell), BuildingTier.Wood);
            }
            var changes = new List<BuildOp>();
            var floor = ChargeTarget.OnPiece(PieceRef.Foundation(cell));
            Explosives.Blast(server, floor, changes);
            Explosives.Blast(server, floor, changes);
            Assert.IsFalse(server.HasFoundation(cell), "the floor went at two charges");
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(BuildOpKind.Health, changes[0].Kind);
            Assert.AreEqual(125f, changes[0].HealthLeft, 1e-3f);
            Assert.AreEqual(0f, changes[1].HealthLeft);

            // Sent over the wire and applied to the phone's copy.
            foreach (var op in changes)
            {
                var writer = new NetWriter();
                op.Write(writer);
                var back = BuildOp.Read(new NetReader(writer.ToArray()));
                Assert.AreEqual(op.Kind, back.Kind);
                Assert.AreEqual(op.HealthLeft, back.HealthLeft);
                Assert.IsTrue(ApplyTo(phone, back), op.ToString());
            }
            Assert.AreEqual(server.PieceCount, phone.PieceCount, "the same pieces stand");
            Assert.IsFalse(phone.HasFoundation(cell));
            Assert.IsFalse(phone.HasEdgePiece(cell.West), "the wall that stood only on it fell with it");
            Assert.IsFalse(phone.HasRoof(cell), "and the roof");
            Assert.IsTrue(phone.HasFoundation(east), "the next floor stands");
        }

        /// <summary>Applies a health op to a bare grid as <see cref="BuildOps.Apply"/> would.</summary>
        private static bool ApplyTo(BuildingGrid grid, BuildOp op)
        {
            var piece = BuildOps.Stored(grid, op.Piece);
            if (!grid.Exists(piece)) return false;
            grid.SetHealth(piece, op.HealthLeft);
            return true;
        }

        [Test]
        public void ADamagedBaseIsDescribedDamagedToJoinersAndSaves()
        {
            var ground = new IslandGround(Seed);
            var a = new Settlement(ground, Catalog);
            var cell = new Cell(0, 0);
            a.Grid.PlaceFoundation(cell, 1f);
            a.Grid.PlaceEdge(cell.South, PieceType.Doorway);
            a.Grid.PlaceEdge(cell.North, PieceType.Wall);
            var wall = PieceRef.OnEdge(cell.North, PieceType.Wall);
            a.Grid.Upgrade(wall, BuildingTier.Stone);
            a.Grid.PlaceDoor(cell.South, DoorKind.Metal);
            Explosives.Blast(a.Grid, ChargeTarget.OnPiece(wall), null);
            Explosives.Blast(a.Grid, ChargeTarget.OnDoor(cell.South), null);

            var b = new Settlement(ground, Catalog);
            foreach (var op in BuildOps.Describe(a)) Assert.IsTrue(BuildOps.Apply(b, op), op.ToString());
            Assert.AreEqual(375f, b.Grid.Health(wall), 1e-3f);
            Assert.AreEqual(125f, b.Grid.DoorHealth(cell.South), 1e-3f);
        }

        // ---- On the server

        /// <summary>Ale, with charges, standing 5 m south of the stone wall Bea built on the south side of her floor.</summary>
        private static (ServerWorld world, SurvivorRecord ale, ServerState.Player pa, SurvivorRecord bea, ServerState.Player pb, Cell cell) Base()
        {
            var world = new ServerWorld(Seed) { DevMode = true };
            var ale = world.Join(Protocol.Version, "Ale", TokenA, 0f, out var pa, out _, out _);
            var bea = world.Join(Protocol.Version, "Bea", TokenB, 0f, out var pb, out _, out _);
            bea.Survivor.Inventory.Add(ItemIds.Wood, 3000);
            bea.Survivor.Inventory.Add(ItemIds.Stone, 3000);
            var near = BuildingGrid.CellAt(pa.Pose.X, pa.Pose.Z);
            for (int r = 1; r < 30; r++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                var cell = new Cell(near.X + dx, near.Z + dz);
                if (PlayerActions.FoundationTop(world.Settlement, cell, out float top) != ActionResult.Ok) continue;
                float x = (cell.X + 0.5f) * S, z = (cell.Z + 0.5f) * S;
                world.Players.Place(pb.Id, new Pose(x, top, z, 0f), 0f);
                Assert.AreEqual(ActionResult.Ok, world.Build(pb.Id, BuildOp.Foundation(cell, 0f), out _));
                Assert.AreEqual(ActionResult.Ok, world.Build(pb.Id, BuildOp.Edge(cell.South, PieceType.Wall), out _));
                var wall = PieceRef.OnEdge(cell.South, PieceType.Wall);
                Assert.AreEqual(ActionResult.Ok, world.Build(pb.Id, BuildOp.Upgrade(wall, BuildingTier.Wood), out _));
                Assert.AreEqual(ActionResult.Ok, world.Build(pb.Id, BuildOp.Upgrade(wall, BuildingTier.Stone), out _));
                // Ale stands 5 m south of the wall, facing it: out of the blast.
                world.Players.Place(pa.Id, new Pose(x, top, cell.Z * S - 5f, 0f), 0f);
                ale.Survivor.Inventory.Add(ItemIds.SatchelCharge, 6);
                world.Announcements.Clear();
                return (world, ale, pa, bea, pb, cell);
            }
            Assert.Fail("No buildable cell near the spawn.");
            return default;
        }

        private static (float x, float y, float z) WallFace(ServerWorld world, Cell cell)
        {
            var p = PlayerActions.EdgePoint(world.Settlement.Grid, cell.South);
            return (p.X, p.Y, p.Z - 0.1f);
        }

        private static void Burn(ServerWorld world, float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.05f) world.Tick(0.05f);
        }

        [Test]
        public void AThrownChargeSticksFizzesAndBringsAStoneWallDownAtFour()
        {
            var (world, ale, pa, bea, pb, cell) = Base();
            var wall = ChargeTarget.OnPiece(PieceRef.OnEdge(cell.South, PieceType.Wall));
            var (x, y, z) = WallFace(world, cell);
            float now = 10f;
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(world.Throw(pa.Id, wall, x, y, z, 0f, 0f, -1f, now, out var charge), "throw " + i);
                Assert.AreEqual(1, world.LiveCharges.Count);
                Assert.AreEqual(world.Time + Explosives.FuseSeconds, charge.GoesOffAt, 1e-3f);
                Burn(world, Explosives.FuseSeconds - 0.5f);
                Assert.AreEqual(0, world.Blasts.Count, "not yet");
                Burn(world, 1f);
                Assert.AreEqual(1, world.Blasts.Count, "gone off");
                Assert.AreEqual(charge.Id, world.Blasts[0].Id);
                world.Blasts.Clear();
                now += 10f;
            }
            Assert.AreEqual(2, ale.Survivor.Inventory.Count(ItemIds.SatchelCharge), "four spent");
            Assert.IsFalse(world.Settlement.Grid.HasEdgePiece(cell.South), "the wall is down");
            var last = world.Announcements.Last(op => op.Kind == BuildOpKind.Health);
            Assert.AreEqual(0f, last.HealthLeft, "everyone is told it fell");
            Assert.IsTrue(world.Settlement.Grid.HasFoundation(cell));
            Assert.IsFalse(world.BlastHurts.Any(h => h.Target == pa.Id), "five metres off, he is safe");
        }

        [Test]
        public void AThrowIsRefusedWithoutACharge_FromTooFar_OrAtNothing()
        {
            var (world, ale, pa, bea, pb, cell) = Base();
            var wall = ChargeTarget.OnPiece(PieceRef.OnEdge(cell.South, PieceType.Wall));
            var (x, y, z) = WallFace(world, cell);

            Assert.IsFalse(world.Throw(pa.Id, wall, x, y + 20f, z, 0f, 0f, -1f, 1f, out _), "out of reach");
            Assert.IsFalse(world.Throw(pa.Id, ChargeTarget.OnPiece(PieceRef.OnEdge(cell.North, PieceType.Wall)), x, y, z, 0f, 0f, -1f, 1f, out _),
                "no wall there");
            Assert.IsFalse(world.Throw(pa.Id, ChargeTarget.OnDoor(cell.South), x, y, z, 0f, 0f, -1f, 1f, out _), "no door there");
            Assert.IsFalse(world.Throw(pa.Id, wall, float.NaN, y, z, 0f, 0f, -1f, 1f, out _), "nonsense");
            Assert.IsTrue(world.Throw(pa.Id, wall, x, y, z, 0f, 0f, -1f, 1f, out _));
            Assert.IsFalse(world.Throw(pa.Id, wall, x, y, z, 0f, 0f, -1f, 1.2f, out _), "not twice in a moment");
            ale.Survivor.Inventory.Remove(ItemIds.SatchelCharge, ale.Survivor.Inventory.Count(ItemIds.SatchelCharge));
            Assert.IsFalse(world.Throw(pa.Id, wall, x, y, z, 0f, 0f, -1f, 5f, out _), "none left");
            Assert.AreEqual(1, world.LiveCharges.Count);
        }

        [Test]
        public void TheBlastHurtsThoseNearMoreThanThoseFurther_AndSleepersToo()
        {
            var (world, ale, pa, bea, pb, cell) = Base();
            var (x, y, z) = WallFace(world, cell);
            // Bea beside her wall, Ale 3 m in front of it.
            world.Players.Place(pb.Id, new Pose(x + 1f, y - 1.5f, z - 0.8f, 0f), 0f);
            world.Players.Place(pa.Id, new Pose(x, y - 1.5f, z - 2.9f, 0f), 0f);
            Assert.IsTrue(world.Throw(pa.Id, ChargeTarget.OnPiece(PieceRef.OnEdge(cell.South, PieceType.Wall)), x, y, z, 0f, 0f, -1f, 1f, out _));
            Burn(world, Explosives.FuseSeconds + 0.2f);
            var hurts = world.BlastHurts.ToDictionary(h => h.Target);
            Assert.IsTrue(hurts.ContainsKey(pb.Id) && hurts.ContainsKey(pa.Id));
            Assert.Greater(hurts[pb.Id].Damage, hurts[pa.Id].Damage, "the nearer takes more");
            Assert.AreEqual(pa.Id, hurts[pb.Id].By, "whose charge it was");
            Assert.AreEqual(0f, Explosives.DamageToPlayer(Explosives.BlastRadius), "nothing past the radius");

            // Inside, behind the wall that still stands, she is safe.
            world.BlastHurts.Clear();
            world.Players.Place(pb.Id, new Pose(x, y - 1.5f, z + 1.2f, 0f), 0f);
            Assert.IsTrue(world.Throw(pa.Id, ChargeTarget.OnPiece(PieceRef.OnEdge(cell.South, PieceType.Wall)), x, y, z, 0f, 0f, -1f, 3f, out _));
            Burn(world, Explosives.FuseSeconds + 0.2f);
            Assert.IsTrue(world.Settlement.Grid.HasEdgePiece(cell.South));
            Assert.IsFalse(world.BlastHurts.Any(h => h.Target == pb.Id), "the wall shields her");
            world.Players.Place(pb.Id, new Pose(x + 1f, y - 1.5f, z - 0.8f, 0f), 0f);

            // Bea goes to sleep right there; the next charge reaches her body too.
            world.BlastHurts.Clear();
            world.Leave(pb.Id);
            Assert.IsTrue(world.Throw(pa.Id, ChargeTarget.OnPiece(PieceRef.OnEdge(cell.South, PieceType.Wall)), x, y, z, 0f, 0f, -1f, 5f, out _));
            Burn(world, Explosives.FuseSeconds + 0.2f);
            Assert.IsTrue(world.BlastSleeperHits.Any(h => h.Owner == bea.Owner && h.ByName == "Ale"));
        }

        [Test]
        public void TheHammerMendsADamagedWallButNotInTheMiddleOfARaid()
        {
            var (world, ale, pa, bea, pb, cell) = Base();
            var wallRef = PieceRef.OnEdge(cell.South, PieceType.Wall);
            var (x, y, z) = WallFace(world, cell);
            Assert.AreEqual(ActionResult.NotDamaged, world.Build(pb.Id, BuildOp.Repair(wallRef), out _));
            Assert.IsTrue(world.Throw(pa.Id, ChargeTarget.OnPiece(wallRef), x, y, z, 0f, 0f, -1f, 1f, out _));
            Burn(world, Explosives.FuseSeconds + 0.2f);
            var grid = world.Settlement.Grid;
            Assert.AreEqual(375f, grid.Health(wallRef), 1e-3f);

            Assert.AreEqual(ActionResult.RecentlyDamaged, world.Build(pb.Id, BuildOp.Repair(wallRef), out _), "not under fire");
            Burn(world, Repairs.Delay + 0.5f);
            int stone = bea.Survivor.Inventory.Count(ItemIds.Stone);
            Assert.AreEqual(ActionResult.Ok, world.Build(pb.Id, BuildOp.Repair(wallRef), out var done));
            Assert.AreEqual(500f, grid.Health(wallRef));
            Assert.AreEqual(BuildOpKind.Health, done.Kind);
            Assert.AreEqual(500f, done.HealthLeft, "everyone is told");
            int paid = stone - bea.Survivor.Inventory.Count(ItemIds.Stone);
            Assert.AreEqual((int)System.Math.Ceiling(BuildCosts.StoneUpgrade * Repairs.CostShare * 0.25f), paid, "a share of what the stone cost");
        }

        // ---- What is set down

        [Test]
        public void AChargeBesideABoxBreaksItAtTwo_AndWhatItHeldSpillsIntoABag()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog) { DropsLoot = true };
            var box = w.Add(new Deployable(0, DeployableKind.StorageBox, 10f, 5f, 10f, 0f, 1));
            w.Container(box.Id).Add(ItemIds.Stone, 500);
            w.Container(box.Id).Add(ItemIds.Wood, 300);
            var body = w.Add(new Deployable(0, DeployableKind.Bag, 10.5f, 5f, 10.5f, 0f, 2));
            w.Container(body.Id).Add(ItemIds.Cloth, 5);
            Deployable dropped = default;
            w.LootDropped += b => dropped = b;
            var changes = new List<BuildOp>();

            Explosives.Splash(w, 10.3f, 5.4f, 10f, changes);
            Assert.AreEqual(1, changes.Count, "the box, not the dead player's bag");
            Assert.AreEqual(BuildOpKind.DeployableHealth, changes[0].Kind);
            Assert.AreEqual(box.Id, changes[0].Id);
            Assert.AreEqual(150f - Explosives.DeployableDamage, w.DeployableHealth(box.Id), 1e-3f);

            Explosives.Splash(w, 10.3f, 5.4f, 10f, changes);
            Assert.IsFalse(w.TryGet(box.Id, out _), "the box is gone");
            Assert.AreEqual(0f, changes[1].HealthLeft);
            Assert.AreEqual(DeployableKind.Bag, dropped.Kind, "what it held lies in a bag");
            Assert.AreEqual(500, w.Container(dropped.Id).Count(ItemIds.Stone));
            Assert.AreEqual(300, w.Container(dropped.Id).Count(ItemIds.Wood));
            Assert.IsTrue(w.TryGet(body.Id, out _), "a dead player's bag does not burn");
            Assert.AreEqual(0f, Explosives.DamageToDeployable(Explosives.DeployableReach), "nothing past its reach");
        }

        [Test]
        public void AWallShieldsWhatIsInsideUntilItFalls()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog) { DropsLoot = true };
            var grid = w.Grid;
            var cell = new Cell(0, 0);
            grid.PlaceFoundation(cell, 1f);
            grid.PlaceEdge(cell.South, PieceType.Wall);
            var wall = PieceRef.OnEdge(cell.South, PieceType.Wall);
            grid.Upgrade(wall, BuildingTier.Wood);
            var box = w.Add(new Deployable(0, DeployableKind.StorageBox, 1.5f, 1f, 0.6f, 0f));

            // A charge on the outside of the wall, the box just inside.
            Assert.IsTrue(Explosives.Shielded(grid, 1.5f, 2f, -0.1f, 1.5f, 1.4f, 0.6f));
            Explosives.Blast(grid, ChargeTarget.OnPiece(wall), null);
            Explosives.Splash(w, 1.5f, 2f, -0.1f, null);
            Assert.AreEqual(150f, w.DeployableHealth(box.Id), "behind a standing wall");
            Explosives.Blast(grid, ChargeTarget.OnPiece(wall), null);
            Assert.IsFalse(grid.HasEdgePiece(cell.South), "the wood wall fell at two");
            Explosives.Splash(w, 1.5f, 2f, -0.1f, null);
            Assert.Less(w.DeployableHealth(box.Id), 150f, "with the wall gone, the blast reaches it");

            // A doorway: its frame shields, its opening only while a door is shut in it.
            grid.PlaceEdge(cell.North, PieceType.Doorway);
            Assert.IsFalse(Explosives.Shielded(grid, 1.5f, 2f, 3.5f, 1.5f, 1.4f, 2.4f), "through an empty doorway");
            Assert.IsTrue(Explosives.Shielded(grid, 0.3f, 2f, 3.5f, 0.3f, 1.4f, 2.4f), "the frame beside the opening");
            grid.PlaceDoor(cell.North, DoorKind.Wood);
            Assert.IsTrue(Explosives.Shielded(grid, 1.5f, 2f, 3.5f, 1.5f, 1.4f, 2.4f), "a shut door");
            grid.ToggleDoor(cell.North);
            Assert.IsFalse(Explosives.Shielded(grid, 1.5f, 2f, 3.5f, 1.5f, 1.4f, 2.4f), "an open one");
            Assert.IsFalse(Explosives.Shielded(grid, 1.5f, 2f, 5f, 1.5f, 1.4f, 4f), "nothing in between");
        }

        [Test]
        public void WhatStandsOnAFloorFallsWithIt_AndEveryPhoneSeesTheSame()
        {
            var (world, ale, pa, bea, pb, cell) = Base();
            var w = world.Settlement;
            w.Grid.TryGetFoundationTop(cell, out float top);
            float cx = (cell.X + 0.5f) * S, cz = (cell.Z + 0.5f) * S;
            var box = w.Add(new Deployable(0, DeployableKind.StorageBox, cx - 0.8f, top, cz + 0.5f, 0f, bea.Owner));
            w.Container(box.Id).Add(ItemIds.MetalFragments, 250);
            var sleepingBag = w.Add(new Deployable(0, DeployableKind.SleepingBag, cx + 0.8f, top, cz + 0.5f, 0f, bea.Owner));
            float gx = cx + 9f, gz = cz;
            var outside = w.Add(new Deployable(0, DeployableKind.StorageBox, gx, world.Ground.Height(gx, gz), gz, 0f, bea.Owner));
            w.Container(outside.Id).Add(ItemIds.Wood, 10);

            // A phone that joined before the raid, and only applies what it is told.
            var phone = new Settlement(world.Ground, Catalog);
            foreach (var op in BuildOps.Describe(w)) Assert.IsTrue(BuildOps.Apply(phone, op), op.ToString());
            Assert.AreEqual(w.DeployableCount, phone.DeployableCount);

            // Ale, 2 m south of the wall, blows up the twig floor.
            world.Players.Place(pa.Id, new Pose(cx, top, cell.Z * S - 2f, 0f), 0f);
            var floor = PieceRef.Foundation(cell);
            var at = PlayerActions.PiecePoint(w.Grid, floor);
            Assert.IsTrue(world.Throw(pa.Id, ChargeTarget.OnPiece(floor), at.X, at.Y, at.Z, 0f, 1f, 0f, 1f, out _));
            Burn(world, Explosives.FuseSeconds + 0.2f);
            Assert.IsFalse(w.Grid.HasFoundation(cell), "the floor is gone");
            Assert.IsFalse(w.TryGet(box.Id, out _), "the box on it went with it");
            Assert.IsFalse(w.TryGet(sleepingBag.Id, out _), "and the sleeping bag");
            Assert.IsTrue(w.TryGet(outside.Id, out _), "the box on the ground over there stands");
            var spilled = w.Deployables.Single(d => d.Kind == DeployableKind.Bag);
            Assert.AreEqual(250, w.Container(spilled.Id).Count(ItemIds.MetalFragments), "what the box held lies in a bag");
            Assert.AreEqual(world.Ground.Height(spilled.X, spilled.Z), spilled.Y, 1e-3f, "on the ground below");
            Assert.IsTrue(world.Announcements.Any(op => op.Kind == BuildOpKind.DeployableHealth && op.Id == box.Id && op.HealthLeft == 0f));
            Assert.IsTrue(world.Announcements.Any(op => op.Kind == BuildOpKind.Deploy && op.Id == spilled.Id));

            // Everything the server said, over the wire, to the phone.
            foreach (var op in world.Announcements)
            {
                var writer = new NetWriter();
                op.Write(writer);
                BuildOps.Apply(phone, BuildOp.Read(new NetReader(writer.ToArray())));
            }
            CollectionAssert.AreEquivalent(w.Deployables.Select(d => (d.Id, d.Kind)), phone.Deployables.Select(d => (d.Id, d.Kind)),
                "the phone sees the same things standing");
            Assert.IsFalse(phone.Grid.HasFoundation(cell));
        }

        [Test]
        public void AHurtBoxIsDescribedHurt_ClearingDropsNothing_AndItsHealthCrossesTheWire()
        {
            var ground = new IslandGround(Seed);
            var a = new Settlement(ground, Catalog) { DropsLoot = true };
            var cell = new Cell(3, 3);
            a.Grid.PlaceFoundation(cell, 2f);
            var box = a.Add(new Deployable(0, DeployableKind.StorageBox, 10.5f, 2f, 10.5f, 0f));
            a.Container(box.Id).Add(ItemIds.Stone, 10);
            Assert.IsTrue(a.SetDeployableHealth(box.Id, 40f));

            var b = new Settlement(ground, Catalog);
            foreach (var op in BuildOps.Describe(a)) Assert.IsTrue(BuildOps.Apply(b, op), op.ToString());
            Assert.AreEqual(40f, b.DeployableHealth(box.Id), 1e-3f);

            var writer = new NetWriter();
            BuildOp.DeployableHealth(box.Id, 12.5f).Write(writer);
            var back = BuildOp.Read(new NetReader(writer.ToArray()));
            Assert.AreEqual(BuildOpKind.DeployableHealth, back.Kind);
            Assert.AreEqual(box.Id, back.Id);
            Assert.AreEqual(12.5f, back.HealthLeft);

            // A fresh copy from the server wipes this one: no bags come of it.
            a.Clear();
            Assert.AreEqual(0, a.DeployableCount);
        }

        [Test]
        public void ChargesOnTheWireComeBackTheSame()
        {
            var targets = new[]
            {
                ChargeTarget.Ground,
                ChargeTarget.OnPiece(PieceRef.Foundation(new Cell(-3, 7))),
                ChargeTarget.OnPiece(PieceRef.Roof(new Cell(2, 2))),
                ChargeTarget.OnPiece(PieceRef.OnEdge(new Edge(4, -2, EdgeAxis.AlongZ), PieceType.Wall)),
                ChargeTarget.OnDoor(new Edge(1, 5, EdgeAxis.AlongX)),
            };
            var w = new NetWriter();
            foreach (var t in targets)
            {
                Messages.Throw(w, t, 1.5f, 2.5f, -3.5f, 0f, 0.6f, -0.8f);
                var r = new NetReader(w.ToArray());
                Assert.AreEqual(MessageType.Throw, (MessageType)r.Byte());
                Messages.ReadThrow(r, out var back, out float x, out float y, out float z, out float nx, out float ny, out float nz);
                Assert.AreEqual(t, back, t.ToString());
                Assert.AreEqual(-3.5f, z);
                Assert.AreEqual(0.6f, ny, 0.01f);
                Assert.AreEqual(-0.8f, nz, 0.01f);
            }
            var charge = new LiveCharge(7, 3, 1f, 2f, 3f, 1f, 0f, 0f, ChargeTarget.Ground, 99f);
            Messages.ChargeArmed(w, charge, 4.5f);
            var rr = new NetReader(w.ToArray());
            rr.Byte();
            var got = Messages.ReadChargeArmed(rr, out float left);
            Assert.AreEqual(7, got.Id);
            Assert.AreEqual(3, got.Thrower);
            Assert.AreEqual(1f, got.NX, 0.01f);
            Assert.AreEqual(4.5f, left);
        }
    }
}
