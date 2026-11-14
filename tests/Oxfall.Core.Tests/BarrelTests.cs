using System;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    public class BarrelTests
    {
        private const int Seed = 20261113;

        [Test]
        public void ABarrelBreaksOpenOnTheThirdBlowAndGivesItsLoot()
        {
            var barrel = new ResourceNode(1, ResourceNodeType.Barrel);
            var random = new Random(3);
            Assert.IsEmpty(barrel.Hit(ToolKind.None, random), "not with bare hands");
            Assert.IsEmpty(barrel.Hit(ToolKind.Torch, random), "nor a torch");
            Assert.AreEqual(3, barrel.Remaining);
            Assert.IsEmpty(barrel.Hit(ToolKind.Rock, random), "one");
            Assert.IsEmpty(barrel.Hit(ToolKind.Spear, random), "two");
            var loot = barrel.Hit(ToolKind.Rock, random);
            Assert.IsTrue(barrel.IsDepleted, "open");
            Assert.IsTrue(loot.Any(y => y.ItemId == ItemIds.MetalFragments && y.Amount >= 10), "always some metal");
            Assert.IsEmpty(barrel.Hit(ToolKind.Rock, random), "an open barrel is empty");
            Assert.AreEqual(600f, barrel.RespawnTimer, 1e-3f, "another stands there in ten minutes");

            var other = new ResourceNode(2, ResourceNodeType.Barrel);
            Assert.IsEmpty(other.Hit(ToolKind.MetalHatchet, random));
            Assert.IsNotEmpty(other.Hit(ToolKind.MetalPickaxe, random), "metal tools in two");
        }

        [Test]
        public void BarrelLootIsUsefulAndVaried()
        {
            var random = new Random(11);
            int cloth = 0, arrows = 0, finds = 0;
            for (int i = 0; i < 400; i++)
            {
                var loot = LootTable.Barrel(random);
                Assert.That(loot[0].Amount, Is.InRange(10, 25));
                cloth += loot.Count(y => y.ItemId == ItemIds.Cloth);
                arrows += loot.Count(y => y.ItemId == ItemIds.Arrow);
                finds += loot.Count(y => y.ItemId == ItemIds.StoneHatchet || y.ItemId == ItemIds.StonePickaxe
                                         || y.ItemId == ItemIds.WoodenSpear || y.ItemId == ItemIds.WoodenBow);
            }
            Assert.That(cloth, Is.InRange(180, 260), "about half have cloth");
            Assert.That(arrows, Is.InRange(100, 180), "about a third have arrows");
            Assert.That(finds, Is.InRange(25, 75), "about one in eight hides a tool or weapon");
        }

        [Test]
        public void BarrelsStandOnOpenGroundWithoutMovingAnythingElse()
        {
            var ground = new IslandGround(Seed);
            var spawns = ResourceLayout.Generate(ground);
            var barrels = spawns.Where(s => s.Type == ResourceNodeType.Barrel).ToList();
            Assert.That(barrels.Count, Is.InRange(20, 140), "a few dozen across the island");
            Assert.AreEqual(spawns.Count, spawns.Select(s => s.Id).Distinct().Count(), "one thing per cell");
        }

        [Test]
        public void OnTheServerTheBreakerGetsTheLoot()
        {
            var world = new ServerWorld(Seed);
            var record = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var player, out _, out _);
            var spawn = ResourceLayout.Generate(new IslandGround(Seed)).First(s => s.Type == ResourceNodeType.Barrel);
            world.Players.Place(player.Id, new Pose(spawn.X + 1.5f, spawn.Y, spawn.Z, 0f), 0f);
            int metalBefore = record.Survivor.Inventory.Count(ItemIds.MetalFragments);
            float now = 1f;
            for (int i = 0; i < 2; i++, now += 1f)
                Assert.IsEmpty(world.Hit(player.Id, spawn.Id, ToolKind.Rock, now, out _), "not open yet");
            var got = world.Hit(player.Id, spawn.Id, ToolKind.Rock, now, out int lost);
            Assert.IsNotEmpty(got);
            Assert.AreEqual(0, lost);
            Assert.Greater(record.Survivor.Inventory.Count(ItemIds.MetalFragments), metalBefore);
            Assert.IsTrue(world.TryGetNode(spawn.Id, out var node, out _, out _) && node.IsDepleted);
        }
    }
}
