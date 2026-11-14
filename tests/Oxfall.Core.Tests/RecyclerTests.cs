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
    /// <summary>The ruins' recyclers: where scrap comes from, and the ground round the ruins nobody builds on.</summary>
    public class RecyclerTests
    {
        private const int Seed = 20261113;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        private static Deployable FirstRecycler(Settlement settlement) =>
            settlement.Deployables.Single(d => d.Id == Settlement.RecyclerId(0));

        [Test]
        public void Every_ruin_has_a_recycler_nobody_can_take_or_break()
        {
            var ground = new IslandGround(Seed);
            var settlement = new Settlement(ground, Catalog) { DropsLoot = true };
            var monuments = Monuments.Find(ground);
            var recyclers = settlement.Deployables.Where(d => d.Kind == DeployableKind.Recycler).ToList();
            Assert.AreEqual(monuments.Count, recyclers.Count);
            Assert.AreEqual(0, settlement.DeployableCount, "they are not anyone's");
            foreach (var r in recyclers)
            {
                Assert.Less(r.Id, 0);
                Assert.IsTrue(monuments.Any(m => (m.X - r.X) * (m.X - r.X) + (m.Z - r.Z) * (m.Z - r.Z) < Monuments.Radius * Monuments.Radius));
                Assert.AreEqual(6, settlement.Container(r.Id).SlotCount);
                Assert.AreEqual(0f, Deployables.MaxHealth(DeployableKind.Recycler), "nothing breaks it");
            }

            var s = new Survivor(Catalog);
            var first = FirstRecycler(settlement);
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Build(settlement, s, 1, BuildOp.Pickup(first.Id), first.X, first.Y, first.Z, out _));
            Assert.IsFalse(settlement.IsLockable(LockTarget.On(first.Id)));

            // A copy (a phone's, after the server's Clear) gets the same ones back, with the same ids.
            settlement.Clear();
            Assert.IsTrue(settlement.TryGet(first.Id, out var again) && again.Kind == DeployableKind.Recycler);
            Assert.AreEqual(first.X, again.X);
        }

        [Test]
        public void Parts_go_in_and_scrap_comes_out()
        {
            var ground = new IslandGround(Seed);
            var settlement = new Settlement(ground, Catalog) { DropsLoot = true };
            var r = FirstRecycler(settlement);
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.Gears, 2);
            s.Inventory.Add(ItemIds.MetalPipe, 1);
            s.Inventory.Add(ItemIds.Wood, 50);
            float x = r.X, y = r.Y, z = r.Z + 1.5f;
            var box = settlement.Container(r.Id);

            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(settlement, s, 1, r.Id, x, y, z));
            Assert.AreEqual(ActionResult.NothingToRecycle, PlayerActions.Build(settlement, s, 1, BuildOp.Light(r.Id, true), x, y, z, out _), "empty");
            int wood = Slot(s.Inventory, ItemIds.Wood);
            Assert.AreEqual(ActionResult.NothingToRecycle, PlayerActions.Put(settlement, s, wood, -1), "wood is not recycled");
            Assert.AreEqual(ActionResult.NoSpace, PlayerActions.Put(settlement, s, Slot(s.Inventory, ItemIds.Gears), 4), "the bottom row only gives out");
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Put(settlement, s, Slot(s.Inventory, ItemIds.Gears), -1));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Put(settlement, s, Slot(s.Inventory, ItemIds.MetalPipe), -1));
            Assert.IsTrue(Enumerable.Range(0, 3).All(i => box[i].IsEmpty || box[i].ItemId != ItemIds.Scrap));

            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(settlement, s, 1, BuildOp.Light(r.Id, true), x, y, z, out var on));
            Assert.IsTrue(on.Open && settlement.IsLit(r.Id));

            // One thing every few seconds; then it stops by itself.
            var wentOut = new List<int>();
            settlement.Tick(Recycling.Seconds + 0.01f, wentOut);
            Assert.AreEqual(10, box.Count(ItemIds.Scrap), "a gear first");
            Assert.AreEqual(13, box.Count(ItemIds.MetalFragments));
            Assert.IsEmpty(wentOut);
            for (int i = 0; i < 4; i++) settlement.Tick(Recycling.Seconds, wentOut);
            Assert.AreEqual(25, box.Count(ItemIds.Scrap), "two gears and a pipe");
            Assert.AreEqual(13 * 2 + 8, box.Count(ItemIds.MetalFragments));
            Assert.AreEqual(0, box.Count(ItemIds.Gears) + box.Count(ItemIds.MetalPipe));
            Assert.IsFalse(settlement.IsLit(r.Id));
            CollectionAssert.AreEqual(new[] { r.Id }, wentOut);
            Assert.IsTrue(Enumerable.Range(3, 3).Any(i => box[i].ItemId == ItemIds.Scrap), "out in the bottom row");

            // And taken from there.
            int scrapSlot = Enumerable.Range(3, 3).First(i => box[i].ItemId == ItemIds.Scrap);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Take(settlement, s, scrapSlot, -1));
            Assert.AreEqual(25, s.Inventory.Count(ItemIds.Scrap));
        }

        [Test]
        public void Made_things_give_back_half_and_raw_ones_nothing()
        {
            var catalog = Catalog;
            var recycling = new Recycling(catalog, RecipeBook.CreateDefault(catalog));
            var revolver = recycling.Yield(ItemIds.Revolver);
            Assert.AreEqual(62, revolver.Single(y => y.ItemId == ItemIds.MetalFragments).Amount);
            Assert.AreEqual(15, revolver.Single(y => y.ItemId == ItemIds.Cloth).Amount);
            Assert.IsTrue(recycling.Takes(ItemIds.MetalHatchet));
            Assert.IsTrue(recycling.Takes(ItemIds.SheetMetalDoor));
            Assert.IsTrue(recycling.Yield(ItemIds.Workbench).Any(y => y.ItemId == ItemIds.Scrap));
            foreach (string raw in new[] { ItemIds.Wood, ItemIds.Stone, ItemIds.Scrap, ItemIds.MetalFragments, ItemIds.CookedMeat, ItemIds.PistolBullet, ItemIds.Bandage, ItemIds.Gunpowder })
                Assert.IsFalse(recycling.Takes(raw), raw);
            // Nothing comes out that would go round again: what it gives is never a part.
            foreach (var def in catalog.All)
                foreach (var y in recycling.Yield(def.Id))
                    Assert.IsFalse(y.ItemId == ItemIds.Gears || y.ItemId == ItemIds.MetalSpring || y.ItemId == ItemIds.MetalPipe, def.Id);
        }

        [Test]
        public void Nobody_builds_in_the_ruins()
        {
            var ground = new IslandGround(Seed);
            var settlement = new Settlement(ground, Catalog);
            var m = Monuments.Find(ground)[0];
            var cell = BuildingGrid.CellAt(m.X + 3f, m.Z + 3f);
            Assert.AreEqual(ActionResult.Monument, PlayerActions.FoundationTop(settlement, cell, out _));
            Assert.AreEqual(ActionResult.Monument, PlayerActions.CanDeploy(settlement, 1, DeployableKind.StorageBox, m.X + 4f, m.Y + 0.15f, m.Z));
            Assert.AreEqual(ActionResult.Monument,
                PlayerActions.CanDeploy(settlement, 1, DeployableKind.SleepingBag, m.X + Monuments.Radius + 3f, ground.Height(m.X + Monuments.Radius + 3f, m.Z), m.Z),
                "nor just outside its walls");
        }

        [Test]
        public void Nothing_is_built_over_a_tree_or_a_boulder()
        {
            var world = new ServerWorld(Seed);
            foreach (var type in new[] { ResourceNodeType.Tree, ResourceNodeType.Stone })
            {
                var node = ResourceLayout.Generate(world.Ground).First(n => n.Type == type && !Monuments.NoBuilding(world.Ground, n.X, n.Z));
                Assert.AreEqual(ActionResult.Obstructed, PlayerActions.FoundationTop(world.Settlement, BuildingGrid.CellAt(node.X, node.Z), out _), type.ToString());
                Assert.AreEqual(ActionResult.Obstructed, PlayerActions.CanDeploy(world.Settlement, 1, DeployableKind.StorageBox, node.X + 0.3f, node.Y, node.Z), type.ToString());
            }
        }

        [Test]
        public void A_saved_island_keeps_what_is_in_its_recyclers()
        {
            var world = new ServerWorld(Seed);
            int id = Settlement.RecyclerId(0);
            world.Settlement.Container(id).Add(ItemIds.Gears, 5);
            world.Settlement.Container(id).AddOutput(ItemIds.Scrap, 30);
            var loaded = new ServerWorld(Seed);
            Assert.IsTrue(loaded.Load(world.Save(), out var why), why);
            Assert.AreEqual(5, loaded.Settlement.Container(id).Count(ItemIds.Gears));
            Assert.AreEqual(30, loaded.Settlement.Container(id).Count(ItemIds.Scrap));
            Assert.IsTrue(loaded.Settlement.Container(id).IsOutputSlot(Enumerable.Range(0, 6).First(i => loaded.Settlement.Container(id)[i].ItemId == ItemIds.Scrap)));
            Assert.AreEqual(world.Settlement.Deployables.Count(), loaded.Settlement.Deployables.Count());
        }

        private static int Slot(Inventory inventory, string item) =>
            Enumerable.Range(0, inventory.SlotCount).First(i => inventory[i].ItemId == item);
    }
}
