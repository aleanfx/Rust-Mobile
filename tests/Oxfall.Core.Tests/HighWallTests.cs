using System;
using System.Collections.Generic;
using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>High external walls and ladders: where they go, what they stop, how they fall and are mended.</summary>
    public class HighWallTests
    {
        private const int Seed = 20261114;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        /// <summary>A dry, gentle stretch of ground away from the ruins, 16 m across, and its height in the middle.</summary>
        private static (float X, float Y, float Z) FlatSpot(IslandGround ground)
        {
            for (float x = -200f; x <= 200f; x += 6f)
            for (float z = -200f; z <= 200f; z += 6f)
            {
                float h = ground.Height(x, z), lo = h, hi = h;
                bool ok = !Monuments.NoBuilding(ground, x, z);
                for (float dx = -8f; dx <= 8f && ok; dx += 2f)
                for (float dz = -8f; dz <= 8f && ok; dz += 2f)
                {
                    float g = ground.Height(x + dx, z + dz);
                    lo = Math.Min(lo, g);
                    hi = Math.Max(hi, g);
                    if (Monuments.NoBuilding(ground, x + dx, z + dz)) ok = false;
                }
                if (ok && lo > IslandGround.WaterLevel + 1f && hi - lo < 0.8f) return (x, h, z);
            }
            Assert.Fail("no flat spot on the island");
            return default;
        }

        private static ActionResult Deploy(Settlement w, Survivor s, int owner, DeployableKind kind, float x, float z, float yaw, out BuildOp done)
        {
            float y = w.Ground.Height(x, z);
            s.Inventory.Add(Deployables.ItemOf(kind), 1);
            return PlayerActions.Build(w, s, owner, BuildOp.Deploy(new Deployable(0, kind, x, y, z, yaw)), x, y, z, out done);
        }

        [Test]
        public void Walls_meet_end_to_end_and_at_corners_but_do_not_cross()
        {
            var wood = DeployableKind.WoodWall;
            Assert.IsFalse(HighWalls.Overlap(wood, 0f, 0f, 0f, wood, HighWalls.Length, 0f, 0f), "in a line, end to end");
            Assert.IsFalse(HighWalls.Overlap(wood, 0f, 0f, 0f, wood, 2f, 2f, 90f), "a right-angled corner");
            Assert.IsTrue(HighWalls.Overlap(wood, 0f, 0f, 0f, wood, 0f, 0f, 90f), "crossed");
            Assert.IsTrue(HighWalls.Overlap(wood, 0f, 0f, 0f, wood, 0f, 0.2f, 0f), "side by side");
            Assert.IsTrue(HighWalls.Overlap(wood, 0f, 0f, 0f, DeployableKind.StorageBox, 1.5f, 0.3f, 0f), "a box in it");
            Assert.IsFalse(HighWalls.Overlap(wood, 0f, 0f, 0f, DeployableKind.StorageBox, 1.5f, 1.2f, 0f), "a box beside it");
            Assert.IsFalse(HighWalls.Overlap(wood, 0f, 0f, 0f, DeployableKind.Ladder, 1f, HighWalls.Thickness * 0.5f + HighWalls.LadderGap, 0f),
                "a ladder leaning on it");
        }

        [Test]
        public void A_wall_goes_on_open_ground_where_the_player_may_build()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog);
            var (x, _, z) = FlatSpot(w.Ground);
            var s = new Survivor(Catalog);
            Assert.AreEqual(ActionResult.Ok, Deploy(w, s, 1, DeployableKind.WoodWall, x, z, 0f, out var done));
            Assert.AreEqual(DeployableKind.WoodWall, done.Deployed.Kind);
            Assert.AreEqual(1, w.WallCount);
            Assert.AreEqual(ActionResult.Ok, Deploy(w, s, 1, DeployableKind.StoneWall, x + HighWalls.Length, z, 0f, out _), "the next one along");
            Assert.AreEqual(ActionResult.TooClose, Deploy(w, s, 1, DeployableKind.WoodWall, x + 1f, z, 90f, out _), "not across another");

            // Not through a building.
            var cell = BuildingGrid.CellAt(x, z + 6f);
            Assert.AreEqual(PlaceResult.Ok, w.Grid.PlaceFoundation(cell, w.Ground.Height(x, z + 6f) + 0.3f));
            float mx = (cell.X + 0.5f) * BuildingGrid.CellSize, mz = (cell.Z + 0.5f) * BuildingGrid.CellSize;
            Assert.AreEqual(ActionResult.OnBuilding, PlayerActions.CanDeploy(w, 1, DeployableKind.WoodWall, mx, w.Ground.Height(mx, mz), mz, 0f));

            // Not where someone else's cupboard reaches, even with only one end in it.
            var cupboard = w.Add(new Deployable(0, DeployableKind.ToolCupboard, x, w.Ground.Height(x, z - 26f), z - 26f, 0f, 9));
            Assert.AreEqual(ActionResult.BuildingBlocked, PlayerActions.CanDeploy(w, 1, DeployableKind.WoodWall, x, w.Ground.Height(x, z - 2f), z - 2f, 90f));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.CanDeploy(w, 9, DeployableKind.WoodWall, x, w.Ground.Height(x, z - 2f), z - 2f, 90f));
            Assert.IsTrue(w.TryGet(cupboard.Id, out _));
        }

        [Test]
        public void A_ladder_needs_a_wall_at_its_back_but_no_permission()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog);
            var (x, _, z) = FlatSpot(w.Ground);
            var s = new Survivor(Catalog);
            Assert.AreEqual(ActionResult.Ok, Deploy(w, s, 1, DeployableKind.WoodWall, x, z, 0f, out _));
            // Someone else's cupboard covers the wall.
            w.Add(new Deployable(0, DeployableKind.ToolCupboard, x + 3f, w.Ground.Height(x + 3f, z + 3f), z + 3f, 0f, 1));
            float gap = HighWalls.Thickness * 0.5f + HighWalls.LadderGap;
            Assert.AreEqual(ActionResult.NoSupport, Deploy(w, s, 2, DeployableKind.Ladder, x, z - 3f, 180f, out _), "nothing to lean on");
            Assert.AreEqual(ActionResult.NoSupport, Deploy(w, s, 2, DeployableKind.Ladder, x, z - gap, 0f, out _), "facing the wall");
            Assert.AreEqual(ActionResult.Ok, Deploy(w, s, 2, DeployableKind.Ladder, x, z - gap, 180f, out _), "leaning on it, from outside");
            Assert.AreEqual(1, w.LadderCount);
        }

        [Test]
        public void A_ladder_leans_on_a_buildings_wall_too()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog);
            var (x, y, z) = FlatSpot(w.Ground);
            var cell = BuildingGrid.CellAt(x, z);
            float top = y + 0.4f;
            Assert.AreEqual(PlaceResult.Ok, w.Grid.PlaceFoundation(cell, top));
            Assert.AreEqual(PlaceResult.Ok, w.Grid.PlaceEdge(cell.South, PieceType.Wall));
            float sx = (cell.X + 0.5f) * BuildingGrid.CellSize, sz = cell.Z * BuildingGrid.CellSize - HighWalls.LadderGap - 0.05f;
            Assert.IsTrue(HighWalls.LadderSupported(w, sx, w.Ground.Height(sx, sz), sz, 180f));
            Assert.IsFalse(HighWalls.LadderSupported(w, sx, w.Ground.Height(sx, sz - 2f), sz - 2f, 180f));
        }

        [Test]
        public void Someone_in_front_of_a_ladder_climbs_it_up_to_its_top()
        {
            var ladder = new Deployable(1, DeployableKind.Ladder, 0f, 10f, 0f, 180f);
            // Facing -Z: its front is towards -Z.
            Assert.IsTrue(HighWalls.OnLadder(ladder, 0f, 10f, -0.4f, out float top));
            Assert.AreEqual(10f + HighWalls.LadderHeight, top, 1e-4f);
            Assert.IsTrue(HighWalls.OnLadder(ladder, 0.3f, 13f, -0.3f, out _), "halfway up");
            Assert.IsFalse(HighWalls.OnLadder(ladder, 0f, 10f, -1.5f, out _), "stepped back from it");
            Assert.IsFalse(HighWalls.OnLadder(ladder, 1.2f, 11f, -0.3f, out _), "beside it");
            Assert.IsFalse(HighWalls.OnLadder(ladder, 0f, top, -0.3f, out _), "over its top");
            Assert.IsTrue(HighWalls.LadderHeight > HighWalls.Height + 0.4f, "it reaches over a high wall");
        }

        [Test]
        public void A_wall_stops_sight_and_blasts_but_not_over_its_top()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog);
            var (x, y, z) = FlatSpot(w.Ground);
            w.Add(new Deployable(0, DeployableKind.WoodWall, x, y, z, 0f, 1));
            Assert.IsTrue(HighWalls.LineBlocked(w, x, y + 1f, z - 3f, x + 0.5f, y + 1f, z + 3f));
            Assert.IsFalse(HighWalls.LineBlocked(w, x, y + 7f, z - 3f, x, y + 7f, z + 3f), "over it");
            Assert.IsFalse(HighWalls.LineBlocked(w, x + 3f, y + 1f, z - 3f, x + 3f, y + 1f, z + 3f), "past its end");
            Assert.IsTrue(Explosives.Shielded(w, x, y + 1f, z - 1f, x, y + 1f, z + 1f), "a charge's blast stops at it");
            Assert.IsTrue(Fauna.Walls(w)(x, y + 1.5f, z - 5f, x, y + 1.2f, z + 5f), "a bandit cannot see through it");
            Assert.IsTrue(Fauna.Barriers(w, null)(x + 1f, z + 0.3f), "an animal cannot walk through it");
            Assert.IsFalse(Fauna.Barriers(w, null)(x + 1f, z + 2f));
        }

        [Test]
        public void Charges_bring_down_a_wooden_wall_in_two_and_a_stone_one_in_four()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog) { DropsLoot = true };
            var (x, y, z) = FlatSpot(w.Ground);
            var wood = w.Add(new Deployable(0, DeployableKind.WoodWall, x, y, z, 0f, 1));
            var stone = w.Add(new Deployable(0, DeployableKind.StoneWall, x + 8f, y, z, 0f, 1));
            var changes = new List<BuildOp>();
            // A charge stuck on its outside face, a metre up.
            Explosives.Splash(w, x + 0.5f, y + 1f, z - 0.25f, changes);
            Assert.AreEqual(250f - Explosives.BlastDamage, w.DeployableHealth(wood.Id), 1e-3f);
            Assert.AreEqual(Deployables.MaxHealth(DeployableKind.StoneWall), w.DeployableHealth(stone.Id), "the other wall is too far");
            Explosives.Splash(w, x - 1f, y + 2f, z - 0.25f, changes);
            Assert.IsFalse(w.TryGet(wood.Id, out _), "two charges and it is down");
            Assert.AreEqual(0, w.WallCount - 1);
            for (int i = 0; i < 3; i++) Explosives.Splash(w, x + 8f, y + 1f, z - 0.25f, changes);
            Assert.IsTrue(w.TryGet(stone.Id, out _), "three are not enough for stone");
            Explosives.Splash(w, x + 8f, y + 1f, z - 0.25f, changes);
            Assert.IsFalse(w.TryGet(stone.Id, out _));
            Assert.IsTrue(changes.Exists(c => c.Kind == BuildOpKind.DeployableHealth && c.Id == wood.Id && c.HealthLeft <= 0f));
        }

        [Test]
        public void A_box_behind_a_wall_is_safe_from_a_charge_on_the_other_side()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog) { DropsLoot = true };
            var (x, y, z) = FlatSpot(w.Ground);
            w.Add(new Deployable(0, DeployableKind.StoneWall, x, y, z, 0f, 1));
            var box = w.Add(new Deployable(0, DeployableKind.StorageBox, x, w.Ground.Height(x, z + 0.9f), z + 0.9f, 0f, 1));
            Explosives.Splash(w, x, y + 0.6f, z - 0.3f, new List<BuildOp>());
            Assert.AreEqual(Deployables.MaxHealth(DeployableKind.StorageBox), w.DeployableHealth(box.Id));
        }

        [Test]
        public void The_hammer_mends_a_wall_after_a_while_and_it_is_not_picked_up_hurt()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog);
            var (x, y, z) = FlatSpot(w.Ground);
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.Wood, 500);
            Assert.AreEqual(ActionResult.Ok, Deploy(w, s, 1, DeployableKind.WoodWall, x, z, 0f, out var done));
            int id = done.Deployed.Id;
            Assert.AreEqual(ActionResult.NotDamaged, PlayerActions.Build(w, s, 1, BuildOp.RepairDeployable(id), x, y, z - 2f, out _));
            w.SetDeployableHealth(id, 125f);
            Assert.AreEqual(ActionResult.Hurt, PlayerActions.Build(w, s, 1, BuildOp.Pickup(id), x, y, z - 2f, out _));
            Assert.AreEqual(ActionResult.RecentlyDamaged, PlayerActions.Build(w, s, 1, BuildOp.RepairDeployable(id), x, y, z - 2f, out _));
            w.Tick(Repairs.Delay + 1f);
            int wood = s.Inventory.Count(ItemIds.Wood);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 1, BuildOp.RepairDeployable(id), x, y, z - 2f, out var mended));
            Assert.AreEqual(BuildOpKind.DeployableHealth, mended.Kind);
            Assert.AreEqual(250f, mended.HealthLeft);
            Assert.AreEqual(250f, w.DeployableHealth(id));
            Assert.AreEqual(75, wood - s.Inventory.Count(ItemIds.Wood), "half of the half it lost");
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 1, BuildOp.Pickup(id), x, y, z - 2f, out _), "whole again, it comes back");
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.HighWoodWall));
            Assert.AreEqual(0, w.WallCount);
        }

        [Test]
        public void Walls_and_ladders_are_crafted_and_travel_on_the_wire()
        {
            var recipes = Crafting.RecipeBook.CreateDefault(Catalog);
            Assert.IsTrue(recipes.TryGet("high_wood_wall", out _));
            Assert.IsTrue(recipes.TryGet("high_stone_wall", out _));
            Assert.IsTrue(recipes.TryGet("ladder", out _));
            foreach (var kind in new[] { DeployableKind.WoodWall, DeployableKind.StoneWall, DeployableKind.Ladder })
            {
                Assert.IsTrue(Deployables.TryKindOf(Deployables.ItemOf(kind), out var back));
                Assert.AreEqual(kind, back);
                Assert.IsTrue(Catalog.TryGet(Deployables.ItemOf(kind), out _));
            }
            foreach (var op in new[] { BuildOp.RepairDeployable(7), BuildOp.Deploy(new Deployable(3, DeployableKind.StoneWall, 1f, 2f, 3f, 90f, 4)) })
            {
                var wr = new NetWriter();
                op.Write(wr);
                var read = BuildOp.Read(new NetReader(wr.ToArray()));
                Assert.AreEqual(op.ToString(), read.ToString());
            }
        }
    }
}
