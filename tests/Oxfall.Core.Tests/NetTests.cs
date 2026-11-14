using System.Collections.Generic;
using NUnit.Framework;
using Oxfall.Core.Net;

namespace Oxfall.Core.Tests
{
    public class NetBufferTests
    {
        [Test]
        public void EveryFieldReadsBackAsWritten()
        {
            var w = new NetWriter(4);
            w.Byte(200);
            w.Bool(true);
            w.UShort(65000);
            w.Int(-123456789);
            w.Float(-3.25f);
            w.String("Ñandú ok");
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(200, r.Byte());
            Assert.IsTrue(r.Bool());
            Assert.AreEqual(65000, r.UShort());
            Assert.AreEqual(-123456789, r.Int());
            Assert.AreEqual(-3.25f, r.Float());
            Assert.AreEqual("Ñandú ok", r.String());
            Assert.AreEqual(0, r.Remaining);
        }

        [Test]
        public void ReadingPastTheEndThrows()
        {
            var r = new NetReader(new byte[] { 1, 2, 3 });
            r.UShort();
            Assert.Throws<ProtocolException>(() => r.UShort());
        }

        [Test]
        public void AStringClaimingMoreBytesThanSentThrows()
        {
            var r = new NetReader(new byte[] { 10, 0, 65, 66 });
            Assert.Throws<ProtocolException>(() => r.String());
        }

        [Test]
        public void LongStringsAreCut()
        {
            var w = new NetWriter();
            w.String(new string('a', 100), 8);
            Assert.AreEqual("aaaaaaaa", new NetReader(w.ToArray()).String());
        }

        [Test]
        public void UnknownMessageTypesAreRefused()
        {
            Assert.Throws<ProtocolException>(() => Protocol.PeekType(new byte[] { 99 }, 1));
            Assert.Throws<ProtocolException>(() => Protocol.PeekType(new byte[0], 0));
            Assert.AreEqual(MessageType.Move, Protocol.PeekType(new byte[] { 2 }, 1));
        }
    }

    public class MessageTests
    {
        private static NetReader After(NetWriter w, MessageType expected)
        {
            var bytes = w.ToArray();
            Assert.AreEqual(expected, Protocol.PeekType(bytes, bytes.Length));
            return new NetReader(bytes, 1, bytes.Length - 1);
        }

        [Test]
        public void HelloAndMoveRoundTrip()
        {
            var w = new NetWriter();
            Messages.Hello(w, "Ale");
            Messages.ReadHello(After(w, MessageType.Hello), out var version, out var name);
            Assert.AreEqual(Protocol.Version, version);
            Assert.AreEqual("Ale", name);

            Messages.Move(w, new Pose(1f, 2f, 3f, 90f), true);
            Messages.ReadMove(After(w, MessageType.Move), out var pose, out var teleport);
            Assert.AreEqual(new Pose(1f, 2f, 3f, 90f), pose);
            Assert.IsTrue(teleport);
        }

        [Test]
        public void ServerMessagesRoundTrip()
        {
            var w = new NetWriter();
            Messages.Welcome(w, 7, 1234, 5.5f);
            Messages.ReadWelcome(After(w, MessageType.Welcome), out var id, out var seed, out var time);
            Assert.AreEqual((7, 1234, 5.5f), (id, seed, time));

            Messages.Joined(w, 9, "Bea", new Pose(4f, 5f, 6f, 10f));
            Messages.ReadJoined(After(w, MessageType.Joined), out id, out var name, out var pose);
            Assert.AreEqual((9, "Bea", new Pose(4f, 5f, 6f, 10f)), (id, name, pose));

            Messages.Left(w, 9);
            Assert.AreEqual(9, Messages.ReadLeft(After(w, MessageType.Left)));

            Messages.Rejected(w, "lleno");
            Assert.AreEqual("lleno", Messages.ReadRejected(After(w, MessageType.Rejected)));

            Messages.Correction(w, new Pose(1f, 1f, 1f, 0f));
            Assert.AreEqual(new Pose(1f, 1f, 1f, 0f), Messages.ReadCorrection(After(w, MessageType.Correction)));
        }

        [Test]
        public void SnapshotRoundTrip()
        {
            var w = new NetWriter();
            var players = new List<(ushort, Pose)> { (1, new Pose(1f, 0f, 0f, 0f)), (2, new Pose(0f, 0f, 2f, 180f)) };
            Messages.Snapshot(w, 12.5f, players);
            var read = new List<(ushort Id, Pose Pose)>();
            float time = Messages.ReadSnapshot(After(w, MessageType.Snapshot), read);
            Assert.AreEqual(12.5f, time);
            CollectionAssert.AreEqual(players, read);
        }
    }

    public class ServerStateTests
    {
        private static readonly Pose Spawn = new Pose(0f, 1f, 0f, 0f);

        private static ServerState.Player Placed(ServerState s, string name, Pose at)
        {
            var p = s.Join(Protocol.Version, name, 0f, out _);
            Assert.IsTrue(s.ApplyMove(p.Id, at, false, 0f), "the first move places the player");
            return p;
        }

        [Test]
        public void PlayersJoinWithDistinctIdsAndCleanNames()
        {
            var s = new ServerState(42);
            var a = s.Join(Protocol.Version, "Ale<script>", 0f, out var why);
            var b = s.Join(Protocol.Version, "", 0f, out _);
            Assert.IsNull(why);
            Assert.AreNotEqual(a.Id, b.Id);
            Assert.AreNotEqual(0, a.Id);
            Assert.AreEqual("Alescript", a.Name);
            Assert.AreEqual("Superviviente", b.Name);
            Assert.AreEqual(2, s.Count);
            Assert.IsTrue(s.Leave(a.Id));
            Assert.AreEqual(1, s.Count);
        }

        [Test]
        public void AnotherVersionIsTurnedAway()
        {
            var s = new ServerState(42);
            Assert.IsNull(s.Join((ushort)(Protocol.Version - 1), "Old", 0f, out var why));
            StringAssert.Contains("antigua", why);
            Assert.AreEqual(0, s.Count);
        }

        [Test]
        public void AFullServerTurnsPlayersAway()
        {
            var s = new ServerState(42);
            for (int i = 0; i < Protocol.MaxPlayers; i++) Assert.IsNotNull(s.Join(Protocol.Version, "P" + i, 0f, out _));
            Assert.IsNull(s.Join(Protocol.Version, "One more", 0f, out var why));
            StringAssert.Contains("lleno", why);
        }

        [Test]
        public void BelievableMovesAreTakenAndTeleportsAreNot()
        {
            var s = new ServerState(42);
            var p = Placed(s, "Ale", Spawn);
            Assert.IsTrue(s.ApplyMove(p.Id, new Pose(4f, 1f, 0f, 0f), false, 0.5f), "8 m/s is a sprint");
            Assert.IsFalse(s.ApplyMove(p.Id, new Pose(60f, 1f, 0f, 0f), false, 1f), "112 m/s is not");
            Assert.AreEqual(new Pose(4f, 1f, 0f, 0f), p.Pose, "the old position stays");
            Assert.AreEqual(1, p.RejectedMoves);
            Assert.IsTrue(s.ApplyMove(p.Id, new Pose(60f, 1f, 0f, 0f), true, 1.1f), "waking up elsewhere is allowed");
        }

        [Test]
        public void WaitingLongDoesNotEarnALongJump()
        {
            var s = new ServerState(42);
            var p = Placed(s, "Ale", Spawn);
            Assert.IsFalse(s.ApplyMove(p.Id, new Pose(100f, 1f, 0f, 0f), false, 60f));
        }

        [Test]
        public void BrokenNumbersAreRefused()
        {
            var s = new ServerState(42);
            var p = Placed(s, "Ale", Spawn);
            Assert.IsFalse(s.ApplyMove(p.Id, new Pose(float.NaN, 1f, 0f, 0f), false, 1f));
            Assert.IsFalse(s.ApplyMove(999, new Pose(0f, 1f, 0f, 0f), false, 1f), "nobody with that id");
        }

        [Test]
        public void TheSnapshotHasEveryone()
        {
            var s = new ServerState(42);
            Placed(s, "A", Spawn);
            Placed(s, "B", new Pose(5f, 1f, 5f, 0f));
            s.Join(Protocol.Version, "C", 0f, out _);
            Assert.AreEqual(2, s.Snapshot().Count, "C has not said where it is yet");
        }
    }

    public class PoseBufferTests
    {
        [Test]
        public void SamplesBetweenTheTwoKnownPositions()
        {
            var b = new PoseBuffer();
            b.Add(1f, new Pose(0f, 0f, 0f, 350f));
            b.Add(2f, new Pose(10f, 0f, 0f, 10f));
            var mid = b.Sample(1.5f);
            Assert.AreEqual(5f, mid.X, 1e-4);
            Assert.AreEqual(360f, mid.Yaw, 1e-3, "turns the short way across north");
        }

        [Test]
        public void HoldsTheEndsOutsideTheKnownTimes()
        {
            var b = new PoseBuffer();
            b.Add(1f, new Pose(1f, 0f, 0f, 0f));
            b.Add(2f, new Pose(2f, 0f, 0f, 0f));
            Assert.AreEqual(1f, b.Sample(0f).X);
            Assert.AreEqual(2f, b.Sample(5f).X);
        }

        [Test]
        public void IgnoresOldOrRepeatedSnapshotsAndDropsWhatIsPast()
        {
            var b = new PoseBuffer();
            for (int i = 0; i < 10; i++) b.Add(i, new Pose(i, 0f, 0f, 0f));
            b.Add(3f, new Pose(99f, 0f, 0f, 0f));
            Assert.AreEqual(10, b.Count);
            Assert.AreEqual(7.5f, b.Sample(7.5f).X, 1e-4);
            Assert.AreEqual(3, b.Count, "only 7, 8 and 9 are still needed");
        }

        [Test]
        public void KeepsOnlyTheNewestWhenFull()
        {
            var b = new PoseBuffer();
            for (int i = 0; i < 100; i++) b.Add(i, new Pose(i, 0f, 0f, 0f));
            Assert.AreEqual(32, b.Count);
            Assert.AreEqual(68f, b.Sample(0f).X);
        }
    }

    public class ServerClockTests
    {
        [Test]
        public void TheLeastDelayedSnapshotSetsTheClock()
        {
            var c = new ServerClock();
            c.Observe(100f, 10f);     // offset 90 (arrived late)
            c.Observe(100.05f, 10f);  // offset 90.05: arrived sooner after being sent
            Assert.AreEqual(110.05f, c.Now(20f), 1e-3);
            c.Observe(101f, 11.5f);   // offset 89.5: delayed, only nudges
            Assert.Greater(c.Now(20f), 110f);
        }
    }
}
