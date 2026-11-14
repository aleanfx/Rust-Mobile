using System;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>The metal age: scrap and parts from barrels and the ruins' crates, and the workbench they build.</summary>
    public class ProgressionTests
    {
        private const int Seed = 20261113;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        [Test]
        public void The_island_has_ruins_with_crates_well_apart()
        {
            var ground = new IslandGround(Seed);
            var monuments = Monuments.Find(ground);
            TestContext.WriteLine(string.Join("; ", monuments.Select(m => $"{m.Name} {m.Kind} ({m.X:0},{m.Y:0.0},{m.Z:0}) {m.Yaw}")));
            Assert.That(monuments.Count, Is.InRange(2, Monuments.Max));
            for (int i = 0; i < monuments.Count; i++)
            for (int j = i + 1; j < monuments.Count; j++)
            {
                float dx = monuments[i].X - monuments[j].X, dz = monuments[i].Z - monuments[j].Z;
                Assert.GreaterOrEqual(Math.Sqrt(dx * dx + dz * dz), Monuments.Spacing);
            }

            var nodes = ResourceLayout.Generate(ground);
            var crates = nodes.Where(n => n.Type == ResourceNodeType.Crate).ToList();
            Assert.GreaterOrEqual(crates.Count, monuments.Count * 3);
            Assert.AreEqual(nodes.Count, nodes.Select(n => n.Id).Distinct().Count(), "every node its own id");
            // Nothing of the wild grows on a monument's ground.
            foreach (var m in monuments)
                Assert.IsFalse(nodes.Any(n => n.Id < Monuments.FirstNodeId && Math.Abs(n.X - m.X) < Monuments.Radius && Math.Abs(n.Z - m.Z) < Monuments.Radius));
            // Each crate is on its monument's pad, inside its walls.
            foreach (var c in crates)
                Assert.IsTrue(monuments.Any(m => Math.Abs(c.X - m.X) < 11f && Math.Abs(c.Z - m.Z) < 11f && Math.Abs(c.Y - m.Y - 0.15f) < 0.01f), c.Id.ToString());
            Assert.IsTrue(monuments.All(m => Monuments.Props(m).Count > 10));
            // The same island every time.
            Assert.AreEqual(nodes.Count, ResourceLayout.Generate(new IslandGround(Seed)).Count);
        }

        [Test]
        public void A_crate_opens_by_hand_and_restocks()
        {
            var crate = new ResourceNode(1, ResourceNodeType.Crate);
            var loot = crate.Hit(ToolKind.None, new Random(5));
            Assert.IsTrue(crate.IsDepleted);
            Assert.IsTrue(loot.Any(y => y.ItemId == ItemIds.Scrap && y.Amount >= 8));
            Assert.IsEmpty(crate.Hit(ToolKind.None, new Random(5)), "empty until it restocks");
            Assert.IsTrue(crate.TickRespawn(GatherTable.RespawnSeconds(ResourceNodeType.Crate) + 1f));
            Assert.IsNotEmpty(crate.Hit(ToolKind.Rock, new Random(6)));

            // Over many crates: parts usually, guns now and then, every item a real one.
            var random = new Random(1);
            int parts = 0, guns = 0;
            for (int i = 0; i < 400; i++)
                foreach (var y in LootTable.Crate(random))
                {
                    Assert.IsTrue(Catalog.Contains(y.ItemId), y.ItemId);
                    if (y.ItemId == ItemIds.Gears || y.ItemId == ItemIds.MetalSpring || y.ItemId == ItemIds.MetalPipe) parts++;
                    if (y.ItemId == ItemIds.Revolver || y.ItemId == ItemIds.Blunderbuss) guns++;
                }
            Assert.That(parts, Is.InRange(250, 500));
            Assert.That(guns, Is.InRange(40, 120));
        }

        [Test]
        public void Barrels_give_scrap()
        {
            var random = new Random(3);
            for (int i = 0; i < 50; i++)
                Assert.IsTrue(LootTable.Barrel(random).Any(y => y.ItemId == ItemIds.Scrap && y.Amount >= 2));
        }

        [Test]
        public void Guns_and_metal_tools_are_made_only_near_a_workbench()
        {
            var ground = new IslandGround(Seed);
            var settlement = new Settlement(ground, Catalog);
            var recipes = RecipeBook.CreateDefault(Catalog);
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.Wood, 1000);
            s.Inventory.Add(ItemIds.MetalFragments, 1000);
            s.Inventory.Add(ItemIds.Scrap, 100);
            s.Inventory.Add(ItemIds.Cloth, 100);
            s.Inventory.Add(ItemIds.MetalPipe, 2);
            s.Inventory.Add(ItemIds.MetalSpring, 2);
            float x = 10f, y = ground.Height(10f, 10f), z = 10f;

            Assert.AreEqual(ActionResult.NeedsWorkbench, PlayerActions.Craft(s, recipes.Get("revolver"), settlement, x, y, z));
            Assert.AreEqual(ActionResult.NeedsWorkbench, PlayerActions.Craft(s, recipes.Get("metal_hatchet")), "no settlement given: no bench");
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Craft(s, recipes.Get("workbench"), settlement, x, y, z), "the bench itself needs none");
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Craft(s, recipes.Get("blunderbuss"), settlement, x, y, z), "nor does the crude gun");

            settlement.Add(new Deployable(0, DeployableKind.Workbench, x + 2f, y, z, 0f, 1));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Craft(s, recipes.Get("revolver"), settlement, x, y, z));
            Assert.AreEqual(ActionResult.NeedsWorkbench, PlayerActions.Craft(s, recipes.Get("revolver"), settlement, x + 9f, y, z), "too far from it");

            Assert.IsTrue(Deployables.TryKindOf(ItemIds.Workbench, out var kind));
            Assert.AreEqual(DeployableKind.Workbench, kind);
            Assert.AreEqual(ItemIds.Workbench, Deployables.ItemOf(DeployableKind.Workbench));
            Assert.IsTrue(recipes.All.Where(r => r.NeedsWorkbench).All(r => r.OutputItemId != ItemIds.Workbench));
        }
    }
}
