using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Survival;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>The shotgun trap: fires shells at strangers in front of it, spares its own, and stops when empty.</summary>
    public class GunTrapTests
    {
        private const int Seed = 20261114;
        private static readonly string[] Tokens = { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" };
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        [Test]
        public void It_covers_what_is_in_front_of_it_and_near()
        {
            var trap = new Deployable(1, DeployableKind.ShotgunTrap, 0f, 0f, 0f, 0f, 1);
            Assert.IsTrue(GunTraps.Covers(trap, 0f, 0f, 5f), "straight ahead");
            Assert.IsTrue(GunTraps.Covers(trap, 3f, 0.5f, 4f), "off to the side, within the cone");
            Assert.IsFalse(GunTraps.Covers(trap, 0f, 0f, -3f), "behind");
            Assert.IsFalse(GunTraps.Covers(trap, 5f, 0f, 0.5f), "beside it");
            Assert.IsFalse(GunTraps.Covers(trap, 0f, 0f, GunTraps.Range + 0.5f), "too far");
            var east = new Deployable(1, DeployableKind.ShotgunTrap, 0f, 0f, 0f, 90f, 1);
            Assert.IsTrue(GunTraps.Covers(east, 5f, 0f, 0f), "turned, it covers where it points");
            Assert.Greater(GunTraps.DamageAt(2f), GunTraps.DamageAt(7f));
            Assert.AreEqual(0f, GunTraps.DamageAt(GunTraps.Range + 1f));
        }

        [Test]
        public void It_spares_its_owner_their_team_and_those_on_the_cupboard()
        {
            var settlement = new Settlement(new IslandGround(Seed), Catalog);
            var trap = settlement.Add(new Deployable(0, DeployableKind.ShotgunTrap, 0f, 1f, 0f, 0f, 7));
            Assert.IsTrue(GunTraps.Spares(settlement, trap, 7, null));
            Assert.IsFalse(GunTraps.Spares(settlement, trap, 8, null));
            Assert.IsTrue(GunTraps.Spares(settlement, trap, 8, (a, b) => true), "a teammate");
            var cupboard = settlement.Add(new Deployable(0, DeployableKind.ToolCupboard, 2f, 1f, 2f, 0f, 7));
            Assert.IsFalse(GunTraps.Spares(settlement, trap, 9, null));
            settlement.Authorize(cupboard.Id, 9);
            Assert.IsTrue(GunTraps.Spares(settlement, trap, 9, null), "authorized on the cupboard it stands by");
        }

        [Test]
        public void It_is_loaded_with_shells_only()
        {
            var settlement = new Settlement(new IslandGround(Seed), Catalog);
            var trap = settlement.Add(new Deployable(0, DeployableKind.ShotgunTrap, 0f, 1f, 0f, 0f, 1));
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.Shell, 10);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(settlement, s, 1, trap.Id, trap.X + 1f, trap.Y, trap.Z));
            int rock = Enumerable.Range(0, s.Inventory.SlotCount).First(i => s.Inventory[i].ItemId == ItemIds.Rock);
            int shells = Enumerable.Range(0, s.Inventory.SlotCount).First(i => s.Inventory[i].ItemId == ItemIds.Shell);
            Assert.AreEqual(ActionResult.NotShells, PlayerActions.Put(settlement, s, rock, -1));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Put(settlement, s, shells, -1));
            Assert.AreEqual(10, settlement.Container(trap.Id).Count(ItemIds.Shell));
        }

        [Test]
        public void On_the_server_it_fires_at_a_stranger_spends_a_shell_and_waits()
        {
            var world = new ServerWorld(Seed);
            var owner = world.Join(Protocol.Version, "Ana", Tokens[0], 0f, out var ana, out _, out _);
            var stranger = world.Join(Protocol.Version, "Beto", Tokens[1], 0f, out var beto, out _, out _);
            var at = ana.Pose;
            var trap = world.Settlement.Add(new Deployable(0, DeployableKind.ShotgunTrap, at.X, at.Y, at.Z, 0f, owner.Owner));
            world.Settlement.Container(trap.Id).Add(ItemIds.Shell, 2);
            // Ana stands behind her trap; Beto walks up in front of it.
            world.Players.Place(ana.Id, new Pose(at.X, at.Y, at.Z - 2f, 0f), 0f);
            float y = world.Ground.Height(at.X, at.Z + 3f);
            world.Players.Place(beto.Id, new Pose(at.X, y, at.Z + 3f, 180f), 0f);

            world.Tick(0.05f);
            Assert.AreEqual(1, world.TrapShots.Count, "it fired");
            Assert.AreEqual(beto.Id, world.TrapShots[0].Target);
            Assert.Less(stranger.Survivor.Vitals.Health, Vitals.MaxHealth);
            Assert.AreEqual(Vitals.MaxHealth, owner.Survivor.Vitals.Health, "not at its owner");
            Assert.AreEqual(1, world.Settlement.Container(trap.Id).Count(ItemIds.Shell));

            world.TrapShots.Clear();
            world.Tick(0.5f);
            Assert.IsEmpty(world.TrapShots, "it waits between shots");
            world.Tick(GunTraps.Interval);
            Assert.AreEqual(1, world.TrapShots.Count);
            Assert.AreEqual(0, world.Settlement.Container(trap.Id).Count(ItemIds.Shell));

            world.TrapShots.Clear();
            world.Tick(GunTraps.Interval + 0.1f);
            Assert.IsEmpty(world.TrapShots, "empty, it is only a box");
        }
    }
}
