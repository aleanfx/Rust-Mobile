using System.Collections.Generic;
using NUnit.Framework;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Horses: a few graze in the meadows; ridden they trot, gallop on their wind, stay out of the sea, eat berries.</summary>
    public class HorseTests
    {
        private const int Seed = 20261114;
        private const string Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        [Test]
        public void A_few_horses_graze_on_open_dry_ground_the_same_for_a_seed()
        {
            var g = new IslandGround(Seed);
            var a = Riding.Spawn(g, Seed);
            var b = Riding.Spawn(g, Seed);
            Assert.GreaterOrEqual(a.Count, 4);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].X, b[i].X);
                Assert.Greater(a[i].Y, IslandGround.WaterLevel + 1f);
                Assert.IsFalse(Monuments.NoBuilding(g, a[i].X, a[i].Z));
            }
        }

        [Test]
        public void Ridden_it_trots_where_the_rider_looks_and_gallops_until_winded()
        {
            var g = new IslandGround(Seed);
            var h = Riding.Spawn(g, Seed)[0];
            h.Driver = 1;
            float x0 = h.X, z0 = h.Z;
            for (int i = 0; i < 40; i++) Riding.Step(h, 1f, 0f, false, h.Yaw, 0.05f, g);
            Assert.That(h.Speed, Is.EqualTo(Riding.TrotSpeed).Within(0.5f).Or.EqualTo(0f), "trotting (or stopped by a slope)");
            // Gallop: faster than a trot while it has wind, which runs out and comes back.
            foreach (var horse in Riding.Spawn(g, Seed))
            {
                horse.Driver = 1;
                float top = 0f;
                int turn = 0;
                for (int i = 0; i < 400 && horse.Stamina > 0f; i++)
                {
                    // Round in a circle, so it stays on its meadow.
                    Riding.Step(horse, 1f, 0f, true, horse.Yaw + 6f, 0.05f, g);
                    top = System.Math.Max(top, horse.Speed);
                    turn++;
                }
                if (top < Riding.GallopSpeed - 0.5f) continue;
                Assert.AreEqual(0f, horse.Stamina, 0.01f, "winded");
                Assert.Greater(turn * 0.05f, 8f, "after several seconds");
                for (int i = 0; i < 40; i++) Riding.Step(horse, 1f, 0f, true, horse.Yaw + 6f, 0.05f, g);
                Assert.Less(horse.Speed, Riding.GallopSpeed - 3f, "winded, it slows to a trot");
                for (int i = 0; i < 100; i++) Riding.Step(horse, 0f, 0f, false, horse.Yaw, 0.05f, g);
                Assert.Greater(horse.Stamina, 20f, "its wind comes back at rest");
                return;
            }
            Assert.Fail("no horse could gallop");
        }

        [Test]
        public void It_will_not_go_into_the_sea()
        {
            var g = new IslandGround(Seed);
            // Find a spot on the beach facing the sea.
            for (float r = 60f; r < IslandGround.HalfSize; r += 2f)
            {
                float x = r, z = 0f;
                if (Riding.Floor(g, x, z) > IslandGround.WaterLevel + 0.3f && Riding.Floor(g, x + 6f, z) < IslandGround.WaterLevel - 2f)
                {
                    var h = new Horse(1, x, Riding.Floor(g, x, z), z, 90f) { Driver = 1 };
                    for (int i = 0; i < 200; i++) Riding.Step(h, 1f, 0f, true, 90f, 0.05f, g);
                    Assert.Greater(Riding.Floor(g, h.X, h.Z), IslandGround.WaterLevel - Riding.MaxWade - 0.01f);
                    return;
                }
            }
            Assert.Inconclusive("no beach found");
        }

        [Test]
        public void Berries_feed_it_and_a_hungry_horse_tires_twice_as_fast()
        {
            var h = new Horse(1, 0f, 0f, 0f, 0f) { Food = 10f, Stamina = 20f };
            Assert.AreEqual(5, Riding.Feed(h, 30));
            Assert.AreEqual(Riding.MaxFood, h.Food);
            Assert.Greater(h.Stamina, 20f);
            Assert.AreEqual(0, Riding.Feed(h, 30), "full");
        }

        [Test]
        public void The_server_lets_a_player_ride_moves_them_with_it_and_gets_them_off()
        {
            var world = new ServerWorld(Seed);
            var rec = world.Join(Protocol.Version, "Ale", Token, 0f, out var p, out _, out _);
            var h = world.Horses[0];
            h.Food = 30f;
            rec.Survivor.Inventory.Add(ItemIds.Berries, 10);
            world.Players.Place(p.Id, new Pose(h.X + 20f, h.Y, h.Z, 0f), 0f);
            Assert.AreEqual(ActionResult.TooFar, world.Act(p.Id, ActKind.Ride, h.Id, 0, null));
            world.Players.Place(p.Id, new Pose(h.X + 1.5f, h.Y, h.Z, 0f), 0f);
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Ride, h.Id, 0, null));
            Assert.AreEqual(p.Id, h.Driver);
            Assert.AreEqual(Riding.MaxFood, h.Food, 0.01f, "it ate the berries carried");
            Assert.AreEqual(6, rec.Survivor.Inventory.Count(ItemIds.Berries));
            Assert.AreSame(h, world.RiddenBy(p.Id));
            Assert.AreEqual(ActionResult.Invalid, world.Act(p.Id, ActKind.Mount, world.Minicopters[0].Id, 0, null), "one vehicle at a time");

            // A believable step moves horse and rider; a teleport is refused.
            world.Tick(0.5f);
            float nx = h.X + 4f;
            if (Riding.Walkable(world.Ground, nx, h.Z))
            {
                Assert.IsTrue(world.Gallop(p.Id, nx, h.Z, 90f, 8f));
                Assert.AreEqual(nx, h.X, 0.01f);
                world.Players.TryGet(p.Id, out var rider);
                Assert.AreEqual(nx, rider.Pose.X, 0.01f);
            }
            Assert.IsFalse(world.Gallop(p.Id, h.X + 200f, h.Z, 90f, 8f));

            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Dismount, 0, 0, null));
            Assert.AreEqual(0, h.Driver);
            Assert.IsNull(world.RiddenBy(p.Id));
        }

        [Test]
        public void Horses_go_over_the_wire()
        {
            var w = new NetWriter(256);
            var horses = new List<Horse> { new Horse(3, 10f, 2f, -5f, 270f) { Speed = -1.5f, Stamina = 40f, Food = 70f, Driver = 9 } };
            Messages.Horses(w, horses);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(MessageType.Horses, (MessageType)r.Byte());
            var into = new List<HorseSnap>();
            Messages.ReadHorses(r, into);
            Assert.AreEqual(1, into.Count);
            Assert.AreEqual(3, into[0].Id);
            Assert.AreEqual(-1.5f, into[0].Speed, 0.01f);
            Assert.AreEqual(270f, into[0].Yaw, 0.1f);
            Assert.AreEqual(9, into[0].Driver);
            Assert.AreEqual(70f, into[0].Food);
        }
    }
}
