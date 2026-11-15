using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;

namespace Oxfall.Core.Tests
{
    public class GatheringTests
    {
        [Test]
        public void A_hatchet_beats_a_rock_on_trees()
        {
            Assert.That(GatherTable.PerHit(ResourceNodeType.Tree, ToolKind.Hatchet),
                Is.GreaterThan(GatherTable.PerHit(ResourceNodeType.Tree, ToolKind.Rock)));
        }

        [Test]
        public void A_pickaxe_beats_a_rock_on_stone_and_ore()
        {
            Assert.That(GatherTable.PerHit(ResourceNodeType.Stone, ToolKind.Pickaxe),
                Is.GreaterThan(GatherTable.PerHit(ResourceNodeType.Stone, ToolKind.Rock)));
            Assert.That(GatherTable.PerHit(ResourceNodeType.MetalOre, ToolKind.Pickaxe),
                Is.GreaterThan(GatherTable.PerHit(ResourceNodeType.MetalOre, ToolKind.Rock)));
        }

        [Test]
        public void The_weak_spot_doubles_a_blow_on_a_tree_and_adds_half_on_rock()
        {
            var tree = new ResourceNode(1, ResourceNodeType.Tree);
            var normal = tree.Hit(ToolKind.Hatchet);
            var weak = tree.Hit(ToolKind.Hatchet, null, weakSpot: true);
            Assert.That(weak[0].Amount, Is.EqualTo(normal[0].Amount * 2));

            var ore = new ResourceNode(2, ResourceNodeType.MetalOre);
            int plain = ore.Hit(ToolKind.Pickaxe)[0].Amount;
            Assert.That(ore.Hit(ToolKind.Pickaxe, null, true)[0].Amount, Is.EqualTo((int)System.Math.Round(plain * 1.5)));

            Assert.That(GatherTable.HasWeakSpot(ResourceNodeType.Hemp), Is.False);
            var hemp = new ResourceNode(3, ResourceNodeType.Hemp);
            Assert.That(hemp.Hit(ToolKind.None, null, true)[0].Amount, Is.EqualTo(GatherTable.Capacity(ResourceNodeType.Hemp)), "nothing to find in a plant");
            Assert.That(new ResourceNode(4, ResourceNodeType.Tree).Hit(ToolKind.Torch, null, true), Is.Empty, "a torch still gathers nothing");
        }

        [Test]
        public void A_torch_gathers_nothing()
        {
            var tree = new ResourceNode(1, ResourceNodeType.Tree);
            Assert.That(tree.Hit(ToolKind.Torch), Is.Empty);
            Assert.That(tree.Remaining, Is.EqualTo(tree.Capacity));
        }

        [Test]
        public void Hitting_a_tree_until_it_falls_gives_exactly_its_capacity()
        {
            var tree = new ResourceNode(1, ResourceNodeType.Tree);
            int wood = 0;
            int hits = 0;
            while (!tree.IsDepleted && hits < 1000)
            {
                wood += tree.Hit(ToolKind.Rock).Where(y => y.ItemId == ItemIds.Wood).Sum(y => y.Amount);
                hits++;
            }
            Assert.That(wood, Is.EqualTo(GatherTable.Capacity(ResourceNodeType.Tree)));
            Assert.That(tree.Hit(ToolKind.Rock), Is.Empty);
        }

        [Test]
        public void Metal_ore_also_gives_stone()
        {
            var ore = new ResourceNode(2, ResourceNodeType.MetalOre);
            var y = ore.Hit(ToolKind.Pickaxe);
            Assert.That(y.Any(v => v.ItemId == ItemIds.MetalOre), Is.True);
            Assert.That(y.Any(v => v.ItemId == ItemIds.Stone), Is.True);
        }

        [Test]
        public void Hemp_is_picked_in_one_go_by_hand()
        {
            var hemp = new ResourceNode(3, ResourceNodeType.Hemp);
            var y = hemp.Hit(ToolKind.None);
            Assert.That(y.First().ItemId, Is.EqualTo(ItemIds.Cloth));
            Assert.That(y.Single(v => v.ItemId == ItemIds.HempSeed).Amount, Is.EqualTo(1), "a seed to plant");
            Assert.That(hemp.IsDepleted, Is.True);
        }

        [Test]
        public void Berries_are_picked_by_hand_a_few_at_a_time()
        {
            var bush = new ResourceNode(4, ResourceNodeType.BerryBush);
            var y = bush.Hit(ToolKind.None);
            Assert.That(y.First().ItemId, Is.EqualTo(ItemIds.Berries));
            Assert.That(y.First().Amount, Is.EqualTo(GatherTable.Capacity(ResourceNodeType.BerryBush)));
            Assert.That(y.Single(v => v.ItemId == ItemIds.BerrySeed).Amount, Is.EqualTo(1));
            Assert.That(bush.IsDepleted, Is.True);
        }

        [Test]
        public void Berries_are_food_that_fills_food_and_water()
        {
            var berries = ItemCatalog.CreateDefault().Get(ItemIds.Berries);
            Assert.That(berries.Category, Is.EqualTo(ItemCategory.Food));
            Assert.That(berries.Nourishment.Food, Is.GreaterThan(0f));
            Assert.That(berries.Nourishment.Water, Is.GreaterThan(0f));
            Assert.That(ItemCatalog.CreateDefault().Get(ItemIds.Wood).Nourishment.Food, Is.EqualTo(0f));
        }

        [Test]
        public void A_depleted_node_grows_back_after_its_timer()
        {
            var hemp = new ResourceNode(3, ResourceNodeType.Hemp);
            hemp.Hit(ToolKind.None);
            float wait = GatherTable.RespawnSeconds(ResourceNodeType.Hemp);
            Assert.That(hemp.TickRespawn(wait - 1f), Is.False);
            Assert.That(hemp.TickRespawn(1f), Is.True);
            Assert.That(hemp.Remaining, Is.EqualTo(hemp.Capacity));
        }
    }
}
