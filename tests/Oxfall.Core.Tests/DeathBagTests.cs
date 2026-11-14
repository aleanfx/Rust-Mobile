using NUnit.Framework;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    public class DeathBagTests
    {
        private const int Seed = 20261113;

        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        private static (Settlement w, float x, float y, float z) Beach()
        {
            var ground = new IslandGround(Seed);
            var spawn = Spawns.Beach(ground, new System.Random(Seed));
            return (new Settlement(ground, Catalog), spawn.X, ground.Height(spawn.X, spawn.Z), spawn.Z);
        }

        [Test]
        public void DyingLeavesEverythingInABagWhereThePlayerFell()
        {
            var (w, x, y, z) = Beach();
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.Wood, 340);
            s.Inventory.Add(ItemIds.MetalFragments, 25);
            s.Vitals.Damage(1000f);

            Assert.IsTrue(PlayerActions.DropBag(w, s, 7, x, y, z, 30f, out var bag));
            Assert.AreEqual(DeployableKind.Bag, bag.Kind);
            Assert.AreEqual(7, bag.Owner);
            Assert.AreEqual(x, bag.X, 1e-4f);
            Assert.AreEqual(z, bag.Z, 1e-4f);
            Assert.IsTrue(s.Inventory.IsEmpty);
            var inside = w.Container(bag.Id);
            Assert.AreEqual(340, inside.Count(ItemIds.Wood));
            Assert.AreEqual(25, inside.Count(ItemIds.MetalFragments));
            Assert.AreEqual(1, inside.Count(ItemIds.Rock));
            Assert.IsFalse(inside[0].IsEmpty, "packed into the first slots");

            Assert.AreEqual(ActionResult.Ok, PlayerActions.Respawn(s));
            Assert.AreEqual(1, s.Inventory.Count(ItemIds.Rock), "a fresh start");
            Assert.AreEqual(0, s.Inventory.Count(ItemIds.Wood));

            var empty = new Survivor(Catalog);
            empty.Inventory.Clear();
            Assert.IsFalse(PlayerActions.DropBag(w, empty, 8, x, y, z, 0f, out _), "nothing carried, no bag");
        }

        [Test]
        public void ABagOnlyEmptiesAndIsGoneOnceEmpty()
        {
            var (w, x, y, z) = Beach();
            var dead = new Survivor(Catalog);
            dead.Inventory.Add(ItemIds.Stone, 50);
            PlayerActions.DropBag(w, dead, 1, x, y, z, 0f, out var bag);
            var looter = new Survivor(Catalog);

            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, looter, 2, bag.Id, x, y, z), "anyone can open it");
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Put(w, looter, 0, -1), "nothing goes in");
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Build(w, looter, 1, BuildOp.Pickup(bag.Id), x, y, z, out _), "not picked up");
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Build(w, looter, 1, BuildOp.Deploy(new Deployable(0, DeployableKind.Bag, x, y, z, 0f)), x, y, z, out _),
                "no item makes one");

            // Rock, torch, stone: taking the first leaves the rest packed at the top.
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Take(w, looter, 0, -1));
            Assert.IsFalse(w.Container(bag.Id)[0].IsEmpty);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.TakeAll(w, looter));
            Assert.IsFalse(w.TryGet(bag.Id, out _), "emptied, it is gone");
            Assert.AreEqual(0, looter.Looting);
            Assert.AreEqual(50, looter.Inventory.Count(ItemIds.Stone));
        }

        [Test]
        public void ABagLeftAloneRotsAway()
        {
            var (w, x, y, z) = Beach();
            var dead = new Survivor(Catalog);
            PlayerActions.DropBag(w, dead, 1, x, y, z, 0f, out var bag);
            var rotted = new System.Collections.Generic.List<int>();
            w.Tick(Deployables.DeathBagSeconds - 1f, null, rotted);
            Assert.IsTrue(w.TryGet(bag.Id, out _));
            Assert.AreEqual(1f, w.RotsIn(bag.Id), 1e-3f);
            w.Tick(2f, null, rotted);
            Assert.IsFalse(w.TryGet(bag.Id, out _));
            CollectionAssert.AreEqual(new[] { bag.Id }, rotted);
        }

        [Test]
        public void TheServerDropsTheBagAndTellsEveryone()
        {
            var world = new ServerWorld(Seed);
            var record = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var player, out _, out _);
            record.Survivor.Inventory.Add(ItemIds.Cloth, 12);
            world.Announcements.Clear();
            record.Survivor.Vitals.Damage(1000f);

            Assert.AreEqual(1, world.Announcements.Count);
            var op = world.Announcements[0];
            Assert.AreEqual(BuildOpKind.Deploy, op.Kind);
            Assert.AreEqual(DeployableKind.Bag, op.Deployed.Kind);
            Assert.AreEqual(player.Pose.X, op.Deployed.X, 1e-3f);
            int bagId = op.Deployed.Id;
            Assert.AreEqual(12, world.Settlement.Container(bagId).Count(ItemIds.Cloth));

            // Someone empties it: the bag is gone for everyone.
            var other = world.Join(Protocol.Version, "Bea", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 1f, out var b, out _, out _);
            world.Players.Place(b.Id, player.Pose, 1f);
            world.Announcements.Clear();
            Assert.AreEqual(ActionResult.Ok, world.Act(b.Id, ActKind.Open, bagId, 0, null));
            Assert.AreEqual(ActionResult.Ok, world.Act(b.Id, ActKind.TakeAll, 0, 0, null));
            Assert.AreEqual(1, world.Announcements.Count);
            Assert.AreEqual(BuildOpKind.Pickup, world.Announcements[0].Kind);
            Assert.AreEqual(12, other.Survivor.Inventory.Count(ItemIds.Cloth));

            // Waking up again starts afresh.
            Assert.AreEqual(ActionResult.Ok, world.Act(player.Id, ActKind.Respawn, 0, 0, null));
            Assert.AreEqual(0, record.Survivor.Inventory.Count(ItemIds.Cloth));
        }
    }
}
