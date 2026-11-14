using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;

namespace Oxfall.Core.Tests
{
    public class ArcheryTests
    {
        private const int Seed = 20261113;

        /// <summary>An archer with a bow and arrows, and another player 30 m away.</summary>
        private static (ServerWorld world, SurvivorRecord a, ServerState.Player pa, SurvivorRecord b, ServerState.Player pb) Range()
        {
            var world = new ServerWorld(Seed);
            var a = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var pa, out _, out _);
            var b = world.Join(Protocol.Version, "Bea", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 0f, out var pb, out _, out _);
            var at = pa.Pose;
            world.Players.Place(pb.Id, new Pose(at.X + 30f, at.Y, at.Z, 0f), 0f);
            a.Survivor.Inventory.Add(ItemIds.WoodenBow, 1);
            a.Survivor.Inventory.Add(ItemIds.Arrow, 5);
            return (world, a, pa, b, pb);
        }

        private static (float x, float y, float z) Eyes(ServerState.Player p) => (p.Pose.X, p.Pose.Y + 1.6f, p.Pose.Z);

        [Test]
        public void ABowAndArrowsAreMadeFromWoodClothAndStone()
        {
            var catalog = ItemCatalog.CreateDefault();
            var book = RecipeBook.CreateDefault(catalog);
            Assert.IsTrue(book.TryGet("wooden_bow", out var bow));
            Assert.AreEqual(ItemIds.WoodenBow, bow.OutputItemId);
            Assert.AreEqual(ToolKind.Bow, catalog.Get(ItemIds.WoodenBow).Tool);
            Assert.IsTrue(book.TryGet("arrows", out var arrows));
            Assert.AreEqual(2, arrows.OutputAmount, "two at a time");
            Assert.AreEqual(ItemCategory.Ammo, catalog.Get(ItemIds.Arrow).Category);
            Assert.IsFalse(Combat.CanHurt(ToolKind.Bow), "a bow is not a club");
            Assert.IsTrue(Combat.Shoots(ToolKind.Bow));
        }

        [Test]
        public void EachShotSpendsAnArrowAndIsJudged()
        {
            var (world, a, pa, b, pb) = Range();
            var (x, y, z) = Eyes(pa);
            Assert.IsTrue(world.Shoot(pa.Id, x, y, z, 1f));
            Assert.AreEqual(4, a.Survivor.Inventory.Count(ItemIds.Arrow));
            Assert.IsFalse(world.Shoot(pa.Id, x, y, z, 1.3f), "faster than a bow draws");
            Assert.IsFalse(world.Shoot(pa.Id, x + 10f, y, z, 3f), "not from where they stand");
            Assert.IsFalse(world.Shoot(pb.Id, Eyes(pb).x, Eyes(pb).y, Eyes(pb).z, 3f), "no bow");
            Assert.AreEqual(4, a.Survivor.Inventory.Count(ItemIds.Arrow), "refused shots spend nothing");

            a.Survivor.Inventory.Remove(ItemIds.Arrow, 4);
            Assert.IsFalse(world.Shoot(pa.Id, x, y, z, 5f), "no arrows left");
        }

        [Test]
        public void ThreeArrowsKillAPlayer()
        {
            var (world, a, pa, b, pb) = Range();
            var (x, y, z) = Eyes(pa);
            float now = 1f;
            bool killed = false;
            int hits = 0;
            while (!killed && hits < 5)
            {
                Assert.IsTrue(world.Shoot(pa.Id, x, y, z, now));
                Assert.IsTrue(world.ArrowHit(pa.Id, ArrowTarget.Player, pb.Id, now + 0.6f, out float damage, out killed, out _));
                Assert.Greater(damage, 0f);
                hits++;
                now += Combat.ShotSeconds;
            }
            Assert.AreEqual(3, hits);
            Assert.IsTrue(b.Survivor.Vitals.IsDead);
            Assert.IsFalse(world.ArrowHit(pa.Id, ArrowTarget.Player, pb.Id, now, out _, out _, out _), "no arrow in the air");
        }

        [Test]
        public void AnArrowOnlyLandsWhereItCouldHaveReached()
        {
            var (world, a, pa, b, pb) = Range();
            var (x, y, z) = Eyes(pa);
            Assert.IsTrue(world.Shoot(pa.Id, x, y, z, 1f));
            Assert.IsFalse(world.ArrowHit(pa.Id, ArrowTarget.Player, pa.Id, 1.2f, out _, out _, out _), "not themselves");

            world.Players.Place(pb.Id, new Pose(pa.Pose.X + 150f, pa.Pose.Y, pa.Pose.Z, 0f), 0f);
            Assert.IsTrue(world.Shoot(pa.Id, x, y, z, 3f));
            Assert.IsFalse(world.ArrowHit(pa.Id, ArrowTarget.Player, pb.Id, 3.5f, out _, out _, out _), "too far for a bow");

            Assert.IsTrue(world.Shoot(pa.Id, x, y, z, 5f));
            Assert.IsFalse(world.ArrowHit(pa.Id, ArrowTarget.None, 0, 5.4f, out _, out _, out _), "into the ground");
            Assert.IsTrue(world.Shoot(pa.Id, x, y, z, 7f));
            Assert.IsFalse(world.ArrowHit(pa.Id, ArrowTarget.Player, pb.Id, 7f + Combat.ArrowFlightSeconds + 1f, out _, out _, out _), "long since landed");
            Assert.AreEqual(100f, b.Survivor.Vitals.Health, 1e-3f);
        }

        [Test]
        public void ArrowsHuntDeerAndTurnWolvesOnTheArcher()
        {
            var (world, a, pa, _, _) = Range();
            var (x, y, z) = Eyes(pa);
            var deer = world.Fauna.Add(AnimalKind.Deer, pa.Pose.X + 25f, pa.Pose.Z, 0f);
            float now = 1f;
            AnimalBlow blow = AnimalBlow.None;
            int arrows = 0;
            while (blow != AnimalBlow.Killed && arrows < 5)
            {
                Assert.IsTrue(world.Shoot(pa.Id, x, y, z, now));
                Assert.IsTrue(world.ArrowHit(pa.Id, ArrowTarget.Animal, deer.Id, now + 0.5f, out _, out _, out blow));
                // Back where it was for the next arrow (it ran).
                deer.X = pa.Pose.X + 25f;
                deer.Z = pa.Pose.Z;
                arrows++;
                now += Combat.ShotSeconds;
            }
            Assert.AreEqual(3, arrows);
            Assert.IsTrue(deer.IsDead);
            Assert.IsTrue(world.Shoot(pa.Id, x, y, z, now));
            Assert.IsFalse(world.ArrowHit(pa.Id, ArrowTarget.Animal, deer.Id, now + 0.5f, out _, out _, out _), "a carcass is cut, not shot");

            var wolf = world.Fauna.Add(AnimalKind.Wolf, pa.Pose.X + 18f, pa.Pose.Z, 0f);
            now += Combat.ShotSeconds;
            a.Survivor.Inventory.Add(ItemIds.Arrow, 2);
            Assert.IsTrue(world.Shoot(pa.Id, x, y, z, now));
            Assert.IsTrue(world.ArrowHit(pa.Id, ArrowTarget.Animal, wolf.Id, now + 0.4f, out _, out _, out blow));
            Assert.AreEqual(AnimalBlow.Hurt, blow);
            Assert.AreEqual(AnimalState.Chase, wolf.State, "it comes for whoever shot it");
        }

        [Test]
        public void ArrowMessagesRoundTrip()
        {
            var w = new NetWriter();
            Messages.Shoot(w, 1f, 2f, 3f, 45f, -10f);
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Shoot, (MessageType)r.Byte());
            Messages.ReadShoot(r, out float x, out float y, out float z, out float yaw, out float pitch);
            Assert.AreEqual((1f, 2f, 3f, 45f, -10f), (x, y, z, yaw, pitch));

            Messages.ArrowHit(w, ArrowTarget.Animal, 123456);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.ArrowHit, (MessageType)r.Byte());
            Messages.ReadArrowHit(r, out var kind, out int target);
            Assert.AreEqual((ArrowTarget.Animal, 123456), (kind, target));

            Messages.Arrow(w, 7, 1f, 2f, 3f, 90f, 5f);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Arrow, (MessageType)r.Byte());
            Messages.ReadArrow(r, out ushort shooter, out x, out y, out z, out yaw, out pitch);
            Assert.AreEqual(((ushort)7, 1f, 2f, 3f, 90f, 5f), (shooter, x, y, z, yaw, pitch));
        }
    }
}
