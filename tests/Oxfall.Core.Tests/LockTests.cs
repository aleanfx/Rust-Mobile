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
    public class MetalAndLockTests
    {
        private const int Seed = 20261113;
        private const float S = BuildingGrid.CellSize;
        private const int Me = 1, Raider = 2;

        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        /// <summary>A settlement, a well-off survivor, and a cell near the spawn where a floor fits.</summary>
        private static (Settlement w, Survivor s, Cell cell, float x, float y, float z) Setup()
        {
            var ground = new IslandGround(Seed);
            var w = new Settlement(ground, Catalog);
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.Wood, 3000);
            s.Inventory.Add(ItemIds.MetalFragments, 1000);
            var spawn = Spawns.Beach(ground, new System.Random(Seed ^ 0x5f3759df));
            var near = BuildingGrid.CellAt(spawn.X, spawn.Z);
            for (int r = 1; r < 30; r++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                var cell = new Cell(near.X + dx, near.Z + dz);
                if (PlayerActions.FoundationTop(w, cell, out _) != ActionResult.Ok) continue;
                float x = (cell.X + 0.5f) * S, z = (cell.Z - 0.6f) * S;
                return (w, s, cell, x, ground.Height(x, z), z);
            }
            Assert.Fail("No buildable cell near the spawn.");
            return default;
        }

        private static ActionResult Build(Settlement w, Survivor s, int owner, BuildOp op, float x, float y, float z) =>
            PlayerActions.Build(w, s, owner, op, x, y, z, out _);

        /// <summary>A floor and a doorway on its south side, with a door of this kind hung in it.</summary>
        private static Edge Doorway(Settlement w, Survivor s, Cell cell, float x, float y, float z, DoorKind kind)
        {
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Foundation(cell, 0f), x, y, z));
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Edge(cell.South, PieceType.Doorway), x, y, z));
            s.Inventory.Add(DoorKinds.ItemOf(kind), 1);
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Door(cell.South, kind), x, y, z));
            return cell.South;
        }

        [Test]
        public void MetalToolsGatherFasterAndAreMadeOfFragments()
        {
            Assert.Greater(GatherTable.PerHit(ResourceNodeType.Tree, ToolKind.MetalHatchet), GatherTable.PerHit(ResourceNodeType.Tree, ToolKind.Hatchet));
            Assert.Greater(GatherTable.PerHit(ResourceNodeType.Stone, ToolKind.MetalPickaxe), GatherTable.PerHit(ResourceNodeType.Stone, ToolKind.Pickaxe));
            Assert.Greater(GatherTable.PerHit(ResourceNodeType.MetalOre, ToolKind.MetalPickaxe), GatherTable.PerHit(ResourceNodeType.MetalOre, ToolKind.Pickaxe));
            var recipes = RecipeBook.CreateDefault(Catalog);
            foreach (var id in new[] { "metal_hatchet", "metal_pickaxe", "sheet_metal_door", "code_lock" })
            {
                Assert.IsTrue(recipes.TryGet(id, out var recipe), id);
                Assert.IsTrue(recipe.Ingredients.Any(i => i.ItemId == ItemIds.MetalFragments), id);
                Assert.IsTrue(Catalog.Contains(recipe.OutputItemId), id);
            }
        }

        [Test]
        public void AWallGoesUpToMetalForFragments()
        {
            var (w, s, cell, x, y, z) = Setup();
            s.Inventory.Add(ItemIds.Stone, 1000);
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Foundation(cell, 0f), x, y, z));
            var floor = PieceRef.Foundation(cell);
            Build(w, s, Me, BuildOp.Upgrade(floor, BuildingTier.Wood), x, y, z);
            Build(w, s, Me, BuildOp.Upgrade(floor, BuildingTier.Stone), x, y, z);
            int fragments = s.Inventory.Count(ItemIds.MetalFragments);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, Me, BuildOp.Upgrade(floor, BuildingTier.Twig), x, y, z, out var done));
            Assert.AreEqual(BuildingTier.Metal, done.Tier);
            Assert.AreEqual(fragments - BuildCosts.MetalUpgrade, s.Inventory.Count(ItemIds.MetalFragments));
            Assert.AreEqual(BuildingTiers.MaxHealth(BuildingTier.Metal), w.Grid.Health(floor));
            Assert.AreEqual(ActionResult.MaxTier, Build(w, s, Me, BuildOp.Upgrade(floor, BuildingTier.Twig), x, y, z));
        }

        [Test]
        public void ASheetMetalDoorHangsAndComesBackAsItself()
        {
            var (w, s, cell, x, y, z) = Setup();
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Foundation(cell, 0f), x, y, z));
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Edge(cell.South, PieceType.Doorway), x, y, z));
            Assert.AreEqual(ActionResult.NotEnoughResources, Build(w, s, Me, BuildOp.Door(cell.South, DoorKind.Metal), x, y, z));
            s.Inventory.Add(ItemIds.SheetMetalDoor, 1);
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Door(cell.South, DoorKind.Metal), x, y, z));
            Assert.AreEqual(DoorKind.Metal, w.Grid.DoorKindOf(cell.South));
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Remove(PieceRef.OnEdge(cell.South, PieceType.Doorway)), x, y, z));
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.SheetMetalDoor));
            Assert.AreEqual(0, s.Inventory.Count(ItemIds.WoodenDoor));
        }

        [Test]
        public void ALockedDoorOpensOnlyForThoseWhoKnowTheCode()
        {
            var (w, s, cell, x, y, z) = Setup();
            var raider = new Survivor(Catalog);
            var door = Doorway(w, s, cell, x, y, z, DoorKind.Metal);
            var target = LockTarget.Door(door);

            Assert.AreEqual(ActionResult.NotEnoughResources, Build(w, s, Me, BuildOp.PlaceLock(target), x, y, z));
            s.Inventory.Add(ItemIds.CodeLock, 1);
            Assert.AreEqual(ActionResult.TooFar, Build(w, s, Me, BuildOp.PlaceLock(target), x + 40f, y, z));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, Me, BuildOp.PlaceLock(target), x, y, z, out var placed));
            Assert.AreEqual(Me, placed.Deployed.Owner, "the server marks whose lock it is");
            Assert.AreEqual(0, s.Inventory.Count(ItemIds.CodeLock));
            Assert.AreEqual(ActionResult.Occupied, Build(w, s, Me, BuildOp.PlaceLock(target), x, y, z));

            // Unlocked until a code is chosen; only its owner chooses the first one.
            Assert.AreEqual(ActionResult.Ok, Build(w, raider, Raider, BuildOp.ToggleDoor(door, true), x, y, z));
            Assert.AreEqual(ActionResult.Ok, Build(w, raider, Raider, BuildOp.ToggleDoor(door, false), x, y, z));
            Assert.AreEqual(ActionResult.Locked, Build(w, raider, Raider, BuildOp.SetCode(target, 1111), x, y, z));
            Assert.AreEqual(ActionResult.Invalid, Build(w, s, Me, BuildOp.SetCode(target, 12345), x, y, z));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, Me, BuildOp.SetCode(target, 4821), x, y, z, out var set));
            Assert.AreEqual(CodeLocks.Hidden, set.Code, "nobody else is told the code");
            Assert.AreEqual(Me, set.Deployed.Owner);

            // Locked: the owner walks in, the raider does not, and pays for guessing.
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.ToggleDoor(door, true), x, y, z));
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.ToggleDoor(door, false), x, y, z));
            Assert.AreEqual(ActionResult.Locked, Build(w, raider, Raider, BuildOp.ToggleDoor(door, true), x, y, z));
            float health = raider.Vitals.Health;
            Assert.AreEqual(ActionResult.WrongCode, Build(w, raider, Raider, BuildOp.EnterCode(target, 1234), x, y, z));
            Assert.AreEqual(health - CodeLocks.WrongCodeShock, raider.Vitals.Health);
            Assert.AreEqual(ActionResult.Locked, Build(w, raider, Raider, BuildOp.RemoveLock(target), x, y, z));
            Assert.AreEqual(ActionResult.Locked, Build(w, raider, Raider, BuildOp.SetLocked(target, false), x, y, z));
            Assert.AreEqual(ActionResult.Invalid, Build(w, raider, Raider, BuildOp.LockAuthorize(target, Raider), x, y, z),
                "only the server says who is let in");

            // The right code lets a friend in for good.
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, raider, Raider, BuildOp.EnterCode(target, 4821), x, y, z, out var entered));
            Assert.AreEqual(BuildOpKind.LockAuthorize, entered.Kind);
            Assert.AreEqual(Raider, entered.Deployed.Owner);
            Assert.AreEqual(ActionResult.Ok, Build(w, raider, Raider, BuildOp.ToggleDoor(door, true), x, y, z));

            // A new code leaves only whoever set it on the list.
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.SetCode(target, 9090), x, y, z));
            Assert.AreEqual(ActionResult.Locked, Build(w, raider, Raider, BuildOp.ToggleDoor(door, false), x, y, z));

            // Unlocked, anyone passes; locking needs the list.
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.SetLocked(target, false), x, y, z));
            Assert.AreEqual(ActionResult.Ok, Build(w, raider, Raider, BuildOp.ToggleDoor(door, false), x, y, z));
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.SetLocked(target, true), x, y, z));

            // Demolishing the doorway gives the door and the lock back.
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Remove(PieceRef.OnEdge(door, PieceType.Doorway)), x, y, z));
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.CodeLock));
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.SheetMetalDoor));
            Assert.IsFalse(w.HasLock(target));
        }

        [Test]
        public void ALockedBoxAndCupboardKeepOthersOut()
        {
            var (w, s, cell, x, y, z) = Setup();
            var raider = new Survivor(Catalog);
            // On a floor, so the ground under them is level.
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Foundation(cell, 0f), x, y, z));
            w.Grid.TryGetFoundationTop(cell, out float top);
            float mx = (cell.X + 0.5f) * S, mz = (cell.Z + 0.5f) * S;
            s.Inventory.Add(ItemIds.StorageBox, 1);
            s.Inventory.Add(ItemIds.ToolCupboard, 1);
            s.Inventory.Add(ItemIds.Furnace, 1);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, Me,
                BuildOp.Deploy(new Deployable(0, DeployableKind.StorageBox, mx + 0.8f, top, mz, 0f)), x, y, z, out var box));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, Me,
                BuildOp.Deploy(new Deployable(0, DeployableKind.ToolCupboard, mx - 0.8f, top, mz, 0f)), x, y, z, out var tc));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, Me,
                BuildOp.Deploy(new Deployable(0, DeployableKind.Furnace, mx, top, mz + 0.9f, 0f)), x, y, z, out var furnace));
            s.Inventory.Add(ItemIds.CodeLock, 3);

            Assert.AreEqual(ActionResult.NoPiece, Build(w, s, Me, BuildOp.PlaceLock(LockTarget.On(furnace.Id)), x, y, z),
                "a furnace takes no lock");
            foreach (var id in new[] { box.Id, tc.Id })
            {
                var t = LockTarget.On(id);
                Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.PlaceLock(t), x, y, z));
                Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.SetCode(t, 7), x, y, z));
            }

            Assert.AreEqual(ActionResult.Locked, PlayerActions.Open(w, raider, Raider, box.Id, x, y, z));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, s, Me, box.Id, x, y, z));
            Assert.AreEqual(ActionResult.Locked, Build(w, raider, Raider, BuildOp.Authorize(tc.Id), x, y, z));
            Assert.IsFalse(w.IsAuthorized(tc.Id, Raider));
            Assert.AreEqual(ActionResult.NotYours, Build(w, raider, Raider, BuildOp.Pickup(box.Id), x, y, z));

            // Picking a locked box up (its owner, empty) brings the lock back too.
            PlayerActions.Close(s);
            int locks = s.Inventory.Count(ItemIds.CodeLock);
            Assert.AreEqual(ActionResult.Ok, Build(w, s, Me, BuildOp.Pickup(box.Id), x, y, z));
            Assert.AreEqual(locks + 1, s.Inventory.Count(ItemIds.CodeLock));
            Assert.IsFalse(w.HasLock(LockTarget.On(box.Id)));
        }

        [Test]
        public void ACopyKnowsWhoMayPassButNeverTheCode()
        {
            var (w, s, cell, x, y, z) = Setup();
            var friend = new Survivor(Catalog);
            var door = Doorway(w, s, cell, x, y, z, DoorKind.Wood);
            var target = LockTarget.Door(door);
            s.Inventory.Add(ItemIds.CodeLock, 1);
            Build(w, s, Me, BuildOp.PlaceLock(target), x, y, z);
            Build(w, s, Me, BuildOp.SetCode(target, 2580), x, y, z);
            Build(w, friend, 3, BuildOp.EnterCode(target, 2580), x, y, z);

            var copy = new Settlement(w.Ground, Catalog);
            foreach (var op in BuildOps.Describe(w))
                Assert.IsTrue(BuildOps.Apply(copy, op), op.ToString());
            Assert.IsTrue(copy.TryGetLock(target, out var l));
            Assert.AreEqual(Me, l.Owner);
            Assert.IsTrue(l.Locked);
            Assert.AreEqual(CodeLocks.Hidden, l.Code);
            Assert.IsTrue(copy.CanPass(target, Me));
            Assert.IsTrue(copy.CanPass(target, 3));
            Assert.IsFalse(copy.CanPass(target, Raider));
            Assert.IsFalse(copy.CodeMatches(target, 2580));
            Assert.AreEqual(DoorKind.Wood, copy.Grid.DoorKindOf(door));
        }

        [Test]
        public void ASavedLockKeepsItsCode()
        {
            var world = new ServerWorld(Seed);
            var (_, s, cell, x, y, z) = Setup();
            var w = world.Settlement;
            var door = Doorway(w, s, cell, x, y, z, DoorKind.Metal);
            var target = LockTarget.Door(door);
            s.Inventory.Add(ItemIds.CodeLock, 1);
            Build(w, s, Me, BuildOp.PlaceLock(target), x, y, z);
            Build(w, s, Me, BuildOp.SetCode(target, 31), x, y, z);
            Build(w, s, Me, BuildOp.SetLocked(target, false), x, y, z);

            var loaded = new ServerWorld(Seed);
            Assert.IsTrue(loaded.Load(world.Save(), out string why), why);
            var l2 = loaded.Settlement;
            Assert.IsTrue(l2.TryGetLock(target, out var l));
            Assert.IsFalse(l.Locked);
            Assert.IsTrue(l2.CodeMatches(target, 31));
            Assert.AreEqual(DoorKind.Metal, l2.Grid.DoorKindOf(door));
        }
    
        [Test]
        public void ABoxLockedAgainstALooterClosesOnThem()
        {
            var world = new ServerWorld(Seed);
            var owner = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var a, out _, out _);
            var looter = world.Join(Protocol.Version, "Bea", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 0f, out var b, out _, out _);
            var at = b.Pose;
            var box = world.Settlement.Add(new Deployable(0, DeployableKind.StorageBox, at.X + 1f, at.Y, at.Z, 0f));
            world.Players.Place(a.Id, new Pose(at.X - 1f, at.Y, at.Z, 0f), 1f);
            var target = LockTarget.On(box.Id);
            Assert.IsTrue(world.Settlement.AddLock(target, owner.Owner));
            owner.Survivor.Looting = box.Id;
            looter.Survivor.Looting = box.Id;

            world.Tick(0.1f);
            Assert.AreEqual(box.Id, looter.Survivor.Looting, "an open lock keeps no one out");
            world.Settlement.SetCode(target, 1111, owner.Owner);
            world.Tick(0.1f);
            Assert.AreEqual(0, looter.Survivor.Looting, "locked: the looter is out");
            Assert.AreEqual(box.Id, owner.Survivor.Looting, "whoever knows the code stays");
        }
}
}
