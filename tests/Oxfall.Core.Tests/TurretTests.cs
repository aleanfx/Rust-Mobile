using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Survival;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>The automatic turret: a wide, far half circle; bullets only; strangers and hostile animals; waits to swing round.</summary>
    public class TurretTests
    {
        private const int Seed = 20261114;
        private static readonly string[] Tokens = { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" };
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        [Test]
        public void It_is_learned_at_the_workbench_takes_bullets_and_a_lock()
        {
            Assert.IsTrue(RecipeBook.CreateDefault(Catalog).TryGet("auto_turret", out var r));
            Assert.IsTrue(r.NeedsWorkbench);
            Assert.Greater(r.ScrapToLearn, 0);
            Assert.IsTrue(Deployables.TryKindOf(ItemIds.AutoTurret, out var kind));
            Assert.AreEqual(DeployableKind.AutoTurret, kind);
            Assert.IsTrue(CodeLocks.Lockable(DeployableKind.AutoTurret));
            Assert.IsTrue(Turrets.Takes(ItemIds.RifleBullet));
            Assert.IsFalse(Turrets.Takes(ItemIds.Shell));

            var w = new Settlement(new IslandGround(Seed), Catalog);
            var t = w.Add(new Deployable(0, DeployableKind.AutoTurret, 0f, 1f, 0f, 0f, 1));
            var s = new Survivor(Catalog);
            s.Inventory.Clear();
            s.Inventory.Add(ItemIds.Shell, 5);
            s.Inventory.Add(ItemIds.RifleBullet, 5);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, s, 1, t.Id, 1f, 1f, 0f));
            Assert.AreEqual(ActionResult.NotBullets, PlayerActions.Put(w, s, 0, -1));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Put(w, s, 1, -1));
        }

        [Test]
        public void It_covers_a_wide_half_circle_far_out()
        {
            var t = new Deployable(1, DeployableKind.AutoTurret, 0f, 0f, 0f, 0f, 1);
            Assert.IsTrue(Turrets.Covers(t, 0f, 0f, 25f));
            Assert.IsTrue(Turrets.Covers(t, 20f, 0f, 0.5f), "out to its side");
            Assert.IsFalse(Turrets.Covers(t, 0f, 0f, -8f), "behind");
            Assert.IsTrue(Turrets.Covers(t, 0f, 0f, -3f), "right behind it, it swings round (a wolf at its owner's back)");
            Assert.IsFalse(Turrets.Covers(t, 0f, 0f, Turrets.Range + 1f));
            Assert.Greater(Turrets.Range, GunTraps.Range * 3f);
        }

        [Test]
        public void On_the_server_it_swings_round_then_fires_at_a_stranger_and_spares_its_owner()
        {
            var world = new ServerWorld(Seed);
            world.Fauna.Clear();
            var owner = world.Join(Protocol.Version, "Ana", Tokens[0], 0f, out var ana, out _, out _);
            var stranger = world.Join(Protocol.Version, "Beto", Tokens[1], 0f, out var beto, out _, out _);
            var at = ana.Pose;
            var t = world.Settlement.Add(new Deployable(0, DeployableKind.AutoTurret, at.X, at.Y, at.Z, 0f, owner.Owner));
            world.Settlement.Container(t.Id).Add(ItemIds.RifleBullet, 3);
            world.Players.Place(ana.Id, new Pose(at.X, at.Y, at.Z + 2f, 0f), 0f);
            for (int i = 0; i < 10; i++) world.Tick(0.05f);
            Assert.IsEmpty(world.TurretShots, "without power it sleeps");
            float y0 = world.Ground.Height(at.X + 1f, at.Z + 4f);
            world.Players.Place(beto.Id, new Pose(at.X + 1f, y0, at.Z + 4f, 180f), 0f);
            for (int i = 0; i < 20; i++) world.Tick(0.05f);
            Assert.IsEmpty(world.TurretShots, "not even at a stranger");
            ElectricityTests.Power(world.Settlement, t);
            float y = world.Ground.Height(at.X + 1f, at.Z + 4f);
            world.Players.Place(beto.Id, new Pose(at.X + 1f, y, at.Z + 4f, 180f), 0f);

            world.Tick(0.05f);
            Assert.IsEmpty(world.TurretShots, "a moment to swing round");
            for (int i = 0; i < 20 && world.TurretShots.Count == 0; i++) world.Tick(0.05f);
            Assert.AreEqual(1, world.TurretShots.Count);
            var shot = world.TurretShots[0];
            Assert.AreEqual(TurretTarget.Player, shot.Kind);
            Assert.AreEqual(beto.Id, shot.Target);
            Assert.Less(stranger.Survivor.Vitals.Health, Vitals.MaxHealth);
            Assert.AreEqual(Vitals.MaxHealth, owner.Survivor.Vitals.Health);
            Assert.AreEqual(2, world.Settlement.Container(t.Id).Count(ItemIds.RifleBullet));
            world.TurretShots.Clear();
            for (int i = 0; i < 30; i++) world.Tick(0.1f);
            Assert.AreEqual(2, world.TurretShots.Count, "until it runs dry");
            Assert.AreEqual(0, world.Settlement.Container(t.Id).Count(ItemIds.RifleBullet));
        }

        [Test]
        public void It_shoots_a_wolf_but_leaves_a_deer_alone()
        {
            var world = new ServerWorld(Seed);
            world.Fauna.Clear();
            world.Join(Protocol.Version, "Ana", Tokens[0], 0f, out var ana, out _, out _);
            var at = ana.Pose;
            var owner = world.Records.First();
            var t = world.Settlement.Add(new Deployable(0, DeployableKind.AutoTurret, at.X, at.Y, at.Z, 0f, owner.Owner));
            world.Settlement.Container(t.Id).Add(ItemIds.RifleBullet, 50);
            var deer = world.Fauna.Add(AnimalKind.Deer, at.X - 1f, at.Z + 6f, 0f);
            world.Fauna.Clear();
            Assert.IsNull(Turrets.NearestAnimal(t, new[] { world.Fauna.Add(AnimalKind.Deer, at.X - 1f, at.Z + 6f, 0f) }, null, out _), "no deer");
            var wolf = world.Fauna.Add(AnimalKind.Wolf, at.X + 1f, at.Z + 6f, 0f);
            Assert.AreSame(wolf, Turrets.NearestAnimal(t, world.Fauna.All, null, out _));
        }
    }
}
