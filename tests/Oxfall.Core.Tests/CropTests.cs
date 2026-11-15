using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Farming: seeds from wild plants, sown in open ground, grow through stages, ripen, harvest, wither.</summary>
    public class CropTests
    {
        private const int Seed = 20261114;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        /// <summary>A spot of open, dry, flat-enough ground away from the ruins and the island's trees and rocks.</summary>
        private static (float X, float Y, float Z) Field(Settlement w)
        {
            var g = w.Ground;
            for (float x = -120f; x < 120f; x += 3f)
                for (float z = -120f; z < 120f; z += 3f)
                {
                    float y = g.Height(x, z);
                    if (y < IslandGround.WaterLevel + 1f || Monuments.NoBuilding(g, x, z)) continue;
                    if (w.Obstacles != null && w.Obstacles.Blocks(x, z, 2f)) continue;
                    if (PlayerActions.CanDeploy(w, 1, DeployableKind.HempPlant, x, y, z) != ActionResult.Ok) continue;
                    if (PlayerActions.CanDeploy(w, 1, DeployableKind.HempPlant, x + 1.5f, g.Height(x + 1.5f, z), z) != ActionResult.Ok) continue;
                    return (x, y, z);
                }
            Assert.Fail("no field on the island");
            return default;
        }

        private static Settlement World()
        {
            var g = new IslandGround(Seed);
            var w = new Settlement(g, Catalog) { DropsLoot = true };
            w.Obstacles = new Obstacles(ResourceLayout.Generate(g));
            return w;
        }

        [Test]
        public void Seeds_are_items_that_sow_a_crop_and_come_from_wild_plants()
        {
            Assert.IsTrue(Catalog.Contains(ItemIds.HempSeed));
            Assert.IsTrue(Catalog.Contains(ItemIds.BerrySeed));
            Assert.IsTrue(Deployables.TryKindOf(ItemIds.HempSeed, out var hemp));
            Assert.AreEqual(DeployableKind.HempPlant, hemp);
            Assert.IsTrue(Deployables.TryKindOf(ItemIds.BerrySeed, out var berry));
            Assert.AreEqual(DeployableKind.BerryPlant, berry);
            Assert.AreEqual(ItemIds.HempSeed, Deployables.ItemOf(DeployableKind.HempPlant));
            var wild = new ResourceNode(1, ResourceNodeType.Hemp).Hit(ToolKind.None);
            Assert.IsTrue(wild.Any(y => y.ItemId == ItemIds.HempSeed));
        }

        [Test]
        public void A_crop_goes_through_its_stages_and_rain_speeds_it()
        {
            var k = DeployableKind.HempPlant;
            float grow = Crops.GrowSeconds(k);
            Assert.AreEqual(CropStage.Sprout, Crops.Stage(k, 0f));
            Assert.AreEqual(CropStage.Young, Crops.Stage(k, grow * 0.4f));
            Assert.AreEqual(CropStage.Grown, Crops.Stage(k, grow * 0.8f));
            Assert.AreEqual(CropStage.Ripe, Crops.Stage(k, grow));
            Assert.AreEqual(CropStage.Withered, Crops.Stage(k, grow + Crops.RipeSeconds));
            Assert.AreEqual(1f, Crops.Rate(0f));
            Assert.Greater(Crops.Rate(0.8f), 1f);
            Assert.Greater(Crops.GrowSeconds(DeployableKind.BerryPlant), 0f);
        }

        [Test]
        public void Sown_in_the_ground_it_grows_tells_its_stages_and_harvests_for_more_than_a_wild_plant()
        {
            var w = World();
            var s = new Survivor(Catalog);
            s.Inventory.Clear();
            s.Inventory.Add(ItemIds.HempSeed, 2);
            var (x, y, z) = Field(w);

            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 1, BuildOp.Deploy(new Deployable(0, DeployableKind.HempPlant, x, y, z, 0f)), x + 1f, y, z, out var done));
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.HempSeed));
            int id = done.Deployed.Id;
            Assert.AreEqual(CropStage.Sprout, w.CropStageOf(id));
            // Too close to the first.
            Assert.AreEqual(ActionResult.TooClose, PlayerActions.Build(w, s, 1, BuildOp.Deploy(new Deployable(0, DeployableKind.HempPlant, x + 0.3f, y, z, 0f)), x + 1f, y, z, out _));

            // Not ripe yet: nothing to harvest.
            Assert.AreEqual(ActionResult.NotRipe, PlayerActions.Build(w, s, 2, BuildOp.Harvest(id), x + 1f, y, z, out _));

            var told = new System.Collections.Generic.List<BuildOp>();
            for (int i = 0; i < 60; i++) w.Tick(Crops.HempSeconds / 50f, decayed: told);
            Assert.AreEqual(CropStage.Ripe, w.CropStageOf(id));
            var grows = told.Where(o => o.Kind == BuildOpKind.Grow && o.Id == id).ToList();
            Assert.AreEqual(3, grows.Count, "young, grown, ripe");

            // A copy told the same ops agrees.
            var copy = new Settlement(w.Ground, Catalog);
            foreach (var op in BuildOps.Describe(w)) BuildOps.Apply(copy, op);
            Assert.AreEqual(CropStage.Ripe, copy.CropStageOf(id));

            // Anyone may harvest a ripe crop.
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 2, BuildOp.Harvest(id), x + 1f, y, z, out var harvested));
            Assert.AreEqual(BuildOpKind.Pickup, harvested.Kind);
            Assert.IsFalse(w.TryGet(id, out _));
            Assert.Greater(s.Inventory.Count(ItemIds.Cloth), GatherTable.Capacity(ResourceNodeType.Hemp));
            Assert.AreEqual(3, s.Inventory.Count(ItemIds.HempSeed));
        }

        [Test]
        public void Rain_ripens_sooner_and_left_too_long_it_withers()
        {
            var w = World();
            var (x, y, z) = Field(w);
            var crop = w.Add(new Deployable(0, DeployableKind.BerryPlant, x, y, z, 0f, 1));
            w.GrowthRate = Crops.Rate(1f);
            w.Tick(Crops.BerrySeconds / Crops.RainBoost + 1f);
            Assert.AreEqual(CropStage.Ripe, w.CropStageOf(crop.Id));
            w.GrowthRate = 1f;
            w.Tick(Crops.RipeSeconds + 1f);
            Assert.AreEqual(CropStage.Withered, w.CropStageOf(crop.Id));
            var s = new Survivor(Catalog);
            s.Inventory.Clear();
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 1, BuildOp.Harvest(crop.Id), x, y, z, out _));
            Assert.AreEqual(0, s.Inventory.Count(ItemIds.Berries));
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.BerrySeed));
        }

        [Test]
        public void Only_the_sower_pulls_up_a_young_crop_and_the_seed_comes_back()
        {
            var w = World();
            var (x, y, z) = Field(w);
            var crop = w.Add(new Deployable(0, DeployableKind.HempPlant, x, y, z, 0f, 1));
            var thief = new Survivor(Catalog);
            Assert.AreEqual(ActionResult.NotYours, PlayerActions.Build(w, thief, 2, BuildOp.Pickup(crop.Id), x, y, z, out _));
            var me = new Survivor(Catalog);
            me.Inventory.Clear();
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, me, 1, BuildOp.Pickup(crop.Id), x, y, z, out _));
            Assert.AreEqual(1, me.Inventory.Count(ItemIds.HempSeed));
        }

        [Test]
        public void Seeds_go_in_the_earth_not_on_a_floor()
        {
            var w = World();
            var (x, y, z) = Field(w);
            var cell = BuildingGrid.CellAt(x, z);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.FoundationTop(w, cell, out float top));
            w.Grid.PlaceFoundation(cell, top);
            Assert.AreEqual(ActionResult.NeedsSoil, PlayerActions.CanDeploy(w, 1, DeployableKind.HempPlant, x, top, z));
            Assert.AreEqual(ActionResult.NeedsSoil, PlayerActions.CanDeploy(w, 1, DeployableKind.HempPlant, x, y, z));
        }

        [Test]
        public void The_server_saves_how_far_crops_have_grown()
        {
            var a = new ServerWorld(Seed);
            var (x, y, z) = Field(a.Settlement);
            var crop = a.Settlement.Add(new Deployable(0, DeployableKind.HempPlant, x, y, z, 0f, 1));
            a.Settlement.SetCropAge(crop.Id, 300f);
            var b = new ServerWorld(Seed);
            Assert.IsTrue(b.Load(a.Save(), out string why), why);
            Assert.AreEqual(300f, b.Settlement.CropAge(crop.Id), 0.01f);
        }
    }
}
