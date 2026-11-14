using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Gathering;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    public class IslandGroundTests
    {
        [Test]
        public void TheSameSeedGivesTheSameIsland()
        {
            var a = new IslandGround(20261113);
            var b = new IslandGround(20261113);
            for (float x = -200f; x <= 200f; x += 37f)
            for (float z = -200f; z <= 200f; z += 41f)
                Assert.AreEqual(a.Height(x, z), b.Height(x, z));
            Assert.AreEqual(a.Ponds.Count, b.Ponds.Count);
        }

        [Test]
        public void ThereIsLandInTheMiddleAndSeaAtTheEdges()
        {
            var g = new IslandGround(20261113);
            Assert.Greater(g.Height(0f, 0f), 1f);
            Assert.Less(g.Height(230f, 230f), IslandGround.WaterLevel);
            Assert.Less(g.Height(-230f, 0f), IslandGround.WaterLevel);
        }

        [TestCase(1)]
        [TestCase(20261113)]
        [TestCase(777)]
        public void EveryIslandHasPondsAndHills(int seed)
        {
            var g = new IslandGround(seed);
            Assert.That(g.Ponds.Count, Is.InRange(1, 3));
            float highest = 0f;
            for (float x = -150f; x <= 150f; x += 5f)
            for (float z = -150f; z <= 150f; z += 5f)
                highest = System.Math.Max(highest, g.Height(x, z));
            Assert.Greater(highest, 8f, "some hills");
            Assert.Less(highest, 45f);
            foreach (var p in g.Ponds)
                Assert.IsTrue(g.PondAt(p.X, p.Z, out _));
        }

        [Test]
        public void NoiseStaysAboutZeroToOne()
        {
            float min = 1f, max = 0f;
            for (int i = 0; i < 20000; i++)
            {
                float n = IslandGround.Noise(i * 0.137f, i * 0.091f + 13f);
                min = System.Math.Min(min, n);
                max = System.Math.Max(max, n);
            }
            Assert.That(min, Is.InRange(-0.05f, 0.3f));
            Assert.That(max, Is.InRange(0.7f, 1.05f));
        }
    }

    public class ResourceLayoutTests
    {
        private static readonly List<NodeSpawn> Layout = ResourceLayout.Generate(new IslandGround(20261113));

        [Test]
        public void TheSameSeedGivesTheSameNodes()
        {
            var again = ResourceLayout.Generate(new IslandGround(20261113));
            CollectionAssert.AreEqual(Layout.Select(s => (s.Id, s.Type, s.X, s.Z)), again.Select(s => (s.Id, s.Type, s.X, s.Z)));
        }

        [Test]
        public void IdsAreUniqueAndCountsAreSensible()
        {
            Assert.AreEqual(Layout.Count, Layout.Select(s => s.Id).Distinct().Count());
            var byType = Layout.GroupBy(s => s.Type).ToDictionary(g => g.Key, g => g.Count());
            TestContext.WriteLine(string.Join(", ", byType.Select(p => p.Key + " " + p.Value)));
            Assert.Greater(byType[ResourceNodeType.Tree], 200);
            Assert.Greater(byType[ResourceNodeType.Stone], 50);
            Assert.Greater(byType[ResourceNodeType.MetalOre], 10);
            Assert.Greater(byType[ResourceNodeType.Hemp], 20);
            Assert.Greater(byType[ResourceNodeType.BerryBush], 10);
        }

        [Test]
        public void NodesStandOnLandAndOutOfPonds()
        {
            var g = new IslandGround(20261113);
            foreach (var s in Layout)
            {
                Assert.GreaterOrEqual(s.Y, 1.3f);
                Assert.AreEqual(0f, g.PondBank(s.X, s.Z));
                // The ruins' crates and barrels stand on their monument's pad, not the ground.
                if (s.Id < Monuments.FirstNodeId) Assert.AreEqual(g.Height(s.X, s.Z), s.Y);
            }
        }

        [Test]
        public void AnIdNamesItsCell()
        {
            foreach (var s in Layout)
            {
                if (s.Id >= Monuments.FirstNodeId) continue;
                int cell = s.Id - 1;
                int gx = cell % ResourceLayout.Cells, gz = cell / ResourceLayout.Cells;
                Assert.That(s.X, Is.InRange(-IslandGround.HalfSize + gx * ResourceLayout.Cell, -IslandGround.HalfSize + (gx + 1) * ResourceLayout.Cell));
                Assert.That(s.Z, Is.InRange(-IslandGround.HalfSize + gz * ResourceLayout.Cell, -IslandGround.HalfSize + (gz + 1) * ResourceLayout.Cell));
            }
        }
    }
}
