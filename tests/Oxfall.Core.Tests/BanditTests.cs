using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>The armed bandits who keep the ruins: where they stand, whom they shoot, what they drop.</summary>
    public class BanditTests
    {
        private const int Seed = 20261113;

        private static void Run(Fauna fauna, float seconds, IReadOnlyList<Fauna.Prey> prey, List<Fauna.Bite> bites = null)
        {
            for (float t = 0f; t < seconds; t += 0.05f) fauna.Tick(0.05f, prey, bites);
        }

        private static (Fauna fauna, Animal bandit) Guarded()
        {
            var ground = new IslandGround(Seed);
            var fauna = new Fauna(ground, Seed);
            fauna.Guard(Fauna.Posts(ground));
            return (fauna, fauna.All.First(a => a.Kind == AnimalKind.Bandit));
        }

        /// <summary>Somewhere this far from a bandit, on open ground, that he can see from his post.</summary>
        private static Fauna.Prey InSight(IslandGround ground, Animal bandit, float range, int key = 7)
        {
            for (int i = 0; i < 72; i++)
            {
                double angle = i * 5.0 * System.Math.PI / 180.0;
                float x = bandit.X + (float)System.Math.Sin(angle) * range, z = bandit.Z + (float)System.Math.Cos(angle) * range;
                float y = Monuments.SurfaceHeight(ground, x, z);
                if (Monuments.Solid(ground, x, z, 0.5f) || System.Math.Abs(y - bandit.Y) > 2f) continue;
                if (Monuments.LineBlocked(ground, bandit.X, bandit.Y + 1.55f, bandit.Z, x, y + 1.2f, z)) continue;
                bool hill = false;
                for (int s = 1; s < 8 && !hill; s++)
                {
                    float t = s / 8f;
                    hill = ground.Height(bandit.X + (x - bandit.X) * t, bandit.Z + (z - bandit.Z) * t) > bandit.Y + 1.4f + (y - bandit.Y) * t;
                }
                if (!hill) return new Fauna.Prey(key, x, y, z);
            }
            Assert.Fail("no open spot in sight of the bandit");
            return default;
        }

        [Test]
        public void Every_ruin_is_kept_by_bandits_standing_on_its_pad()
        {
            var ground = new IslandGround(Seed);
            var fauna = new Fauna(ground, Seed);
            fauna.Guard(Fauna.Posts(ground));
            var monuments = Monuments.Find(ground);
            var bandits = fauna.All.Where(a => a.Kind == AnimalKind.Bandit).ToList();
            Assert.AreEqual(monuments.Sum(m => Monuments.GuardPosts(m).Count()), bandits.Count);
            Assert.GreaterOrEqual(bandits.Count, monuments.Count);
            foreach (var b in bandits)
            {
                Assert.IsTrue(monuments.Any(m => (m.X - b.X) * (m.X - b.X) + (m.Z - b.Z) * (m.Z - b.Z) < Monuments.Radius * Monuments.Radius), "in a ruin");
                Assert.IsFalse(Monuments.Solid(ground, b.X, b.Z, 0.3f), "not inside a wall or hut");
                Assert.AreEqual(Monuments.SurfaceHeight(ground, b.X, b.Z), b.Y, 1e-3f, "on the concrete");
                Assert.AreEqual(Animals.MaxHealth(AnimalKind.Bandit), b.Health);
            }
            // Nothing new for the animals: they do not come with the herds.
            var herds = new Fauna(ground, Seed);
            herds.Populate();
            Assert.IsFalse(herds.All.Any(a => a.Kind == AnimalKind.Bandit));
        }

        [Test]
        public void A_bandit_shoots_a_player_in_sight_from_where_he_stands()
        {
            var (fauna, bandit) = Guarded();
            var ground = new IslandGround(Seed);
            float postX = bandit.X, postZ = bandit.Z;
            var prey = new[] { InSight(ground, bandit, 13f) };
            var bites = new List<Fauna.Bite>();
            Run(fauna, 8f, prey, bites);

            Assert.AreEqual(AnimalState.Chase, bandit.State);
            var shots = bites.Where(b => b.Animal == bandit.Id).ToList();
            Assert.GreaterOrEqual(shots.Count, 3, "a shot every second or so");
            Assert.LessOrEqual(shots.Count, (int)(8f / Bandits.FireSeconds) + 1);
            Assert.AreEqual(shots.Count, bandit.Shots, "every shot counted, hit or miss");
            Assert.IsTrue(shots.All(b => b.Target == 7 && (b.Damage == Bandits.Damage || b.Damage == 0f)));
            Assert.IsTrue(shots.Any(b => b.Damage > 0f), "close: most hit");
            float moved = (float)System.Math.Sqrt((bandit.X - postX) * (bandit.X - postX) + (bandit.Z - postZ) * (bandit.Z - postZ));
            Assert.Less(moved, Bandits.StrafeMetres + 1f, "kept his ground, a step or two aside at most");
        }

        [Test]
        public void Nobody_far_off_or_out_of_sight_is_shot()
        {
            var (fauna, bandit) = Guarded();
            var ground = new IslandGround(Seed);
            var bites = new List<Fauna.Bite>();
            var far = InSight(ground, bandit, 13f);
            // Far away: not noticed.
            float dx = far.X - bandit.X, dz = far.Z - bandit.Z;
            var away = new Fauna.Prey(7, bandit.X + dx * 6f, ground.Height(bandit.X + dx * 6f, bandit.Z + dz * 6f), bandit.Z + dz * 6f);
            Run(fauna, 6f, new[] { away }, bites);
            Assert.IsEmpty(bites);

            // Close, but a building's wall in the way.
            var (walled, other) = Guarded();
            walled.WallBetween = (x0, y0, z0, x1, y1, z1) => true;
            Run(walled, 6f, new[] { InSight(ground, other, 13f) }, bites);
            Assert.IsEmpty(bites);
            Assert.AreNotEqual(AnimalState.Chase, other.State);
        }

        [Test]
        public void A_wall_or_foundation_blocks_a_line_of_sight_and_open_air_does_not()
        {
            var grid = new BuildingGrid();
            Assert.AreEqual(PlaceResult.Ok, grid.PlaceFoundation(new Cell(0, 0), 1f));
            Assert.AreEqual(PlaceResult.Ok, grid.PlaceFoundation(new Cell(1, 0), 1f));
            Assert.AreEqual(PlaceResult.Ok, grid.PlaceEdge(new Edge(1, 0, EdgeAxis.AlongZ), PieceType.Wall));
            var walls = Fauna.Walls(grid);
            Assert.IsTrue(walls(1.5f, 2.5f, 1.5f, 4.5f, 2.5f, 1.5f), "through the wall");
            Assert.IsTrue(walls(1.5f, 2.5f, 1.5f, 4.5f, 2.5f, 4.4f), "slanting through it");
            Assert.IsFalse(walls(1.5f, 5.5f, 1.5f, 4.5f, 5.5f, 1.5f), "over it");
            Assert.IsFalse(walls(1.5f, 2.5f, 4.5f, 4.5f, 2.5f, 4.5f), "beside the building");
            Assert.IsTrue(walls(-4f, 0.5f, 1.5f, 1.5f, 0.5f, 1.5f), "into the foundation");
            Assert.IsFalse(Fauna.Walls(new BuildingGrid())(0f, 1f, 0f, 30f, 1f, 30f));
        }

        [Test]
        public void A_bandit_shot_at_from_afar_turns_on_whoever_did_it()
        {
            var (fauna, bandit) = Guarded();
            var ground = new IslandGround(Seed);
            float postX = bandit.X, postZ = bandit.Z;
            var shooter = InSight(ground, bandit, 30f, 9);
            Assert.IsFalse(fauna.Hit(bandit.Id, 20f, 9, shooter.X, shooter.Z));
            Assert.AreEqual(AnimalState.Chase, bandit.State, "beyond his sight, but he knows where it came from");
            var bites = new List<Fauna.Bite>();
            Run(fauna, 6f, new[] { shooter }, bites);
            Assert.IsTrue(bites.Any(b => b.Target == 9), "closed in and fired back");
            float fromPost = (float)System.Math.Sqrt((bandit.X - postX) * (bandit.X - postX) + (bandit.Z - postZ) * (bandit.Z - postZ));
            Assert.LessOrEqual(fromPost, Bandits.Leash + 0.5f);
        }

        [Test]
        public void A_fallen_bandit_drops_his_things_and_another_holds_the_post_later()
        {
            var world = new ServerWorld(Seed);
            var bandit = world.Fauna.All.First(a => a.Kind == AnimalKind.Bandit);
            float postX = bandit.X, postZ = bandit.Z;
            int bags = world.Settlement.Deployables.Count(d => d.Kind == DeployableKind.Bag);
            world.Announcements.Clear();

            Assert.IsTrue(world.Fauna.Hit(bandit.Id, 500f, 3, bandit.X + 4f, bandit.Z));
            Assert.AreEqual(0, bandit.Carcass, "nothing to cut up");
            var bag = world.Settlement.Deployables.Single(d => d.Kind == DeployableKind.Bag && d.Owner == 0);
            Assert.AreEqual(bags + 1, world.Settlement.Deployables.Count(d => d.Kind == DeployableKind.Bag));
            Assert.Less(System.Math.Abs(bag.X - bandit.X) + System.Math.Abs(bag.Z - bandit.Z), 0.01f);
            var held = world.Settlement.Container(bag.Id);
            Assert.GreaterOrEqual(held.Count(ItemIds.PistolBullet), 4);
            Assert.GreaterOrEqual(held.Count(ItemIds.Scrap), 6);
            Assert.GreaterOrEqual(held.Count(ItemIds.Cloth), 5);
            Assert.AreEqual(1, world.Announcements.Count, "everyone sees the bag");

            // His body goes; while someone is about, nobody new turns up; then one does.
            var none = new List<Fauna.Prey>();
            world.Fauna.Tick(Bandits.BodySeconds + 0.1f, none, null);
            Assert.IsFalse(world.Fauna.TryGet(bandit.Id, out _));
            var watcher = new[] { new Fauna.Prey(1, postX + 10f, 0f, postZ) };
            world.Fauna.Tick(Bandits.RespawnSeconds + 1f, watcher, null);
            Assert.IsFalse(world.Fauna.All.Any(a => a.Kind == AnimalKind.Bandit && Near(a, postX, postZ)));
            world.Fauna.Tick(12f, none, null);
            var back = world.Fauna.All.Single(a => a.Kind == AnimalKind.Bandit && Near(a, postX, postZ));
            Assert.IsFalse(back.IsDead);
        }

        private static bool Near(Animal a, float x, float z) => System.Math.Abs(a.X - x) < 0.01f && System.Math.Abs(a.Z - z) < 0.01f;

        [Test]
        public void A_bandits_shots_reach_the_phones()
        {
            var (fauna, bandit) = Guarded();
            var ground = new IslandGround(Seed);
            Run(fauna, 4f, new[] { InSight(ground, bandit, 10f) }, new List<Fauna.Bite>());
            Assert.Greater(bandit.Shots, 0);
            var w = new NetWriter();
            Messages.Animals(w, fauna.All, bandit.X, bandit.Z, 5f);
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Animals, (MessageType)r.Byte());
            var snaps = new List<AnimalSnap>();
            Messages.ReadAnimals(r, snaps);
            var snap = snaps.Single(s => s.Id == bandit.Id);
            Assert.AreEqual(AnimalKind.Bandit, snap.Kind);
            Assert.AreEqual(bandit.Shots & 0xFF, snap.Shots);
            Assert.AreEqual(AnimalState.Chase, snap.State);
            Assert.AreEqual(bandit.Calls & 0xFF, snap.Calls);
            Assert.Greater(snap.Calls, 0, "he called it in");
            Assert.AreEqual(bandit.Act, snap.Act);
        }

        /// <summary>The outpost (two posts) with one player in sight of its first bandit.</summary>
        private static (Fauna fauna, Animal first, Animal second, IslandGround ground) Outpost()
        {
            var ground = new IslandGround(Seed);
            var fauna = new Fauna(ground, Seed);
            var outpost = Monuments.Find(ground).First(m => m.Kind == MonumentKind.Outpost);
            fauna.Guard(Monuments.GuardPosts(outpost));
            var pair = fauna.All.Where(a => a.Kind == AnimalKind.Bandit).ToList();
            Assert.AreEqual(2, pair.Count);
            return (fauna, pair[0], pair[1], ground);
        }

        [Test]
        public void A_bandit_who_sees_someone_radios_the_others_and_they_come()
        {
            var (fauna, first, second, ground) = Outpost();
            var prey = new[] { InSight(ground, first, 14f) };
            fauna.Tick(0.05f, prey, null);
            Assert.AreEqual(AnimalState.Chase, first.State);
            Assert.AreEqual(1, first.Calls, "one call on the radio");
            Assert.AreEqual(AnimalState.Chase, second.State, "the other one heard it");
            Assert.AreEqual(7, second.Quarry);
            // Out of sight of the second, he still comes looking for longer than he would on his own.
            var bites = new List<Fauna.Bite>();
            Run(fauna, Bandits.LoseSeconds + 1f, prey, bites);
            Assert.AreEqual(AnimalState.Chase, second.State);
            Assert.IsTrue(bites.Any(b => b.Animal == first.Id));
        }

        [Test]
        public void A_bandit_runs_dry_after_six_rounds_and_reloads()
        {
            var (fauna, bandit) = Guarded();
            var ground = new IslandGround(Seed);
            var prey = new[] { InSight(ground, bandit, 10f) };
            var bites = new List<Fauna.Bite>();
            bool sawReload = false;
            for (float t = 0f; t < 14f; t += 0.05f)
            {
                fauna.Tick(0.05f, prey, bites);
                if (bandit.Act != BanditAct.Reloading) continue;
                sawReload = true;
                Assert.AreEqual(0, bandit.Rounds);
                Assert.AreEqual(Bandits.Magazine, bites.Count(b => b.Animal == bandit.Id), "a full cylinder first");
                int before = bites.Count(b => b.Animal == bandit.Id);
                Run(fauna, Bandits.ReloadSeconds - 0.2f, prey, bites);
                Assert.AreEqual(before, bites.Count(b => b.Animal == bandit.Id), "no shots while reloading");
                Run(fauna, 0.4f, prey, bites);
                Assert.AreNotEqual(BanditAct.Reloading, bandit.Act);
                Assert.GreaterOrEqual(bandit.Rounds, Bandits.Magazine - 1, "full again (and maybe firing at once)");
                break;
            }
            Assert.IsTrue(sawReload);
        }

        [Test]
        public void A_bandit_steps_aside_between_shots_still_facing_his_target()
        {
            var (fauna, bandit) = Guarded();
            var ground = new IslandGround(Seed);
            var prey = new[] { InSight(ground, bandit, 12f) };
            float x0 = bandit.X, z0 = bandit.Z, most = 0f;
            bool slid = false;
            for (float t = 0f; t < 6f; t += 0.05f)
            {
                fauna.Tick(0.05f, prey, null);
                most = System.Math.Max(most, (float)System.Math.Sqrt((bandit.X - x0) * (bandit.X - x0) + (bandit.Z - z0) * (bandit.Z - z0)));
                if (bandit.Strafing)
                {
                    slid = true;
                    Assert.AreEqual(0f, bandit.Drift, "the phones are not told to walk him on forward");
                }
            }
            Assert.IsTrue(slid);
            Assert.Greater(most, 0.8f, "moved off his spot");
            Assert.Less(most, Bandits.StrafeMetres * 2f + 0.5f);
            float want = (float)(System.Math.Atan2(prey[0].X - bandit.X, prey[0].Z - bandit.Z) * 180.0 / System.Math.PI);
            float off = System.Math.Abs(((want - bandit.Yaw) % 360f + 540f) % 360f - 180f);
            Assert.Less(off, 20f, "still aiming at them");
        }

        [Test]
        public void A_badly_hurt_bandit_runs_for_cover_and_bandages_himself_once()
        {
            var (fauna, first, second, ground) = Outpost();
            var shooter = InSight(ground, first, 14f, 9);
            Assert.IsFalse(fauna.Hit(first.Id, 70f, 9, shooter.X, shooter.Z));
            Assert.AreEqual(BanditAct.Cover, first.Act);
            Assert.AreEqual(AnimalState.Flee, first.State);
            float hurt = first.Health;
            // Away from the shooter: the bandage takes a few seconds, then he has more health.
            float x0 = first.X, z0 = first.Z;
            for (float t = 0f; t < Bandits.CoverSeconds + 0.2f && first.Act == BanditAct.Cover; t += 0.05f)
                fauna.Tick(0.05f, new[] { shooter }, null);
            Assert.AreEqual(BanditAct.Healing, first.Act);
            Assert.Greater((first.X - x0) * (first.X - x0) + (first.Z - z0) * (first.Z - z0), 4f, "ran some way");
            Assert.AreEqual(hurt, first.Health, 0.01f);
            Run(fauna, Bandits.HealSeconds + 0.2f, new[] { shooter }, null);
            Assert.AreNotEqual(BanditAct.Healing, first.Act);
            Assert.AreEqual(hurt + Bandits.HealAmount, first.Health, 0.01f);
            Assert.IsTrue(first.Healed);
            // Hurt again: no second bandage, he fights on.
            Assert.IsFalse(fauna.Hit(first.Id, 40f, 9, shooter.X, shooter.Z));
            Assert.AreEqual(AnimalState.Chase, first.State);
            Assert.AreNotEqual(BanditAct.Cover, first.Act);
        }

        [Test]
        public void A_bandit_who_loses_someone_looks_where_they_were_then_goes_back_to_his_post()
        {
            var (fauna, bandit) = Guarded();
            var ground = new IslandGround(Seed);
            float postX = bandit.X, postZ = bandit.Z;
            var seen = InSight(ground, bandit, 15f);
            fauna.Tick(0.05f, new[] { seen }, null);
            Assert.AreEqual(AnimalState.Chase, bandit.State);
            // Gone round a wall.
            fauna.WallBetween = (x0, y0, z0, x1, y1, z1) => true;
            Run(fauna, Bandits.LoseSeconds + 0.5f, new[] { seen }, null);
            Assert.AreEqual(BanditAct.Searching, bandit.Act);
            Run(fauna, 20f, System.Array.Empty<Fauna.Prey>(), null);
            Assert.AreNotEqual(AnimalState.Chase, bandit.State);
            Run(fauna, 40f, System.Array.Empty<Fauna.Prey>(), null);
            Assert.AreEqual(BanditAct.None, bandit.Act);
            float fromPost = (float)System.Math.Sqrt((bandit.X - postX) * (bandit.X - postX) + (bandit.Z - postZ) * (bandit.Z - postZ));
            Assert.Less(fromPost, 7f, "back about his post");
            Assert.AreEqual(Bandits.Magazine, bandit.Rounds, "and reloaded");
        }
    }
}
