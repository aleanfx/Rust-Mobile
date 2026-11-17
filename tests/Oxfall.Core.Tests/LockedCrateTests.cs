using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>The locked crate: turns up in an outpost, hacked over minutes, opens on its own, wakes the guards, rots away.</summary>
    public class LockedCrateTests
    {
        private const int Seed = 20261114;
        private static readonly string[] Tokens = { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" };
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        private static (Settlement, Deployable, Survivor) Crate()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog) { DropsLoot = true };
            var c = w.PlaceLockedCrate(0f, 1f, 0f, 0f, LootTable.LockedCrate(new System.Random(1)));
            var s = new Survivor(w.Catalog);
            s.Inventory.Clear();
            return (w, c, s);
        }

        [Test]
        public void It_holds_the_best_loot_and_nobody_makes_picks_up_or_breaks_it()
        {
            var loot = LootTable.LockedCrate(new System.Random(7));
            Assert.IsTrue(loot.Any(y => y.ItemId == ItemIds.SemiRifle));
            Assert.GreaterOrEqual(loot.First(y => y.ItemId == ItemIds.RifleBullet).Amount, 60);
            Assert.AreEqual(9, loot.Count);
            Assert.IsNull(Deployables.ItemOf(DeployableKind.LockedCrate));
            Assert.AreEqual(0f, Deployables.MaxHealth(DeployableKind.LockedCrate));
            Assert.IsTrue(Deployables.IsLoot(DeployableKind.LockedCrate));
            Assert.AreEqual(12, Deployables.Slots(DeployableKind.LockedCrate));
        }

        [Test]
        public void It_stays_shut_until_hacked_then_opens_on_its_own_when_the_time_is_up()
        {
            var (w, c, s) = Crate();
            Assert.AreEqual(ActionResult.CrateLocked, PlayerActions.Open(w, s, 1, c.Id, 1f, 1f, 0f));
            Assert.AreEqual(ActionResult.TooFar, PlayerActions.Build(w, s, 1, BuildOp.Hack(c.Id), 20f, 1f, 0f, out _));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 1, BuildOp.Hack(c.Id), 1f, 1f, 0f, out var done));
            Assert.AreEqual(BuildOpKind.Hack, done.Kind);
            Assert.AreEqual(LockedCrates.HackSeconds, done.Top);
            Assert.AreEqual(ActionResult.AlreadyHacked, PlayerActions.Build(w, s, 2, BuildOp.Hack(c.Id), 1f, 1f, 0f, out _));
            Assert.AreEqual(ActionResult.CrateLocked, PlayerActions.Open(w, s, 1, c.Id, 1f, 1f, 0f));

            var opened = new List<BuildOp>();
            for (int i = 0; i < 299; i++) w.Tick(1f, decayed: opened);
            Assert.IsFalse(w.IsCrateOpen(c.Id));
            Assert.AreEqual(1f, w.HackLeft(c.Id), 0.01f);
            Assert.IsEmpty(opened);
            w.Tick(1.5f, decayed: opened);
            Assert.IsTrue(w.IsCrateOpen(c.Id));
            Assert.AreEqual(1, opened.Count(o => o.Kind == BuildOpKind.Hack && o.Top == 0f));

            // Anyone standing there takes it; emptied, it is gone.
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, s, 2, c.Id, 1f, 1f, 0f));
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Put(w, s, 0, -1));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.TakeAll(w, s));
            Assert.Greater(s.Inventory.Count(ItemIds.SemiRifle), 0);
            Assert.IsFalse(w.TryGet(c.Id, out _));
        }

        [Test]
        public void Left_alone_it_is_taken_away_hacked_it_waits_and_open_it_rots()
        {
            var (w, c, s) = Crate();
            var rotted = new List<int>();
            w.Tick(LockedCrates.UnhackedSeconds - 1f, rotted: rotted);
            Assert.IsTrue(w.TryGet(c.Id, out _));
            PlayerActions.Build(w, s, 1, BuildOp.Hack(c.Id), 1f, 1f, 0f, out _);
            // While the hack runs it does not rot.
            w.Tick(LockedCrates.HackSeconds - 1f, rotted: rotted);
            Assert.IsTrue(w.TryGet(c.Id, out _));
            w.Tick(2f, rotted: rotted);
            Assert.IsTrue(w.IsCrateOpen(c.Id));
            w.Tick(LockedCrates.OpenSeconds - 10f, rotted: rotted);
            Assert.IsTrue(w.TryGet(c.Id, out _));
            w.Tick(20f, rotted: rotted);
            Assert.IsFalse(w.TryGet(c.Id, out _));
            Assert.AreEqual(new[] { c.Id }, rotted);

            var (w2, c2, _) = Crate();
            w2.Tick(LockedCrates.UnhackedSeconds + 1f, rotted: rotted);
            Assert.IsFalse(w2.TryGet(c2.Id, out _), "nobody came: taken away");
        }

        [Test]
        public void A_copy_follows_the_hack_and_a_player_who_joins_late_sees_it()
        {
            var (w, c, s) = Crate();
            var copy = new Settlement(w.Ground, w.Catalog);
            foreach (var op in BuildOps.Describe(w)) BuildOps.Apply(copy, op);
            Assert.IsTrue(copy.TryGet(c.Id, out var seen));
            Assert.AreEqual(DeployableKind.LockedCrate, seen.Kind);
            Assert.IsFalse(copy.IsHacked(c.Id));

            PlayerActions.Build(w, s, 1, BuildOp.Hack(c.Id), 1f, 1f, 0f, out var done);
            // On the wire and back.
            var wr = new NetWriter();
            done.Write(wr);
            var back = BuildOp.Read(new NetReader(wr.ToArray()));
            Assert.IsTrue(BuildOps.Apply(copy, back));
            Assert.AreEqual(LockedCrates.HackSeconds, copy.HackLeft(c.Id));

            w.Tick(100f);
            var late = new Settlement(w.Ground, w.Catalog);
            foreach (var op in BuildOps.Describe(w)) BuildOps.Apply(late, op);
            Assert.AreEqual(LockedCrates.HackSeconds - 100f, late.HackLeft(c.Id), 0.01f);

            // A copy counts down on its own but opens only when told.
            copy.CountDownHacks(LockedCrates.HackSeconds + 5f);
            Assert.IsFalse(copy.IsCrateOpen(c.Id));
            Assert.Less(copy.HackLeft(c.Id), 0.1f);

            // Told it opened, a copy opens (and a late word of the hack does not lock it again).
            BuildOps.Apply(copy, BuildOp.Hack(c.Id, 0f));
            Assert.IsTrue(copy.IsCrateOpen(c.Id));
            BuildOps.Apply(copy, BuildOp.Hack(c.Id, 50f));
            Assert.IsTrue(copy.IsCrateOpen(c.Id));
        }

        [Test]
        public void It_turns_up_in_an_outpost_some_minutes_in_and_never_two_at_once()
        {
            var ground = new IslandGround(Seed);
            var crates = new LockedCrates(Seed);
            Assert.IsFalse(crates.Due(LockedCrates.FirstAfter - 1f, false));
            Assert.IsTrue(crates.Due(LockedCrates.FirstAfter, false));
            Assert.IsFalse(crates.Due(LockedCrates.FirstAfter + 1f, false));
            // While one lies there no other comes, and after it goes a while passes.
            float later = LockedCrates.FirstAfter + LockedCrates.Every + LockedCrates.Jitter + 10f;
            Assert.IsFalse(crates.Due(later, true));
            Assert.IsFalse(crates.Due(later + 1f, false));
            Assert.IsTrue(crates.Due(later + LockedCrates.Respite, false));

            Assert.IsTrue(crates.PickSpot(ground, out float x, out _, out float z, out _));
            var outposts = Monuments.Find(ground).Where(m => m.Kind == MonumentKind.Outpost).ToList();
            Assert.IsNotEmpty(outposts);
            Assert.IsTrue(outposts.Any(m => (m.X - x) * (m.X - x) + (m.Z - z) * (m.Z - z) < Monuments.Radius * Monuments.Radius));
            Assert.IsNotNull(LockedCrates.PlaceName(ground, x, z));
            // Not inside a wall, and clear of the ruin's crates and barrels.
            Assert.IsFalse(Monuments.Solid(ground, x, z, Deployables.Radius(DeployableKind.LockedCrate)));
            var nodes = new List<NodeSpawn>();
            Monuments.AddNodes(ground, nodes);
            foreach (var n in nodes)
                Assert.Greater((n.X - x) * (n.X - x) + (n.Z - z) * (n.Z - z), 1.6f * 1.6f, n.Type.ToString());
            Assert.AreEqual("4:59", LockedCrates.Clock(298.2f));
            Assert.AreEqual("0:00", LockedCrates.Clock(0f));
        }

        [Test]
        public void On_the_server_hacking_wakes_the_guards_and_everyone_hears_it_open()
        {
            var world = new ServerWorld(Seed) { DevMode = true };
            world.Join(Protocol.Version, "Ana", Tokens[0], 0f, out var ana, out _, out _);
            var crate = world.SpawnLockedCrate();
            Assert.IsTrue(crate.HasValue);
            var c = crate.Value;
            Assert.IsTrue(world.LockedCratePresent);
            Assert.IsTrue(world.Announcements.Any(o => o.Kind == BuildOpKind.Deploy && o.Deployed.Kind == DeployableKind.LockedCrate));
            world.Announcements.Clear();

            world.Players.Place(ana.Id, new Pose(c.X + 1.5f, c.Y, c.Z, 270f), 0f);
            var guards = world.Fauna.All.Where(a => a.Kind == AnimalKind.Bandit && a.Guard
                && (a.X - c.X) * (a.X - c.X) + (a.Z - c.Z) * (a.Z - c.Z) < LockedCrates.AlarmRadius * LockedCrates.AlarmRadius).ToList();
            Assert.IsNotEmpty(guards);
            foreach (var g in guards) g.Angry = false;
            Assert.AreEqual(ActionResult.Ok, world.Build(ana.Id, BuildOp.Hack(c.Id), out var done));
            Assert.AreEqual(LockedCrates.HackSeconds, done.Top);
            Assert.IsTrue(guards.All(g => g.State == AnimalState.Chase && g.Angry), "the guards come for her");

            // The test command brings the hack to its last seconds; then it opens for everyone.
            Assert.AreEqual(ActionResult.Ok, world.Act(ana.Id, ActKind.DevCrate, 2, 0, null));
            Assert.AreEqual(2f, world.Settlement.HackLeft(c.Id), 0.01f);
            world.Announcements.Clear();
            for (int i = 0; i < 50; i++) world.Tick(0.05f);
            Assert.IsTrue(world.Settlement.IsCrateOpen(c.Id));
            Assert.IsTrue(world.Announcements.Any(o => o.Kind == BuildOpKind.Hack && o.Id == c.Id && o.Top == 0f));
        }

        [Test]
        public void The_island_gets_its_first_crate_on_schedule()
        {
            var world = new ServerWorld(Seed);
            Assert.IsFalse(world.LockedCratePresent);
            for (float t = 0f; t < LockedCrates.FirstAfter + 2f; t += 10f) world.Tick(10f);
            Assert.IsTrue(world.LockedCratePresent);
            Assert.AreEqual(1, world.Settlement.Deployables.Count(d => d.Kind == DeployableKind.LockedCrate));
        }
    }
}
