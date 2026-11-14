using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Upkeep: a stocked tool cupboard keeps a base up; what nobody keeps up decays and falls.</summary>
    public class UpkeepTests
    {
        private const int Seed = 20261114;
        private const float S = BuildingGrid.CellSize;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        private static Settlement NewSettlement() => new Settlement(new IslandGround(Seed), Catalog);

        /// <summary>A cupboard set down beside the square at (0, 0).</summary>
        private static Deployable Cupboard(Settlement w) =>
            w.Add(new Deployable(0, DeployableKind.ToolCupboard, S * 0.5f, 1f, S * 0.5f, 0f, 1));

        private static void Rounds(Settlement w, int rounds, List<BuildOp> decayed = null)
        {
            for (int i = 0; i < rounds; i++) w.Tick(Upkeep.Interval, decayed: decayed);
        }

        [Test]
        public void Twig_nobody_keeps_up_is_gone_within_the_hour()
        {
            var w = NewSettlement();
            var cell = new Cell(0, 0);
            w.Grid.PlaceFoundation(cell, 1f);
            var decayed = new List<BuildOp>();

            Rounds(w, 1, decayed);
            Assert.IsTrue(w.Grid.HasFoundation(cell), "it decays, slowly");
            Assert.Less(w.Grid.Health(PieceRef.Foundation(cell)), BuildingTiers.MaxHealth(BuildingTier.Twig));
            Assert.AreEqual(1, decayed.Count, "each step is told");
            Assert.AreEqual(BuildOpKind.Health, decayed[0].Kind);

            Rounds(w, 5, decayed);
            Assert.IsFalse(w.Grid.HasFoundation(cell), "an hour of twig decay takes it down");
            Assert.AreEqual(0f, decayed.Last().Top, "the last step tells it fell");
        }

        [Test]
        public void Nothing_happens_between_rounds()
        {
            var w = NewSettlement();
            var cell = new Cell(0, 0);
            w.Grid.PlaceFoundation(cell, 1f);
            for (int i = 0; i < 50; i++) w.Tick(Upkeep.Interval / 60f);
            Assert.AreEqual(BuildingTiers.MaxHealth(BuildingTier.Twig), w.Grid.Health(PieceRef.Foundation(cell)));
        }

        [Test]
        public void A_stocked_cupboard_pays_a_tenth_a_day_and_its_base_stands()
        {
            var w = NewSettlement();
            var cell = new Cell(0, 0);
            w.Grid.PlaceFoundation(cell, 1f);
            w.Grid.Upgrade(PieceRef.Foundation(cell), BuildingTier.Stone);
            var tc = Cupboard(w);
            w.Container(tc.Id).Add(ItemIds.Stone, 100);

            var cost = new Dictionary<string, float>();
            Assert.AreEqual(1, Upkeep.DailyCost(w, tc.Id, cost));
            Assert.AreEqual(BuildCosts.StoneUpgrade * Upkeep.DailyShare, cost[ItemIds.Stone], 0.001f);
            Assert.AreEqual(100f / 20f * Upkeep.Day, Upkeep.SecondsCovered(w, tc.Id, cost), 1f, "five days of stone");

            Rounds(w, (int)(Upkeep.Day / Upkeep.Interval));
            int left = w.Container(tc.Id).Count(ItemIds.Stone);
            Assert.That(left, Is.InRange(80, 81), "a day took about 20 stone");
            Assert.IsFalse(w.Upkeep.IsUnpaid(tc.Id));
            Assert.AreEqual(BuildingTiers.MaxHealth(BuildingTier.Stone), w.Grid.Health(PieceRef.Foundation(cell)), "kept whole");
        }

        [Test]
        public void An_empty_cupboard_does_not_stop_the_decay()
        {
            var w = NewSettlement();
            var cell = new Cell(0, 0);
            w.Grid.PlaceFoundation(cell, 1f);
            w.Grid.Upgrade(PieceRef.Foundation(cell), BuildingTier.Stone);
            var tc = Cupboard(w);

            Rounds(w, 1);
            Assert.IsTrue(w.Upkeep.IsUnpaid(tc.Id));
            float max = BuildingTiers.MaxHealth(BuildingTier.Stone);
            Assert.AreEqual(max - max * Upkeep.Interval / Upkeep.DecaySeconds(BuildingTier.Stone), w.Grid.Health(PieceRef.Foundation(cell)), 0.01f);

            // Stocked again, it pays and the decay stops (the damage stays until the hammer mends it).
            w.Container(tc.Id).Add(ItemIds.Stone, 50);
            float hurt = w.Grid.Health(PieceRef.Foundation(cell));
            Rounds(w, 3);
            Assert.IsFalse(w.Upkeep.IsUnpaid(tc.Id));
            Assert.AreEqual(hurt, w.Grid.Health(PieceRef.Foundation(cell)));
        }

        [Test]
        public void Pieces_out_of_the_cupboards_reach_still_decay()
        {
            var w = NewSettlement();
            var near = new Cell(0, 0);
            var far = BuildingGrid.CellAt(Deployables.PrivilegeRadius + 10f, 0f);
            w.Grid.PlaceFoundation(near, 1f);
            w.Grid.PlaceFoundation(far, 1f);
            var tc = Cupboard(w);
            w.Container(tc.Id).Add(ItemIds.Wood, 100);

            Rounds(w, 1);
            Assert.AreEqual(BuildingTiers.MaxHealth(BuildingTier.Twig), w.Grid.Health(PieceRef.Foundation(near)));
            Assert.Less(w.Grid.Health(PieceRef.Foundation(far)), BuildingTiers.MaxHealth(BuildingTier.Twig));
        }

        [Test]
        public void Each_tier_costs_its_own_material()
        {
            Assert.AreEqual(ItemIds.Wood, Upkeep.PerDay(PieceType.Wall, BuildingTier.Twig, out float twig).ItemId);
            Assert.AreEqual(BuildCosts.TwigWood * Upkeep.DailyShare, twig, 0.001f);
            Assert.AreEqual(ItemIds.Wood, Upkeep.PerDay(PieceType.Wall, BuildingTier.Wood, out _).ItemId);
            Assert.AreEqual(ItemIds.Stone, Upkeep.PerDay(PieceType.Wall, BuildingTier.Stone, out _).ItemId);
            Assert.AreEqual(ItemIds.MetalFragments, Upkeep.PerDay(PieceType.Wall, BuildingTier.Metal, out _).ItemId);
            // Better walls last longer unkept.
            Assert.Less(Upkeep.DecaySeconds(BuildingTier.Twig), Upkeep.DecaySeconds(BuildingTier.Wood));
            Assert.Less(Upkeep.DecaySeconds(BuildingTier.Stone), Upkeep.DecaySeconds(BuildingTier.Metal));
        }

        [Test]
        public void The_cupboard_keeps_only_materials()
        {
            var w = NewSettlement();
            var tc = Cupboard(w);
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.Wood, 300);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, s, 1, tc.Id, tc.X + 1f, tc.Y, tc.Z));
            int wood = Enumerable.Range(0, s.Inventory.SlotCount).First(i => s.Inventory[i].ItemId == ItemIds.Wood);
            int rock = Enumerable.Range(0, s.Inventory.SlotCount).First(i => s.Inventory[i].ItemId == ItemIds.Rock);

            Assert.AreEqual(ActionResult.NotUpkeep, PlayerActions.Put(w, s, rock, -1));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Put(w, s, wood, -1));
            Assert.AreEqual(300, w.Container(tc.Id).Count(ItemIds.Wood));
        }

        [Test]
        public void On_the_server_decay_is_announced()
        {
            var world = new ServerWorld(Seed);
            var cell = new Cell(0, 0);
            world.Settlement.Grid.PlaceFoundation(cell, 1f);
            world.Announcements.Clear();
            world.Settlement.Upkeep.Hurry();
            world.Tick(0.05f);
            Assert.IsTrue(world.Announcements.Any(op => op.Kind == BuildOpKind.Health), "everyone sees it decay");
        }
    }
}
