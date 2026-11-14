using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Tests
{
    public class CombatTests
    {
        private const int Seed = 20261113;

        /// <summary>Two players a metre and a half apart, the first with a wooden spear.</summary>
        private static (ServerWorld world, SurvivorRecord a, ServerState.Player pa, SurvivorRecord b, ServerState.Player pb) Duel()
        {
            var world = new ServerWorld(Seed);
            var a = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var pa, out _, out _);
            var b = world.Join(Protocol.Version, "Bea", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 0f, out var pb, out _, out _);
            var at = pa.Pose;
            world.Players.Place(pb.Id, new Pose(at.X + 1.5f, at.Y, at.Z, 0f), 0f);
            a.Survivor.Inventory.Add(ItemIds.WoodenSpear, 1);
            return (world, a, pa, b, pb);
        }

        [Test]
        public void ASpearKillsInThreeBlowsAndTheDeadDropTheirBag()
        {
            var (world, a, pa, b, pb) = Duel();
            b.Survivor.Inventory.Add(ItemIds.Wood, 90);
            float now = 1f;
            int blows = 0;
            bool killed = false;
            while (!killed && blows < 10)
            {
                Assert.IsTrue(world.Strike(pa.Id, pb.Id, ToolKind.Spear, now, out float damage, out killed), "blow " + blows);
                Assert.Greater(damage, 0f);
                now += Combat.SwingSeconds(ToolKind.Spear);
                blows++;
            }
            Assert.AreEqual(3, blows);
            Assert.IsTrue(b.Survivor.Vitals.IsDead);
            Assert.IsTrue(world.Players.Snapshot()[1].Down || world.Players.Snapshot()[0].Down, "lying down for everyone");
            Assert.IsFalse(world.Strike(pa.Id, pb.Id, ToolKind.Spear, now + 5f, out _, out _), "the dead are not hit again");
            Assert.AreEqual(BuildOpKind.Deploy, world.Announcements[world.Announcements.Count - 1].Kind, "the bag drops");

            Assert.AreEqual(ActionResult.Ok, world.Act(pb.Id, ActKind.Respawn, 0, 0, null));
            foreach (var p in world.Players.Snapshot()) Assert.IsFalse(p.Down, "up again");
        }

        [Test]
        public void BlowsThatCouldNotHaveLandedDoNotCount()
        {
            var (world, a, pa, b, pb) = Duel();
            Assert.IsFalse(world.Strike(pa.Id, pa.Id, ToolKind.Spear, 1f, out _, out _), "not yourself");
            Assert.IsFalse(world.Strike(pa.Id, pb.Id, ToolKind.MetalHatchet, 1f, out _, out _), "a tool they do not have");
            Assert.IsFalse(world.Strike(pa.Id, pb.Id, ToolKind.Hammer, 1f, out _, out _), "the hammer builds");
            Assert.IsTrue(world.Strike(pa.Id, pb.Id, ToolKind.Spear, 1f, out _, out _));
            Assert.IsFalse(world.Strike(pa.Id, pb.Id, ToolKind.Spear, 1.2f, out _, out _), "faster than it swings");
            world.Players.Place(pb.Id, new Pose(pa.Pose.X + 9f, pa.Pose.Y, pa.Pose.Z, 0f), 2f);
            Assert.IsFalse(world.Strike(pa.Id, pb.Id, ToolKind.Spear, 3f, out _, out _), "out of reach");
            world.Players.Place(pb.Id, new Pose(pa.Pose.X + 2f, pa.Pose.Y, pa.Pose.Z, 0f), 3f);
            Assert.IsTrue(world.Strike(pa.Id, pb.Id, ToolKind.None, 4f, out float fist, out _), "bare hands always");
            Assert.AreEqual(Combat.MeleeDamage(ToolKind.None), fist);
            Assert.AreEqual(100f - Combat.MeleeDamage(ToolKind.Spear) - fist, b.Survivor.Vitals.Health, 1e-3f);
        }

        [Test]
        public void StrongerToolsHitHarderAndTheSpearIsCraftedFromWood()
        {
            Assert.Less(Combat.MeleeDamage(ToolKind.Rock), Combat.MeleeDamage(ToolKind.Hatchet));
            Assert.Less(Combat.MeleeDamage(ToolKind.Hatchet), Combat.MeleeDamage(ToolKind.MetalHatchet));
            Assert.Less(Combat.MeleeDamage(ToolKind.MetalPickaxe), Combat.MeleeDamage(ToolKind.Spear));
            Assert.Greater(Combat.MeleeReach(ToolKind.Spear), Combat.MeleeReach(ToolKind.Rock));
            var recipes = RecipeBook.CreateDefault(ItemCatalog.CreateDefault());
            Assert.IsTrue(recipes.TryGet("wooden_spear", out var spear));
            Assert.AreEqual(ItemIds.WoodenSpear, spear.OutputItemId);
        }

        [Test]
        public void HurtAndStrikeRoundTrip()
        {
            var w = new NetWriter();
            Messages.Strike(w, 513, (byte)ToolKind.Spear);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual((byte)MessageType.Strike, r.Byte());
            Messages.ReadStrike(r, out ushort target, out byte tool);
            Assert.AreEqual(513, target);
            Assert.AreEqual((byte)ToolKind.Spear, tool);

            Messages.Hurt(w, 7, 9, 33.6f, true);
            r = new NetReader(w.ToArray());
            Assert.AreEqual((byte)MessageType.Hurt, r.Byte());
            Messages.ReadHurt(r, out ushort t, out ushort a, out int damage, out bool killed);
            Assert.AreEqual(7, t);
            Assert.AreEqual(9, a);
            Assert.AreEqual(34, damage);
            Assert.IsTrue(killed);
        }
    }
}
