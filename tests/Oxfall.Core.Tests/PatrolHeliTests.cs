using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Survival;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>The patrol helicopter: comes now and then, hunts whoever is in the open, can be shot down, drops hot crates.</summary>
    public class PatrolHeliTests
    {
        private const int Seed = 20261114;
        private static readonly string[] Tokens = { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" };

        private static (IslandGround, PatrolHelis) Make()
        {
            var ground = new IslandGround(Seed);
            return (ground, new PatrolHelis(ground, Seed));
        }

        /// <summary>Somewhere open and dry on the island.</summary>
        private static Fauna.Prey Open(IslandGround ground, int key = 1)
        {
            for (float x = -60f; x < 60f; x += 7f)
                for (float z = -60f; z < 60f; z += 7f)
                    if (ground.Height(x, z) > 3f && !Monuments.NoBuilding(ground, x, z))
                        return new Fauna.Prey(key, x, ground.Height(x, z), z);
            Assert.Fail("no open ground");
            return default;
        }

        private static void Run(PatrolHelis helis, ref float now, float seconds, List<Fauna.Prey> players, List<HeliShot> shots, List<PatrolHeli> crashed = null)
        {
            for (float t = 0f; t < seconds; t += 0.05f)
            {
                now += 0.05f;
                helis.Tick(now, 0.05f, players, shots, null, crashed);
            }
        }

        [Test]
        public void It_comes_on_schedule_only_with_someone_about_and_never_two_at_once()
        {
            var (ground, helis) = Make();
            var started = new List<PatrolHeli>();
            var none = new List<Fauna.Prey>();
            helis.Tick(PatrolHelis.FirstAfter + 1f, 1f, none, null, started);
            Assert.IsEmpty(started, "nobody on the island: nothing to patrol for");
            var players = new List<Fauna.Prey> { new Fauna.Prey(1, 0f, -5f, IslandGround.HalfSize * 2f) };
            helis.Tick(PatrolHelis.FirstAfter + 30f, 1f, players, null, started);
            Assert.IsEmpty(started, "put off a minute");
            helis.Tick(PatrolHelis.FirstAfter + 62f, 1f, players, null, started);
            Assert.AreEqual(1, started.Count);
            var h = started[0];
            Assert.AreEqual(PatrolHelis.MaxHealth, h.Health);
            Assert.Greater(Math.Sqrt(h.X * h.X + h.Z * h.Z), IslandGround.IslandRadius, "comes in over the sea");
            helis.Tick(PatrolHelis.FirstAfter + PatrolHelis.Every * 3f, 1f, players, null, started);
            Assert.AreEqual(1, started.Count, "not while one is about");
        }

        [Test]
        public void It_patrols_the_ruins_well_above_the_ground_and_leaves_when_its_time_is_up()
        {
            var (ground, helis) = Make();
            var h = helis.Launch(0f);
            float now = 0f;
            var players = new List<Fauna.Prey>();
            float closest = float.MaxValue;
            var ruins = Monuments.Find(ground);
            for (int i = 0; i < 4000; i++)
            {
                now += 0.1f;
                helis.Tick(now, 0.1f, players, null);
                Assert.Greater(h.Y, Math.Max(ground.Height(h.X, h.Z), 0f) + PatrolHelis.MinClearance * 0.5f, "never low");
                foreach (var m in ruins) closest = Math.Min(closest, (float)Math.Sqrt((m.X - h.X) * (m.X - h.X) + (m.Z - h.Z) * (m.Z - h.Z)));
            }
            Assert.AreEqual(HeliState.Patrol, h.State);
            Assert.Less(closest, PatrolHelis.CircleRadius + 8f, "it circles a ruin");
            for (int i = 0; i < 9000 && helis.Count > 0; i++)
            {
                now += 0.1f;
                helis.Tick(now, 0.1f, players, null);
            }
            Assert.AreEqual(0, helis.Count, "gone off the island once its time is up");
        }

        [Test]
        public void Someone_in_the_open_is_circled_and_shot_at_hard_but_not_every_round_hits()
        {
            var (ground, helis) = Make();
            var me = Open(ground);
            var players = new List<Fauna.Prey> { me };
            var h = helis.Launch(0f, me.X, me.Z, 120f);
            var shots = new List<HeliShot>();
            float now = 0f;
            Run(helis, ref now, 40f, players, shots);
            Assert.AreEqual(HeliState.Engage, h.State);
            Assert.AreEqual(1, h.Target);
            var rounds = shots.Where(s => s.Kind == HeliShotKind.Bullet).ToList();
            Assert.Greater(rounds.Count, 40, "bursts of its gun");
            int hits = rounds.Count(s => s.Target == 1);
            Assert.Greater(hits, rounds.Count / 10);
            Assert.Less(hits, rounds.Count / 2, "most rounds miss");
            Assert.IsTrue(rounds.All(s => s.Target == 0 || s.Damage == PatrolHelis.BulletDamage));
            Assert.IsTrue(shots.Any(s => s.Kind == HeliShotKind.Rocket), "a volley of rockets");
            Assert.IsTrue(shots.Any(s => s.Kind == HeliShotKind.Splash && s.Target == 1 && s.Damage > 0f), "some burst near enough to hurt");
            // Circling, never right on top.
            float d = (float)Math.Sqrt((h.X - me.X) * (h.X - me.X) + (h.Z - me.Z) * (h.Z - me.Z));
            Assert.That(d, Is.InRange(PatrolHelis.OrbitRadius * 0.5f, PatrolHelis.OrbitRadius * 1.6f));
            Assert.Greater(h.Y, me.Y + PatrolHelis.EngageHeight * 0.6f);
        }

        [Test]
        public void Walls_and_roofs_hide_you_and_once_lost_it_searches_then_goes_back_to_its_round()
        {
            var (ground, helis) = Make();
            var me = Open(ground);
            bool hidden = true;
            helis.Shielded = (x0, y0, z0, x1, y1, z1) => hidden;
            var players = new List<Fauna.Prey> { me };
            var h = helis.Launch(0f, me.X, me.Z, 100f);
            var shots = new List<HeliShot>();
            float now = 0f;
            Run(helis, ref now, 30f, players, shots);
            Assert.AreEqual(HeliState.Patrol, h.State, "never saw her");
            Assert.IsEmpty(shots);

            hidden = false;
            helis.Launch(now, me.X, me.Z, 60f);
            Run(helis, ref now, 20f, players, shots);
            var hunter = helis.All.First(x => x.State == HeliState.Engage);
            hidden = true;
            shots.Clear();
            Run(helis, ref now, PatrolHelis.LoseSeconds + 1f, players, shots);
            Assert.AreEqual(HeliState.Search, hunter.State);
            Assert.IsFalse(shots.Any(s => s.Kind == HeliShotKind.Bullet && s.Target == 1), "no round reaches her behind a wall");
            Run(helis, ref now, PatrolHelis.SearchSeconds + 1f, players, shots);
            Assert.AreEqual(HeliState.Patrol, hunter.State);
            Assert.AreEqual(0, hunter.Target);
        }

        [Test]
        public void Shooting_it_turns_it_on_you_from_afar_and_its_rotors_are_the_weak_spot()
        {
            var (ground, helis) = Make();
            var h = helis.Launch(0f);
            float now = 0f;
            Run(helis, ref now, 2f, new List<Fauna.Prey>(), null);
            Assert.AreEqual(HeliState.Patrol, h.State);
            Assert.IsFalse(helis.Hit(h.Id, 100f, false, 7, 300f, 0f, 300f, now));
            Assert.AreEqual(PatrolHelis.MaxHealth - 100f, h.Health);
            Assert.AreEqual(HeliState.Engage, h.State, "turns on whoever fired, however far");
            Assert.AreEqual(7, h.Target);
            helis.Hit(h.Id, 100f, true, 7, 300f, 0f, 300f, now);
            Assert.AreEqual(PatrolHelis.MaxHealth - 100f - 100f * PatrolHelis.RotorMultiplier, h.Health);
            Assert.IsFalse(h.Smoking);
            helis.Hit(h.Id, 700f, false, 7, 300f, 0f, 300f, now);
            Assert.IsTrue(h.Smoking, "smoking once badly hurt");
        }

        [Test]
        public void Shot_down_it_spins_to_the_ground_and_burns_there_for_a_while()
        {
            var (ground, helis) = Make();
            var me = Open(ground);
            var h = helis.Launch(0f, me.X, me.Z, 40f);
            float now = 0f;
            var players = new List<Fauna.Prey> { me };
            Run(helis, ref now, 3f, players, null);
            Assert.IsTrue(helis.Hit(h.Id, PatrolHelis.MaxHealth / PatrolHelis.RotorMultiplier + 1f, true, 1, me.X, me.Y, me.Z, now));
            Assert.AreEqual(HeliState.Crashing, h.State);
            Assert.IsFalse(helis.Flying);
            Assert.IsFalse(helis.Hit(h.Id, 50f, false, 1, 0f, 0f, 0f, now), "nothing more to shoot");
            var crashed = new List<PatrolHeli>();
            var shots = new List<HeliShot>();
            Run(helis, ref now, 16f, players, shots, crashed);
            Assert.AreEqual(1, crashed.Count);
            Assert.AreEqual(HeliState.Wreck, h.State);
            Assert.AreEqual(Math.Max(ground.Height(h.X, h.Z), 0f), h.Y, 0.01f);
            Assert.IsFalse(shots.Any(s => s.Kind != HeliShotKind.Splash), "it fires no more once hit for good");
            Run(helis, ref now, PatrolHelis.WreckSeconds, players, null);
            Assert.AreEqual(0, helis.Count, "the wreck burns out");
        }

        [Test]
        public void A_rocket_bursting_hurts_whoever_is_close_unless_something_shields_them()
        {
            Assert.AreEqual(PatrolHelis.SplashDamage, PatrolHelis.Splash(0f), 1e-3f);
            Assert.Greater(PatrolHelis.Splash(3f), 0f);
            Assert.AreEqual(0f, PatrolHelis.Splash(PatrolHelis.SplashRadius));
            Assert.Greater(PatrolHelis.HitChance(20f), PatrolHelis.HitChance(100f));
            Assert.AreEqual(PatrolHelis.HitChanceFar, PatrolHelis.HitChance(500f), 1e-4f);
        }

        [Test]
        public void On_the_server_it_hurts_players_its_wreck_drops_hot_crates_and_guns_can_bring_it_down()
        {
            var world = new ServerWorld(Seed) { DevMode = true };
            var ana = world.Join(Protocol.Version, "Ana", Tokens[0], 0f, out var pa0, out _, out _);
            var pa = pa0;
            var spot = Open(world.Ground);
            world.Players.Place(pa.Id, new Pose(spot.X, spot.Y, spot.Z, 0f), 0f);
            Assert.AreEqual(ActionResult.Ok, world.Act(pa.Id, ActKind.DevHeli, 1, 0, null));
            Assert.AreEqual(1, world.Helis.Count);
            var h = world.Helis.All[0];
            float before = ana.Survivor.Vitals.Health;
            for (int i = 0; i < 20 * 25; i++) world.Tick(0.05f);
            Assert.IsTrue(world.HeliShots.Any(s => s.Shot.Kind == HeliShotKind.Bullet));
            Assert.Less(ana.Survivor.Vitals.Health, before, "its rounds hurt her");
            Assert.IsTrue(world.HeliShots.Where(s => s.Damage > 0f).All(s => s.Shot.Target == pa.Id));

            // Bea's rifle at it, from cover it cannot see through: a round in a rotor counts three times.
            world.Helis.Shielded = (x0, y0, z0, x1, y1, z1) => true;
            var bea = world.Join(Protocol.Version, "Bea", Tokens[1], world.Time, out pa, out _, out _);
            world.Players.Place(pa.Id, new Pose(spot.X, spot.Y, spot.Z, 0f), world.Time);
            world.HeliShots.Clear();
            bea.Survivor.Inventory.Clear();
            bea.Survivor.Inventory.Add(ItemIds.SemiRifle, 1);
            bea.Survivor.Inventory.Add(ItemIds.RifleBullet, 50);
            ana = bea;
            var p = pa.Pose;
            float ex = p.X, ey = p.Y + 1.6f, ez = p.Z;
            float yaw = (float)(Math.Atan2(h.X - ex, h.Z - ez) * 180.0 / Math.PI);
            float flat = (float)Math.Sqrt((h.X - ex) * (h.X - ex) + (h.Z - ez) * (h.Z - ez));
            float pitch = (float)(Math.Atan2(h.Y - 1f - ey, flat) * 180.0 / Math.PI);
            float now = world.Time;
            Assert.IsTrue(world.Fire(pa.Id, ToolKind.Rifle, ex, ey, ez, yaw, pitch, now));
            Assert.IsTrue(world.GunHit(pa.Id, ArrowTarget.Heli, h.Id, 1, now + 0.05f, out float damage, out bool killed, out _, headPellets: 1));
            Assert.IsFalse(killed);
            Assert.AreEqual(PatrolHelis.MaxHealth - damage, h.Health, 0.5f);
            Assert.Greater(damage, Firearms.Rifle.PelletDamage);
            // Aimed the other way it is not hit, whatever the phone says.
            Assert.IsTrue(world.Fire(pa.Id, ToolKind.Rifle, ex, ey, ez, yaw + 90f, pitch, now + 0.5f));
            Assert.IsFalse(world.GunHit(pa.Id, ArrowTarget.Heli, h.Id, 1, now + 0.55f, out _, out _, out _));

            // Brought down: it falls, and three crates lie round the wreck, too hot to open for a while.
            world.Announcements.Clear();
            Assert.AreEqual(ActionResult.Ok, world.Act(pa.Id, ActKind.DevHeli, 3, 0, null));
            for (int i = 0; i < 20 * 16 && h.State != HeliState.Wreck; i++) world.Tick(0.05f);
            Assert.AreEqual(HeliState.Wreck, h.State);
            var crates = world.Settlement.Deployables.Where(d => d.Kind == DeployableKind.HeliCrate).ToList();
            Assert.AreEqual(PatrolHelis.Crates, crates.Count);
            foreach (var c in crates)
            {
                Assert.IsFalse(world.Settlement.IsCrateOpen(c.Id), "too hot");
                Assert.IsTrue(world.Announcements.Any(o => o.Kind == BuildOpKind.Hack && o.Id == c.Id && o.Top == PatrolHelis.CrateBurnSeconds));
                Assert.IsTrue(world.Announcements.Any(o => o.Kind == BuildOpKind.Deploy && o.Deployed.Id == c.Id));
                Assert.Less(Math.Sqrt((c.X - h.X) * (c.X - h.X) + (c.Z - h.Z) * (c.Z - h.Z)), 7f);
                Assert.IsTrue(world.Settlement.Container(c.Id).Count(ItemIds.RifleBullet) >= 30);
            }
            Assert.AreEqual(ActionResult.CrateLocked, PlayerActions.Open(world.Settlement, ana.Survivor, ana.Owner, crates[0].Id, crates[0].X, crates[0].Y, crates[0].Z));
            for (int i = 0; i < 20 * (int)(PatrolHelis.CrateBurnSeconds + 2f); i++) world.Tick(0.05f);
            Assert.IsTrue(crates.All(c => world.Settlement.IsCrateOpen(c.Id)), "cooled down");
            // (Whoever comes for it: she may have met the wolves in two minutes out there.)
            var looter = new Survivor(world.Catalog);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(world.Settlement, looter, ana.Owner, crates[0].Id, crates[0].X, crates[0].Y, crates[0].Z));
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Build(world.Settlement, looter, ana.Owner, BuildOp.Hack(crates[1].Id), crates[1].X, crates[1].Y, crates[1].Z, out _), "nothing to hack");
        }

        [Test]
        public void Its_crates_hold_military_loot_and_nobody_makes_or_breaks_them()
        {
            var loot = LootTable.HeliCrate(new System.Random(3));
            Assert.AreEqual(6, loot.Count);
            Assert.IsTrue(loot.Any(y => y.ItemId == ItemIds.RifleBullet && y.Amount >= 30));
            Assert.IsNull(Deployables.ItemOf(DeployableKind.HeliCrate));
            Assert.IsTrue(Deployables.IsLoot(DeployableKind.HeliCrate));
            Assert.AreEqual(0f, Deployables.MaxHealth(DeployableKind.HeliCrate));
        }

        [Test]
        public void Helicopters_and_their_shots_go_over_the_wire_and_back()
        {
            var (ground, helis) = Make();
            var h = helis.Launch(0f, 10f, 20f, 50f);
            h.Pitch = 9.6f;
            h.Roll = -14.2f;
            helis.Hit(h.Id, 333.3f, false, 4, 0f, 0f, 0f, 1f);
            var w = new NetWriter();
            Messages.Helis(w, helis.All);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(MessageType.Helis, (MessageType)r.Byte());
            var snaps = new List<HeliSnap>();
            Messages.ReadHelis(r, snaps);
            Assert.AreEqual(1, snaps.Count);
            var s = snaps[0];
            Assert.AreEqual(h.Id, s.Id);
            Assert.AreEqual(h.X, s.X, 1e-4f);
            Assert.AreEqual(h.Yaw, s.Yaw, 0.01f);
            Assert.AreEqual(10f, s.Pitch);
            Assert.AreEqual(-14f, s.Roll);
            Assert.AreEqual(Math.Ceiling(h.Health), s.Health);
            Assert.AreEqual(HeliState.Engage, s.State);
            Assert.AreEqual(4, s.Target);

            var shots = new List<(HeliShot, float, bool)>
            {
                (new HeliShot(h.Id, HeliShotKind.Bullet, 1f, 2f, 3f, 4f, 5f, 6f, 9, 7f), 5.4f, false),
                (new HeliShot(h.Id, HeliShotKind.Rocket, 1f, 2f, 3f, 7f, 8f, 9f, 0, 0f), 0f, false),
                (new HeliShot(h.Id, HeliShotKind.Splash, 7f, 8f, 9f, 7f, 8f, 9f, 9, 40f), 33f, true),
            };
            Messages.HeliShots(w, shots, 1, 2);
            r = new NetReader(w.ToArray());
            Assert.AreEqual(MessageType.HeliShots, (MessageType)r.Byte());
            var back = new List<(HeliShot Shot, int Damage, bool Killed)>();
            Messages.ReadHeliShots(r, back);
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual(HeliShotKind.Rocket, back[0].Shot.Kind);
            Assert.AreEqual(7f, back[0].Shot.ToX);
            Assert.AreEqual(HeliShotKind.Splash, back[1].Shot.Kind);
            Assert.AreEqual(9, back[1].Shot.Target);
            Assert.AreEqual(33, back[1].Damage);
            Assert.IsTrue(back[1].Killed);
        }
    }
}
