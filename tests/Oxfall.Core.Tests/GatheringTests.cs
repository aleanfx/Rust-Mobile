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
            Assert.That(y.Single().ItemId, Is.EqualTo(ItemIds.Cloth));
            Assert.That(hemp.IsDepleted, Is.True);
        }

        [Test]
        public void Berries_are_picked_by_hand_a_few_at_a_time()
        {
            var bush = new ResourceNode(4, ResourceNodeType.BerryBush);
            var y = bush.Hit(ToolKind.None);
            Assert.That(y.Single().ItemId, Is.EqualTo(ItemIds.Berries));
            Assert.That(y.Single().Amount, Is.EqualTo(GatherTable.Capacity(ResourceNodeType.BerryBush)));
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
