using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Supply drops: a plane now and then, a crate down under a parachute, good things for whoever gets there.</summary>
    public class AirdropTests
    {
        private const int Seed = 20261113;

        [Test]
        public void A_plane_crosses_over_an_open_spot_and_the_crate_comes_down()
        {
            var ground = new IslandGround(Seed);
            var drops = new Airdrops(ground, Seed);
            var started = new List<Airdrop>();
            var landed = new List<Airdrop>();
            drops.Tick(Airdrops.FirstAfter - 1f, started, landed);
            Assert.IsEmpty(started, "not straight away on a fresh island");
            drops.Tick(Airdrops.FirstAfter, started, landed);
            var a = started.Single();
            Assert.IsTrue(Airdrops.Suitable(ground, a.DropX, a.DropZ));
            Assert.Greater(drops.Next, Airdrops.FirstAfter + Airdrops.Every - Airdrops.Jitter - 1f);

            // Over the spot when it lets go, and right across the island.
            a.PlaneAt(a.DropTime, out float x, out float z);
            Assert.AreEqual(a.DropX, x, 0.01f);
            Assert.AreEqual(a.DropZ, z, 0.01f);
            a.PlaneAt(a.Start, out x, out z);
            Assert.Greater(System.Math.Sqrt(x * x + z * z), IslandGround.HalfSize, "comes in from beyond the shore");
            Assert.AreEqual(Airdrops.Height, a.CrateHeight(a.DropTime), 1e-3f);
            Assert.AreEqual(a.GroundY, a.CrateHeight(a.LandTime + 5f), 1e-3f);
            Assert.Greater(a.LandTime - a.DropTime, 15f, "a slow way down: time to run for it");

            drops.Tick(a.LandTime - 0.1f, started, landed);
            Assert.IsEmpty(landed);
            drops.Tick(a.LandTime, started, landed);
            Assert.AreEqual(a.Id, landed.Single().Id);
            Assert.IsEmpty(drops.Flying);
        }

        [Test]
        public void Never_onto_a_building_or_into_the_sea()
        {
            var ground = new IslandGround(Seed);
            var drops = new Airdrops(ground, Seed) { Blocked = (x, z) => x > 0f };
            for (int i = 0; i < 40; i++)
            {
                Assert.IsTrue(drops.TryPickSpot(out float x, out float z));
                Assert.LessOrEqual(x, 0f);
                Assert.Greater(ground.Height(x, z), IslandGround.BeachHeight);
                Assert.IsFalse(Monuments.NoBuilding(ground, x, z));
            }
        }

        [Test]
        public void On_the_server_the_crate_lands_full_and_only_empties()
        {
            var world = new ServerWorld(Seed);
            Assert.AreEqual(ActionResult.Invalid, world.Act(1, ActKind.DevAirdrop, 0, 0, null), "nobody online");
            float t = 0f;
            while (world.AirdropsStarted.Count == 0 && t < Airdrops.FirstAfter + 5f)
            {
                world.Tick(1f);
                t += 1f;
            }
            var a = world.AirdropsStarted.Single();
            world.AirdropsStarted.Clear();
            world.Announcements.Clear();
            while (world.Time < a.LandTime + 1f) world.Tick(1f);

            var crate = world.Settlement.Deployables.Single(d => d.Kind == DeployableKind.SupplyDrop);
            Assert.AreEqual(a.DropX, crate.X, 1e-3f);
            Assert.AreEqual(a.GroundY, crate.Y, 1e-3f);
            Assert.AreEqual(0, crate.Owner);
            Assert.IsTrue(world.Announcements.Any(op => op.Kind == BuildOpKind.Deploy), "everyone sees it land");
            var box = world.Settlement.Container(crate.Id);
            Assert.GreaterOrEqual(box.Count(ItemIds.PistolBullet), 20);
            Assert.GreaterOrEqual(box.Count(ItemIds.Gunpowder), 60);
            Assert.AreEqual(9, Enumerable.Range(0, box.SlotCount).Count(i => !box[i].IsEmpty), "six sure things and three finds");
            Assert.IsNull(Deployables.ItemOf(DeployableKind.SupplyDrop));
            Assert.AreEqual(0f, Deployables.MaxHealth(DeployableKind.SupplyDrop));

            // Opened, emptied: gone; nothing goes in.
            var s = new Survivor(ItemCatalog.CreateDefault());
            s.Inventory.Clear();
            s.Inventory.Add(ItemIds.Wood, 10);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(world.Settlement, s, 1, crate.Id, crate.X + 1f, crate.Y, crate.Z));
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Put(world.Settlement, s, 0, -1));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.TakeAll(world.Settlement, s));
            Assert.IsFalse(world.Settlement.TryGet(crate.Id, out _));
            Assert.GreaterOrEqual(s.Inventory.Count(ItemIds.PistolBullet), 20);
        }

        [Test]
        public void A_supply_crate_left_alone_rots()
        {
            var world = new ServerWorld(Seed);
            var crate = world.Settlement.LandSupplyDrop(10f, world.Ground.Height(10f, 10f), 10f, 0f, new[] { new Gathering.Yield(ItemIds.Scrap, 5) });
            world.Tick(Deployables.SupplyDropSeconds - 5f);
            Assert.IsTrue(world.Settlement.TryGet(crate.Id, out _));
            world.Tick(10f);
            Assert.IsFalse(world.Settlement.TryGet(crate.Id, out _));
        }

        [Test]
        public void Supply_planes_reach_the_phones()
        {
            var a = new Airdrop(7, -500f, 3f, 500f, 3f, 12f, 3f, 9.5f, 100f);
            var w = new NetWriter();
            Messages.Airdrop(w, a, 30f);
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Airdrop, (MessageType)r.Byte());
            var b = Messages.ReadAirdrop(r, 50f);
            Assert.AreEqual((7, -500f, 12f, 9.5f), (b.Id, b.FromX, b.DropX, b.GroundY));
            Assert.AreEqual(20f, b.Start, 1e-3f, "on this machine's clock: thirty seconds ago");
        }
    }
}
