using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Wolves in packs: they keep together, call each other in, come from different sides and run off badly hurt.</summary>
    public class WolfPackTests
    {
        private const int Seed = 20261113;

        private static (IslandGround ground, float x, float z) Inland()
        {
            var ground = new IslandGround(Seed);
            for (float r = 20f; r < 150f; r += 7f)
            for (float a = 0f; a < 6.28f; a += 0.3f)
            {
                float x = r * (float)System.Math.Cos(a), z = r * (float)System.Math.Sin(a);
                bool ok = true;
                for (float dx = -30f; dx <= 30f && ok; dx += 5f)
                for (float dz = -30f; dz <= 30f && ok; dz += 5f)
                    ok = ground.Height(x + dx, z + dz) > IslandGround.BeachHeight + 1f && !ground.PondAt(x + dx, z + dz, out _);
                if (ok) return (ground, x, z);
            }
            Assert.Fail("no open ground on the test island");
            return default;
        }

        private static void Run(Fauna fauna, float seconds, IReadOnlyList<Fauna.Prey> prey, List<Fauna.Bite> bites = null)
        {
            for (float t = 0f; t < seconds; t += 0.05f) fauna.Tick(0.05f, prey, bites);
        }

        private static float Distance(Animal a, float x, float z)
        {
            float dx = a.X - x, dz = a.Z - z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        [Test]
        public void A_fresh_island_has_its_wolves_in_packs()
        {
            var fauna = new Fauna(new IslandGround(Seed), Seed);
            fauna.Populate();
            var wolves = fauna.All.Where(a => a.Kind == AnimalKind.Wolf).ToList();
            Assert.AreEqual(Fauna.Wolves, wolves.Count);
            var packs = wolves.GroupBy(w => w.Pack).ToList();
            Assert.AreEqual(Fauna.Wolves / Animals.PackSize, packs.Count);
            foreach (var pack in packs)
            {
                Assert.AreNotEqual(0, pack.Key);
                Assert.AreEqual(Animals.PackSize, pack.Count());
                var lead = pack.First();
                Assert.IsTrue(pack.All(w => Distance(w, lead.X, lead.Z) < 6f), "together");
                Assert.AreEqual(Animals.PackSize, pack.Select(w => System.Math.Sign(w.Flank)).Distinct().Count(), "each from its own side");
            }
        }

        [Test]
        public void A_pack_keeps_together_as_it_roams()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var pack = fauna.AddPack(x, z, 3);
            Run(fauna, 120f, System.Array.Empty<Fauna.Prey>());
            var lead = pack[0];
            foreach (var w in pack.Skip(1)) Assert.Less(Distance(w, lead.X, lead.Z), 20f, "stayed with the leader");
        }

        [Test]
        public void One_wolf_that_sees_someone_brings_the_pack()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var pack = fauna.AddPack(x, z, 3);
            // Only the first is near enough to see them; the others hear its call.
            pack[1].X = x - 12f; pack[1].Z = z;
            pack[2].X = x; pack[2].Z = z - 12f;
            var prey = new[] { new Fauna.Prey(4, x + 15f, ground.Height(x + 15f, z), z) };
            fauna.Tick(0.05f, prey, null);
            Assert.AreEqual(AnimalState.Chase, pack[0].State);
            Assert.Greater(pack[0].Calls, 0, "it called");
            Assert.IsTrue(pack.All(w => w.State == AnimalState.Chase && w.Quarry == 4), "all come");
        }

        [Test]
        public void A_pack_comes_in_from_different_sides_and_all_bite()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var pack = fauna.AddPack(x, z, 3);
            foreach (var w in pack) { w.X = x; w.Z = z; }
            var prey = new[] { new Fauna.Prey(4, x + 16f, ground.Height(x + 16f, z), z) };
            var bites = new List<Fauna.Bite>();
            float widest = 0f;
            for (float t = 0f; t < 10f; t += 0.05f)
            {
                fauna.Tick(0.05f, prey, bites);
                // Bearings of the wolves from the prey, as they close in.
                var bearings = pack.Where(w => Distance(w, prey[0].X, prey[0].Z) < 7f)
                    .Select(w => System.Math.Atan2(w.X - prey[0].X, w.Z - prey[0].Z) * 180.0 / System.Math.PI).ToList();
                for (int i = 0; i < bearings.Count; i++)
                for (int j = i + 1; j < bearings.Count; j++)
                    widest = System.Math.Max(widest, (float)System.Math.Abs(((bearings[i] - bearings[j]) % 360 + 540) % 360 - 180));
            }
            Assert.Greater(widest, 60f, "not all down one line");
            Assert.AreEqual(3, bites.Select(b => b.Animal).Distinct().Count(), "every wolf got its bite in");
        }

        [Test]
        public void A_badly_hurt_wolf_runs_off()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var wolf = fauna.AddPack(x, z, 1)[0];
            Assert.IsFalse(fauna.Hit(wolf.Id, Animals.MaxHealth(AnimalKind.Wolf) * 0.8f, 4, x + 3f, z));
            Assert.AreEqual(AnimalState.Flee, wolf.State);
            Run(fauna, 3f, new[] { new Fauna.Prey(4, x + 3f, ground.Height(x + 3f, z), z) });
            Assert.Greater(Distance(wolf, x + 3f, z), 10f, "got away");
        }

        [Test]
        public void A_wolf_that_dies_is_replaced_in_its_pack()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var pack = fauna.AddPack(x, z, 3);
            Assert.IsTrue(fauna.Hit(pack[2].Id, 500f, 4, x, z));
            Run(fauna, Animals.CarcassSeconds + Animals.RespawnSeconds + 1f, System.Array.Empty<Fauna.Prey>());
            var wolves = fauna.All.Where(a => a.Kind == AnimalKind.Wolf && !a.IsDead).ToList();
            Assert.AreEqual(3, wolves.Count);
            Assert.IsTrue(wolves.All(w => w.Pack == pack[0].Pack), "back with its pack");
        }
    }
}
