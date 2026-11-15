using NUnit.Framework;
using System.Collections.Generic;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Rowboats: spawned off the beaches, rowed over the sea, run aground, step out on land.</summary>
    public class BoatTests
    {
        private const int Seed = 20261114;

        [Test]
        public void BoatsSpawnAfloatOffTheShore()
        {
            var g = new IslandGround(Seed);
            var boats = Boating.Spawn(g);
            Assert.That(boats.Count, Is.GreaterThanOrEqualTo(Boating.Count - 1));
            foreach (var b in boats)
            {
                Assert.That(Boating.Floats(g, b.X, b.Z), Is.True);
                Boating.Landing(b, g, out _, out float y, out _);
                Assert.That(y, Is.GreaterThan(IslandGround.WaterLevel), "land close enough to step out");
            }
            Assert.That(Boating.Spawn(g)[0].X, Is.EqualTo(boats[0].X), "same spots for a seed");
        }

        [Test]
        public void RowingForwardSpeedsUpToTheCap()
        {
            var g = new IslandGround(Seed);
            var b = Boating.Spawn(g)[0];
            float x0 = b.X, z0 = b.Z;
            for (int i = 0; i < 20; i++) Boating.Step(b, 1f, 0f, 0.1f, g);
            Assert.That(b.Speed, Is.GreaterThan(4f).And.LessThanOrEqualTo(Boating.MaxSpeed));
            float moved = (float)System.Math.Sqrt((b.X - x0) * (b.X - x0) + (b.Z - z0) * (b.Z - z0));
            Assert.That(moved, Is.GreaterThan(3f), "out to sea (bow points away from land)");
        }

        [Test]
        public void OarsStillItDriftsToAStop()
        {
            var g = new IslandGround(Seed);
            var b = Boating.Spawn(g)[0];
            b.Speed = 5f;
            for (int i = 0; i < 60; i++) Boating.Step(b, 0f, 0f, 0.1f, g);
            Assert.That(b.Speed, Is.EqualTo(0f));
        }

        [Test]
        public void TurnsFasterMoving()
        {
            var g = new IslandGround(Seed);
            var still = Boating.Spawn(g)[0];
            var moving = Boating.Spawn(g)[0];
            moving.Speed = Boating.MaxSpeed;
            float y0 = still.Yaw;
            Boating.Step(still, 0f, 1f, 0.5f, g);
            Boating.Step(moving, 1f, 1f, 0.5f, g);
            Assert.That(still.Yaw - y0, Is.GreaterThan(0f));
            Assert.That(moving.Yaw - y0, Is.GreaterThan(still.Yaw - y0));
        }

        [Test]
        public void RunsAgroundInsteadOfLeavingTheWater()
        {
            var g = new IslandGround(Seed);
            var b = Boating.Spawn(g)[0];
            b.Yaw = (b.Yaw + 180f) % 360f; // bow to the beach
            for (int i = 0; i < 200; i++) Boating.Step(b, 1f, 0f, 0.1f, g);
            Assert.That(Boating.Floats(g, b.X, b.Z), Is.True);
            Assert.That(b.Speed, Is.LessThan(Boating.MaxSpeed));
        }

        [Test]
        public void ServerBelievesOnlyBoatSpeedOnWater()
        {
            var g = new IslandGround(Seed);
            var b = Boating.Spawn(g)[0];
            float rad = b.Yaw * (float)System.Math.PI / 180f;
            Assert.That(Boating.Believable(b, b.X + (float)System.Math.Sin(rad) * 4f, b.Z + (float)System.Math.Cos(rad) * 4f, 0.5f, g), Is.True);
            Assert.That(Boating.Believable(b, b.X + (float)System.Math.Sin(rad) * 30f, b.Z + (float)System.Math.Cos(rad) * 30f, 0.5f, g), Is.False);
            Assert.That(Boating.Believable(b, 0f, 0f, 100f, g), Is.False, "the middle of the island is dry land");
        }

        [Test]
        public void BoardOnlyNearAndEmpty()
        {
            var g = new IslandGround(Seed);
            var b = Boating.Spawn(g)[0];
            Assert.That(Boating.CanBoard(b, b.X + 2f, 0.5f, b.Z), Is.True);
            Assert.That(Boating.CanBoard(b, b.X + 9f, 0.5f, b.Z), Is.False);
            b.Driver = 3;
            Assert.That(Boating.CanBoard(b, b.X + 1f, 0.5f, b.Z), Is.False);
        }
    
        [Test]
        public void ServerBoardRowAndLeave()
        {
            var world = new ServerWorld(Seed);
            world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var p, out _, out _);
            var boat = world.Boats[0];
            Boating.Landing(boat, world.Ground, out float lx, out float ly, out float lz);
            world.Players.Place(p.Id, new Pose(boat.X + 30f, 0f, boat.Z, 0f), 0f);
            Assert.AreEqual(ActionResult.TooFar, world.Act(p.Id, ActKind.Board, boat.Id, 0, null));
            world.Players.Place(p.Id, new Pose(boat.X + 2f, 0f, boat.Z, 0f), 0f);
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Board, boat.Id, 0, null));
            Assert.AreEqual(p.Id, boat.Driver);
            world.Tick(0.5f);
            float rad = boat.Yaw * (float)System.Math.PI / 180f;
            float nx = boat.X + (float)System.Math.Sin(rad) * 3f, nz = boat.Z + (float)System.Math.Cos(rad) * 3f;
            Assert.That(world.Row(p.Id, nx, nz, boat.Yaw, 6f), Is.True);
            Assert.AreEqual(nx, boat.X, 0.001f);
            world.Players.TryGet(p.Id, out var rower);
            Assert.AreEqual(nx, rower.Pose.X, 0.001f, "the rower sits in it");
            world.Tick(0.1f);
            Assert.That(world.Row(p.Id, 0f, 0f, 0f, 6f), Is.False, "not onto dry land, nor that far");
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.LeaveBoat, 0, 0, null));
            Assert.AreEqual(0, boat.Driver);
            world.Tick(2f);
            Assert.That(boat.Speed, Is.LessThan(6f), "drifts to a stop with nobody rowing");
        }

        [Test]
        public void BoatsAndRowingSurviveTheTrip()
        {
            var g = new IslandGround(Seed);
            var boats = Boating.Spawn(g);
            boats[1].Driver = 42;
            boats[1].Speed = -1.5f;
            var w = new NetWriter();
            Messages.Boats(w, boats);
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Boats, (MessageType)r.Byte());
            var read = new List<BoatSnap>();
            Messages.ReadBoats(r, read);
            Assert.AreEqual(boats.Count, read.Count);
            Assert.AreEqual(42, read[1].Driver);
            Assert.AreEqual(-1.5f, read[1].Speed, 0.05f);
            Assert.AreEqual(boats[0].Yaw, read[0].Yaw, 0.01f);
            Assert.AreEqual(0, r.Remaining);
            Messages.Row(w, 1f, 2f, 3f, 4f);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Row, (MessageType)r.Byte());
            Messages.ReadRow(r, out float x, out float z, out float yaw, out float speed);
            Assert.AreEqual(4f, speed);
        }
            private static Boat Motorboat(List<Boat> boats) => boats.Find(b => b.Kind == BoatKind.Motorboat);

        [Test]
        public void SomeBoatsAreMotorboatsWithALittleFuel()
        {
            var boats = Boating.Spawn(new IslandGround(Seed));
            int motors = boats.FindAll(b => b.Kind == BoatKind.Motorboat).Count;
            Assert.That(motors, Is.GreaterThanOrEqualTo(1).And.LessThan(boats.Count));
            Assert.AreEqual(Boating.StartFuel, Motorboat(boats).Fuel);
            Assert.AreEqual(0f, boats.Find(b => b.Kind == BoatKind.Rowboat).Fuel);
        }

        [Test]
        public void MotorGoesTwiceAsFastAndBurnsFuel()
        {
            var g = new IslandGround(Seed);
            var boats = Boating.Spawn(g);
            var m = Motorboat(boats);
            for (int i = 0; i < 40; i++) Boating.Step(m, 1f, 0f, 0.1f, g);
            Assert.That(m.Speed, Is.GreaterThan(Boating.MaxSpeed + 2f), "faster than oars");
            Assert.That(m.Fuel, Is.EqualTo(Boating.StartFuel - 4f / Boating.SecondsPerFuel).Within(0.05f));
            m.Fuel = 0.01f;
            for (int i = 0; i < 80; i++) Boating.Step(m, 1f, 0f, 0.1f, g);
            Assert.AreEqual(0f, m.Fuel);
            Assert.That(m.Speed, Is.LessThanOrEqualTo(Boating.MaxSpeed + 0.01f), "dry, it is rowed like any boat");
        }

        [Test]
        public void IdleMotorBurnsNothing()
        {
            var g = new IslandGround(Seed);
            var m = Motorboat(Boating.Spawn(g));
            for (int i = 0; i < 50; i++) Boating.Step(m, 0f, 0.5f, 0.1f, g);
            Assert.AreEqual(Boating.StartFuel, m.Fuel);
        }

        [Test]
        public void ServerRefuelsFromThePlayersCansAndBurnsByTheWayItWent()
        {
            var world = new ServerWorld(Seed);
            var rec = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var p, out _, out _);
            var boat = Motorboat(world.Boats);
            var row = world.Boats.Find(b => b.Kind == BoatKind.Rowboat);
            world.Players.Place(p.Id, new Pose(boat.X + 2f, 0f, boat.Z, 0f), 0f);
            var inv = rec.Survivor.Inventory;
            Assert.AreEqual(ActionResult.NoFuel, world.Act(p.Id, ActKind.Refuel, boat.Id, 0, null));
            inv.Add(Items.ItemIds.LowGradeFuel, 500);
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Refuel, boat.Id, 0, null));
            Assert.AreEqual(Boating.TankSize, boat.Fuel, 0.001f, "filled to the brim");
            Assert.AreEqual(500 - (Boating.TankSize - Boating.StartFuel), inv.Count(Items.ItemIds.LowGradeFuel));
            Assert.AreEqual(ActionResult.Invalid, world.Act(p.Id, ActKind.Refuel, row.Id, 0, null), "a rowboat has no tank");
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Board, boat.Id, 0, null));
            world.Tick(1f);
            float rad = boat.Yaw * (float)System.Math.PI / 180f;
            float nx = boat.X + (float)System.Math.Sin(rad) * 14f, nz = boat.Z + (float)System.Math.Cos(rad) * 14f;
            Assert.That(world.Row(p.Id, nx, nz, boat.Yaw, 14f), Is.True, "14 m in a second at full throttle");
            Assert.AreEqual(Boating.TankSize - 14f / Boating.MotorSpeed / Boating.SecondsPerFuel, boat.Fuel, 0.01f);
        }

        [Test]
        public void ServerDoesNotBelieveMotorSpeedWithADryTank()
        {
            var world = new ServerWorld(Seed);
            world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var p, out _, out _);
            var boat = Motorboat(world.Boats);
            boat.Fuel = 0f;
            world.Players.Place(p.Id, new Pose(boat.X + 2f, 0f, boat.Z, 0f), 0f);
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Board, boat.Id, 0, null));
            world.Tick(1f);
            float rad = boat.Yaw * (float)System.Math.PI / 180f;
            Assert.That(world.Row(p.Id, boat.X + (float)System.Math.Sin(rad) * 14f, boat.Z + (float)System.Math.Cos(rad) * 14f, boat.Yaw, 14f), Is.False);
        }

        [Test]
        public void KindAndFuelSurviveTheTrip()
        {
            var boats = Boating.Spawn(new IslandGround(Seed));
            var m = Motorboat(boats);
            m.Fuel = 123.45f;
            m.Speed = 14.2f;
            var w = new NetWriter();
            Messages.Boats(w, boats);
            var r = new NetReader(w.Buffer, 0, w.Length);
            r.Byte();
            var read = new List<BoatSnap>();
            Messages.ReadBoats(r, read);
            var snap = read.Find(s => s.Id == m.Id);
            Assert.AreEqual(BoatKind.Motorboat, snap.Kind);
            Assert.AreEqual(123.5f, snap.Fuel, 0.001f, "never more than is left, rounded up to a tenth");
            Assert.AreEqual(14.2f, snap.Speed, 0.06f);
        }
    }
}
