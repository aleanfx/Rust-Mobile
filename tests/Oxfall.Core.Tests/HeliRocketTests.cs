using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>The patrol helicopter's rockets break what is built: wood goes fast, stone holds, a roof or wall shields what is behind it.</summary>
    public class HeliRocketTests
    {
        private const int Seed = 20261117;
        private const float S = BuildingGrid.CellSize, H = BuildingGrid.WallHeight;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        /// <summary>A 1×1 hut on a foundation 1 m up: four walls of the given tier and a roof.</summary>
        private static (Settlement, BuildingGrid, Cell) Hut(BuildingTier tier)
        {
            var w = new Settlement(new IslandGround(Seed), Catalog) { DropsLoot = true };
            var grid = w.Grid;
            var cell = new Cell(0, 0);
            grid.PlaceFoundation(cell, 1f);
            foreach (var e in new[] { cell.South, cell.North, cell.West, cell.East })
            {
                grid.PlaceEdge(e, PieceType.Wall);
                if (tier != BuildingTier.Twig) grid.Upgrade(PieceRef.OnEdge(e, PieceType.Wall), tier);
            }
            grid.PlaceRoof(cell);
            if (tier != BuildingTier.Twig) grid.Upgrade(PieceRef.Roof(cell), tier);
            if (tier != BuildingTier.Twig) grid.Upgrade(PieceRef.Foundation(cell), tier);
            return (w, grid, cell);
        }

        [Test]
        public void A_burst_against_a_wall_bites_it_by_its_tier_wood_goes_fast_stone_holds_metal_shrugs()
        {
            foreach (var tier in new[] { BuildingTier.Wood, BuildingTier.Stone, BuildingTier.Metal })
            {
                var (w, grid, cell) = Hut(tier);
                var wall = PieceRef.OnEdge(cell.South, PieceType.Wall);
                var changes = new List<BuildOp>();
                // Right against the middle of the south wall, outside.
                Assert.Greater(Explosives.RocketBlast(w, 1.5f, 2.5f, -0.05f, changes), 0);
                float lost = BuildingTiers.MaxHealth(tier) - grid.Health(wall);
                Assert.AreEqual(Explosives.RocketDamage * Explosives.RocketShare(tier), lost, 0.01f, tier.ToString());
                Assert.IsTrue(changes.Any(o => o.Kind == BuildOpKind.Health), "told as health changes");
                // The far wall is inside, behind the near one: untouched.
                Assert.AreEqual(BuildingTiers.MaxHealth(tier), grid.Health(PieceRef.OnEdge(cell.North, PieceType.Wall)), "the far wall is shielded");
            }
            // How many rockets bring down a wall of each tier.
            Assert.AreEqual(3, Count(BuildingTier.Wood), "a wooden wall falls to one volley");
            Assert.That(Count(BuildingTier.Stone), Is.InRange(12, 13), "stone takes three volleys");
            Assert.Greater(Count(BuildingTier.Metal), 40, "metal holds out the helicopter");
        }

        private static int Count(BuildingTier tier)
        {
            var (w, grid, cell) = Hut(tier);
            int n = 0;
            while (grid.HasEdgePiece(cell.South) && n < 100)
            {
                Explosives.RocketBlast(w, 1.5f, 2.5f, -0.05f, null);
                n++;
            }
            return n;
        }

        [Test]
        public void Twig_goes_at_once_and_farther_off_a_burst_bites_less_until_it_does_not_reach()
        {
            var (w, grid, cell) = Hut(BuildingTier.Twig);
            Explosives.RocketBlast(w, 1.5f, 2.5f, -0.5f, null);
            Assert.IsFalse(grid.HasEdgePiece(cell.South), "twig falls to one");
            Assert.Greater(Explosives.DamageToPieceFromRocket(0.5f), Explosives.DamageToPieceFromRocket(2f));
            Assert.AreEqual(0f, Explosives.DamageToPieceFromRocket(Explosives.RocketReach));

            var (w2, grid2, cell2) = Hut(BuildingTier.Wood);
            Explosives.RocketBlast(w2, 1.5f, 2.5f, -Explosives.RocketReach - 0.1f, null);
            Assert.IsFalse(grid2.Damaged.Any(), "too far to bite");
        }

        [Test]
        public void A_roof_takes_a_burst_from_above_and_shields_the_walls_and_box_under_it()
        {
            var (w, grid, cell) = Hut(BuildingTier.Wood);
            var box = w.Add(new Deployable(0, DeployableKind.StorageBox, 1.5f, 1f, 1.5f, 0f));
            float roofY = 1f + H + BuildingGrid.FloorThickness;
            Explosives.RocketBlast(w, 1.5f, roofY + 0.1f, 1.5f, null);
            Assert.Less(grid.Health(PieceRef.Roof(cell)), BuildingTiers.MaxHealth(BuildingTier.Wood), "the roof took it");
            Assert.AreEqual(BuildingTiers.MaxHealth(BuildingTier.Wood), grid.Health(PieceRef.OnEdge(cell.South, PieceType.Wall)), "walls under the roof are shielded");
            Assert.AreEqual(Deployables.MaxHealth(DeployableKind.StorageBox), w.DeployableHealth(box.Id), "the box inside too");
        }

        [Test]
        public void Doors_take_it_too_metal_better_and_what_is_set_down_near_takes_half_a_charges_splash()
        {
            var (w, grid, cell) = Hut(BuildingTier.Stone);
            grid.RemoveEdge(cell.South);
            grid.PlaceEdge(cell.South, PieceType.Doorway);
            grid.Upgrade(PieceRef.OnEdge(cell.South, PieceType.Doorway), BuildingTier.Stone);
            grid.PlaceDoor(cell.South, DoorKind.Metal);
            var box = w.Add(new Deployable(0, DeployableKind.StorageBox, 1.5f, 1f, -1f, 0f));
            Explosives.RocketBlast(w, 1.5f, 2f, -0.05f, null);
            Assert.AreEqual(DoorKinds.MaxHealth(DoorKind.Metal) - Explosives.RocketDamage * Explosives.RocketDoorShare(DoorKind.Metal), grid.DoorHealth(cell.South), 0.5f);
            float boxLost = Deployables.MaxHealth(DeployableKind.StorageBox) - w.DeployableHealth(box.Id);
            Assert.Greater(boxLost, 0f, "the box outside, by the door");
            Assert.LessOrEqual(boxLost, Explosives.DeployableDamage * Explosives.RocketSplashShare + 0.01f);
        }

        [Test]
        public void A_rocket_bursts_on_the_first_wall_or_roof_in_its_way()
        {
            var (w, grid, cell) = Hut(BuildingTier.Wood);
            System.Func<float, float, float, float, float, float, bool> shield = (x0, y0, z0, x1, y1, z1) => Explosives.Shielded(w, x0, y0, z0, x1, y1, z1);
            // From the south, at the middle of the hut: stops at the south wall.
            float x = 1.5f, y = 2f, z = 1.5f;
            Explosives.FirstStop(shield, 1.5f, 2f, -20f, ref x, ref y, ref z);
            Assert.That(z, Is.InRange(-0.1f, 0f), "on the outside of the wall");
            // From high above: on the roof.
            x = 1.5f; y = 2f; z = 1.5f;
            Explosives.FirstStop(shield, 1.6f, 40f, 1.4f, ref x, ref y, ref z);
            Assert.That(y, Is.InRange(1f + H, 1f + H + 0.5f), "on the roof");
            // Nothing in the way: where it was aimed.
            x = 10f; y = 2f; z = 10f;
            Explosives.FirstStop(shield, 10f, 30f, 30f, ref x, ref y, ref z);
            Assert.AreEqual((10f, 2f, 10f), (x, y, z));
        }

        [Test]
        public void Shot_at_from_inside_a_wooden_hut_it_rockets_the_hut_on_the_server()
        {
            var world = new ServerWorld(Seed) { DevMode = true };
            var ana = world.Join(Protocol.Version, "Ana", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var pa, out _, out _);
            // A wooden hut on open ground near where she spawned, with her in it.
            var grid = world.Settlement.Grid;
            Cell cell = default;
            float top = 0f;
            bool found = false;
            var near = BuildingGrid.CellAt(pa.Pose.X, pa.Pose.Z);
            for (int r = 1; r < 30 && !found; r++)
                for (int dz = -r; dz <= r && !found; dz++)
                    for (int dx = -r; dx <= r && !found; dx++)
                    {
                        var c = new Cell(near.X + dx, near.Z + dz);
                        if (PlayerActions.FoundationTop(world.Settlement, c, out top) != ActionResult.Ok) continue;
                        if (Monuments.NoBuilding(world.Ground, (c.X + 0.5f) * S, (c.Z + 0.5f) * S)) continue;
                        cell = c;
                        found = true;
                    }
            Assert.IsTrue(found);
            grid.PlaceFoundation(cell, top);
            foreach (var e in new[] { cell.South, cell.North, cell.West, cell.East })
            {
                grid.PlaceEdge(e, PieceType.Wall);
                grid.Upgrade(PieceRef.OnEdge(e, PieceType.Wall), BuildingTier.Wood);
            }
            grid.PlaceRoof(cell);
            grid.Upgrade(PieceRef.Roof(cell), BuildingTier.Wood);
            float mx = (cell.X + 0.5f) * S, mz = (cell.Z + 0.5f) * S;
            world.Players.Place(pa.Id, new Pose(mx, top, mz, 0f), 0f);
            Assert.AreEqual(ActionResult.Ok, world.Act(pa.Id, ActKind.DevHeli, 1, 0, null));
            var h = world.Helis.All[0];
            world.Announcements.Clear();
            float before = ana.Survivor.Vitals.Health;
            // She shoots it every couple of seconds from in there: it cannot see her, but it knows where the shots come from.
            bool hitWhileWhole = false;
            for (int i = 0; i < 20 * 60; i++)
            {
                if (i % 40 == 0) world.Helis.Hit(h.Id, 1f, false, pa.Id, mx, top, mz, world.Time);
                world.Tick(0.05f);
                // (A round in the same tick as the wall falls is fair: the hole is there.)
                if (grid.Pieces.Count() == 6 && world.HeliShots.Any(s => s.Shot.Kind == HeliShotKind.Bullet && s.Shot.Target == pa.Id)) hitWhileWhole = true;
            }
            Assert.IsTrue(world.HeliShots.Any(s => s.Shot.Kind == HeliShotKind.Rocket), "it fired rockets");
            Assert.IsFalse(hitWhileWhole, "no round reaches her while the hut stands");
            Assert.IsTrue(grid.Damaged.Any() || grid.Pieces.Count() < 6, "the hut took the rockets");
            Assert.IsTrue(world.Announcements.Any(o => o.Kind == BuildOpKind.Health), "every phone is told");
        }
    }
}
