using NUnit.Framework;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Minicopters: spawned by the ruins, flown where the pilot looks, fuel, crashes and wrecks.</summary>
    public class MinicopterTests
    {
        private const int Seed = 20261114;

        private static Minicopter Flown(IslandGround g)
        {
            var m = Flying.Spawn(g)[0];
            m.Driver = 1;
            return m;
        }

        [Test]
        public void OneWaitsByEachRuinOnDryLand()
        {
            var g = new IslandGround(Seed);
            var copters = Flying.Spawn(g);
            Assert.That(copters.Count, Is.GreaterThanOrEqualTo(1));
            foreach (var m in copters)
            {
                Assert.That(Flying.OverWater(g, m.X, m.Z), Is.False);
                Assert.AreEqual(Flying.Floor(g, m.X, m.Z), m.Y, 0.01f, "stands on the ground");
                Assert.AreEqual(Flying.StartFuel, m.Fuel);
            }
            Assert.AreEqual(copters[0].X, Flying.Spawn(g)[0].X, "same spots for a seed");
        }

        [Test]
        public void ClimbsHoversAndComesDownWithoutHarm()
        {
            var g = new IslandGround(Seed);
            var m = Flown(g);
            float y0 = m.Y;
            for (int i = 0; i < 50; i++) Flying.Step(m, 0f, 0f, true, m.Yaw, 0f, 0.1f, g);
            Assert.That(m.Y, Is.GreaterThan(y0 + 15f), "lifted straight up");
            float high = m.Y;
            for (int i = 0; i < 40; i++) Flying.Step(m, 0f, 0f, false, m.Yaw, 0f, 0.1f, g);
            Assert.That(m.Y, Is.EqualTo(high).Within(4f), "holds its height with the stick let go");
            float crashed = 0f;
            for (int i = 0; i < 200; i++) crashed += Flying.Step(m, 0.4f, 0f, false, m.Yaw, 70f, 0.1f, g);
            Assert.That(m.Y - Flying.Floor(g, m.X, m.Z), Is.LessThan(0.1f), "looked down and flew down");
            Assert.AreEqual(0f, crashed, "a gentle descent is no crash");
            Assert.AreEqual(Flying.MaxHealth, m.Health);
        }

        [Test]
        public void FliesWhereThePilotLooksAndBurnsFuel()
        {
            var g = new IslandGround(Seed);
            var m = Flown(g);
            for (int i = 0; i < 30; i++) Flying.Step(m, 0f, 0f, true, 90f, 0f, 0.1f, g);
            float x0 = m.X, y0 = m.Y, fuel0 = m.Fuel;
            for (int i = 0; i < 30; i++) Flying.Step(m, 1f, 0f, false, 90f, -20f, 0.1f, g);
            Assert.That(m.X - x0, Is.GreaterThan(20f), "east, the way it looked");
            Assert.That(m.Y, Is.GreaterThan(y0 + 3f), "looking up it climbs");
            Assert.That(m.Fuel, Is.EqualTo(fuel0 - 3f / Flying.SecondsPerFuel).Within(0.01f));
            Assert.That(System.Math.Sqrt(m.Vx * m.Vx + m.Vz * m.Vz), Is.LessThanOrEqualTo(Flying.MaxSpeed + 0.01f));
        }

        [Test]
        public void OutOfFuelItDropsAndCrashes()
        {
            var g = new IslandGround(Seed);
            var m = Flown(g);
            for (int i = 0; i < 80; i++) Flying.Step(m, 0f, 0f, true, m.Yaw, 0f, 0.1f, g);
            Assert.That(m.Y - Flying.Floor(g, m.X, m.Z), Is.GreaterThan(25f));
            m.Fuel = 0f;
            float crashed = 0f;
            for (int i = 0; i < 100; i++) crashed += Flying.Step(m, 0f, 0f, true, m.Yaw, 0f, 0.1f, g);
            Assert.That(crashed, Is.GreaterThan(5f));
            Assert.That(m.Wrecked, Is.True, "from that high it is a wreck");
        }

        [Test]
        public void IdleOnTheGroundBurnsNothing()
        {
            var g = new IslandGround(Seed);
            var m = Flown(g);
            for (int i = 0; i < 50; i++) Flying.Step(m, 0f, 0f, false, m.Yaw, 0f, 0.1f, g);
            Assert.AreEqual(Flying.StartFuel, m.Fuel);
        }

        [Test]
        public void ServerMountsFuelsFliesAndCrashes()
        {
            var world = new ServerWorld(Seed);
            var rec = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var p, out _, out _);
            var m = world.Minicopters[0];
            world.Players.Place(p.Id, new Pose(m.X + 30f, m.Y, m.Z, 0f), 0f);
            Assert.AreEqual(ActionResult.TooFar, world.Act(p.Id, ActKind.Mount, m.Id, 0, null));
            rec.Survivor.Inventory.Add(ItemIds.LowGradeFuel, 300);
            world.Players.Place(p.Id, new Pose(m.X + 1.5f, m.Y, m.Z, 0f), 0f);
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Mount, m.Id, 0, null));
            Assert.AreEqual(Flying.TankSize, m.Fuel, 0.001f, "the fuel carried went into the tank");
            Assert.AreEqual(300 - (Flying.TankSize - Flying.StartFuel), rec.Survivor.Inventory.Count(ItemIds.LowGradeFuel));
            world.Tick(1f);
            Assert.That(world.Fly(p.Id, m.X, m.Y + 5f, m.Z, 0f), Is.True, "up 5 m in a second");
            world.Tick(0.5f);
            Assert.That(world.Fly(p.Id, m.X + 200f, m.Y, m.Z, 0f), Is.False, "not 200 m in half a second");
            Assert.That(m.Fuel, Is.LessThan(Flying.TankSize));
            // Dropped straight down from high up onto the ground: a crash.
            for (int i = 0; i < 2; i++) { world.Tick(1f); Assert.That(world.Fly(p.Id, m.X, m.Y + 8f, m.Z, 0f), Is.True, $"climb {i} at y={m.Y} fuel={m.Fuel}"); }
            world.Tick(1f);
            float hp = rec.Survivor.Vitals.Health;
            Assert.That(world.Fly(p.Id, m.X, Flying.Floor(world.Ground, m.X, m.Z), m.Z, 0f), Is.True);
            Assert.That(m.Health, Is.LessThan(Flying.MaxHealth));
            Assert.That(rec.Survivor.Vitals.Health, Is.LessThan(hp), "the pilot is hurt too");
            Assert.That(m.Wrecked, Is.True, "16 m a second into the ground");
            Assert.IsNull(world.FlownBy(p.Id), "thrown out of the wreck");
            Assert.AreEqual(ActionResult.TooFar, world.Act(p.Id, ActKind.Mount, m.Id, 0, null), "a wreck cannot be flown");
        }

        [Test]
        public void LeftInTheAirItFallsAndAWreckIsRebuilt()
        {
            var world = new ServerWorld(Seed);
            world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var p, out _, out _);
            var m = world.Minicopters[0];
            world.Players.Place(p.Id, new Pose(m.X + 1.5f, m.Y, m.Z, 0f), 0f);
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Mount, m.Id, 0, null));
            for (int i = 0; i < 12; i++) { world.Tick(1f); world.Fly(p.Id, m.X, m.Y + 8f, m.Z, 0f); }
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Dismount, 0, 0, null));
            for (int i = 0; i < 100; i++) world.Tick(0.1f);
            Assert.That(m.Wrecked, Is.True);
            world.Tick(Flying.RebuildSeconds + 1f);
            world.Tick(0.1f);
            Assert.That(m.Wrecked, Is.False);
            Assert.AreEqual(m.HomeX, m.X);
        }

        [Test]
        public void CoptersSurviveTheTrip()
        {
            var copters = Flying.Spawn(new IslandGround(Seed));
            copters[0].Driver = 9;
            copters[0].Fuel = 42.34f;
            copters[0].Health = 57.2f;
            var w = new NetWriter();
            Messages.Minicopters(w, copters);
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Minicopters, (MessageType)r.Byte());
            var read = new List<MinicopterSnap>();
            Messages.ReadMinicopters(r, read);
            Assert.AreEqual(copters.Count, read.Count);
            Assert.AreEqual(9, read[0].Driver);
            Assert.AreEqual(42.4f, read[0].Fuel, 0.001f);
            Assert.AreEqual(58f, read[0].Health);
            Assert.AreEqual(copters[0].Y, read[0].Y);
            Assert.AreEqual(0, r.Remaining);
            Messages.Fly(w, 1f, 2f, 3f, 4f);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Fly, (MessageType)r.Byte());
            Messages.ReadFly(r, out _, out float y, out _, out float yaw);
            Assert.AreEqual(2f, y);
            Assert.AreEqual(4f, yaw);
        }
    }
}
