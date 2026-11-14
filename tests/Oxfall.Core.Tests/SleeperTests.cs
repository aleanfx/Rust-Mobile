using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Tests
{
    public class SleeperTests
    {
        private const int Seed = 20261113;
        private const string TokenA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string TokenB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        /// <summary>Ale with a spear and a bow; Bea, carrying wood, goes away a metre and a half from her.</summary>
        private static (ServerWorld world, SurvivorRecord a, ServerState.Player pa, SurvivorRecord b) Asleep()
        {
            var world = new ServerWorld(Seed);
            var a = world.Join(Protocol.Version, "Ale", TokenA, 0f, out var pa, out _, out _);
            var b = world.Join(Protocol.Version, "Bea", TokenB, 0f, out var pb, out _, out _);
            world.Players.Place(pb.Id, new Pose(pa.Pose.X + 1.5f, pa.Pose.Y, pa.Pose.Z, 90f), 0f);
            a.Survivor.Inventory.Add(ItemIds.WoodenSpear, 1);
            a.Survivor.Inventory.Add(ItemIds.WoodenBow, 1);
            a.Survivor.Inventory.Add(ItemIds.Arrow, 10);
            b.Survivor.Inventory.Add(ItemIds.Wood, 300);
            world.SleepersChanged = false;
            world.Leave(pb.Id);
            return (world, a, pa, b);
        }

        [Test]
        public void APlayerWhoLeavesSleepsWhereTheyStood()
        {
            var (world, a, pa, b) = Asleep();
            Assert.IsTrue(world.SleepersChanged, "everyone is told");
            Assert.AreEqual(1, world.Sleepers.Count);
            Assert.AreEqual(b.Owner, world.Sleepers[0].Owner);
            Assert.AreEqual(pa.Pose.X + 1.5f, world.Sleepers[0].Pose.X, 1e-3f, "where they stood");
            Assert.IsTrue(world.TryGetSleeper(b.Owner, out _));
            Assert.IsFalse(world.TryGetSleeper(a.Owner, out _), "the awake are not asleep");

            world.SleepersChanged = false;
            world.Join(Protocol.Version, "Bea", TokenB, 5f, out var back, out _, out _);
            Assert.IsTrue(world.SleepersChanged);
            Assert.AreEqual(0, world.Sleepers.Count, "awake again");
            Assert.IsFalse(world.Players.Snapshot()[0].Down || world.Players.Snapshot()[1].Down, "standing");
            Assert.AreEqual(300, b.Survivor.Inventory.Count(ItemIds.Wood), "with everything they had");
        }

        [Test]
        public void ASleeperCanBeKilledAndTheirThingsFallIntoABag()
        {
            var (world, a, pa, b) = Asleep();
            float now = 1f;
            int blows = 0;
            bool killed = false;
            while (!killed && blows < 10)
            {
                Assert.IsTrue(world.StrikeSleeper(pa.Id, b.Owner, ToolKind.Spear, now, out float damage, out killed), "blow " + blows);
                Assert.Greater(damage, 0f);
                now += Combat.SwingSeconds(ToolKind.Spear);
                blows++;
            }
            Assert.AreEqual(3, blows, "as many blows as awake");
            Assert.IsTrue(b.Survivor.Vitals.IsDead);
            Assert.AreEqual(0, world.Sleepers.Count, "the body is gone");
            Assert.IsTrue(world.SleepersChanged);
            var bag = world.Announcements[world.Announcements.Count - 1];
            Assert.AreEqual(BuildOpKind.Deploy, bag.Kind, "a bag where they lay");
            Assert.AreEqual(0, b.Survivor.Inventory.Count(ItemIds.Wood), "everything is in it");
            Assert.IsFalse(world.StrikeSleeper(pa.Id, b.Owner, ToolKind.Spear, now + 5f, out _, out _), "the dead are not hit again");

            // She comes back dead, lying down, and learns who did it (once).
            world.Join(Protocol.Version, "Bea", TokenB, now + 10f, out var back, out _, out _);
            Assert.IsTrue(b.Survivor.Vitals.IsDead);
            Assert.IsTrue(world.Players.TryGet(back.Id, out var p) && p.Down);
            Assert.AreEqual("Ale", world.TakeKilledAsleep(b.Owner));
            Assert.IsNull(world.TakeKilledAsleep(b.Owner));
            Assert.AreEqual(ActionResult.Ok, world.Act(back.Id, ActKind.Respawn, 0, 0, null));
            Assert.IsFalse(p.Down, "up again on a beach");
        }

        [Test]
        public void BlowsAndArrowsAtASleeperAreJudged()
        {
            var (world, a, pa, b) = Asleep();
            Assert.IsFalse(world.StrikeSleeper(pa.Id, a.Owner, ToolKind.Spear, 1f, out _, out _), "not someone awake");
            Assert.IsFalse(world.StrikeSleeper(pa.Id, 999, ToolKind.Spear, 1f, out _, out _), "no such sleeper");
            Assert.IsFalse(world.StrikeSleeper(pa.Id, b.Owner, ToolKind.MetalHatchet, 1f, out _, out _), "a tool they do not have");
            Assert.IsFalse(world.StrikeSleeper(pa.Id, b.Owner, ToolKind.Hammer, 1f, out _, out _), "the hammer builds");
            Assert.IsTrue(world.StrikeSleeper(pa.Id, b.Owner, ToolKind.Spear, 1f, out _, out _));
            Assert.IsFalse(world.StrikeSleeper(pa.Id, b.Owner, ToolKind.Spear, 1.2f, out _, out _), "faster than it swings");
            world.Players.Place(pa.Id, new Pose(pa.Pose.X - 10f, pa.Pose.Y, pa.Pose.Z, 0f), 2f);
            Assert.IsFalse(world.StrikeSleeper(pa.Id, b.Owner, ToolKind.Spear, 3f, out _, out _), "out of reach");

            // From there, an arrow: it reaches.
            var eyes = (x: pa.Pose.X, y: pa.Pose.Y + 1.6f, z: pa.Pose.Z);
            Assert.IsTrue(world.Shoot(pa.Id, eyes.x, eyes.y, eyes.z, 4f));
            Assert.IsTrue(world.ArrowHit(pa.Id, ArrowTarget.Sleeper, b.Owner, 4.3f, out float damage, out bool killed, out _));
            Assert.AreEqual(Combat.ArrowDamage, damage, 1e-3f);
            Assert.IsFalse(killed);
            Assert.AreEqual(100f - Combat.MeleeDamage(ToolKind.Spear) - Combat.ArrowDamage, b.Survivor.Vitals.Health, 1e-3f);
        }

        [Test]
        public void SleeperMessagesRoundTrip()
        {
            var (world, _, _, b) = Asleep();
            var w = new NetWriter();
            Messages.Sleepers(w, world.Sleepers);
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Sleepers, (MessageType)r.Byte());
            var list = new System.Collections.Generic.List<SleeperSnap>();
            Messages.ReadSleepers(r, list);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(b.Owner, list[0].Owner);
            Assert.AreEqual("Bea", list[0].Name);
            Assert.AreEqual(b.Pose.X, list[0].X, 1e-3f);
            Assert.AreEqual(90f, list[0].Yaw, 1.5f);

            Messages.StrikeSleeper(w, 42, (byte)ToolKind.Spear);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.StrikeSleeper, (MessageType)r.Byte());
            Messages.ReadStrikeSleeper(r, out int owner, out byte tool);
            Assert.AreEqual((42, (byte)ToolKind.Spear), (owner, tool));

            Messages.SleeperHit(w, 42, 7, "Ale", 34.6f, true);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.SleeperHit, (MessageType)r.Byte());
            Messages.ReadSleeperHit(r, out owner, out ushort attacker, out string by, out int damage, out bool killed);
            Assert.AreEqual((42, (ushort)7, "Ale", 35, true), (owner, attacker, by, damage, killed));
        }
    }
}
