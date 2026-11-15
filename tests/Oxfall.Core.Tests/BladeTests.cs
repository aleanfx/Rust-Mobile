using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;

namespace Oxfall.Core.Tests
{
    /// <summary>The machete and the sword: learned and made at a workbench, and the hardest-hitting blows.</summary>
    public class BladeTests
    {
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        [Test]
        public void They_hit_harder_than_tools_the_sword_hardest_and_slowest()
        {
            Assert.Greater(Combat.MeleeDamage(ToolKind.Machete), Combat.MeleeDamage(ToolKind.MetalPickaxe));
            Assert.Greater(Combat.MeleeDamage(ToolKind.Sword), Combat.MeleeDamage(ToolKind.Spear));
            Assert.Greater(Combat.SwingSeconds(ToolKind.Sword), Combat.SwingSeconds(ToolKind.Machete));
            // Damage per second: the machete's quick cuts keep up with the spear.
            Assert.GreaterOrEqual(Combat.MeleeDamage(ToolKind.Machete) / Combat.SwingSeconds(ToolKind.Machete),
                Combat.MeleeDamage(ToolKind.Spear) / Combat.SwingSeconds(ToolKind.Spear));
            Assert.IsTrue(Combat.IsBlade(ToolKind.Sword));
            Assert.IsFalse(Combat.IsBlade(ToolKind.Hatchet));
        }

        [Test]
        public void The_machete_cuts_up_a_carcass_well_and_some_wood()
        {
            Assert.AreEqual(Animals.Harvest(AnimalKind.Boar, ToolKind.MetalHatchet), Animals.Harvest(AnimalKind.Boar, ToolKind.Machete));
            Assert.Greater(GatherTable.PerHit(ResourceNodeType.Tree, ToolKind.Machete), 0);
            Assert.Less(GatherTable.PerHit(ResourceNodeType.Tree, ToolKind.Machete), GatherTable.PerHit(ResourceNodeType.Tree, ToolKind.Hatchet));
        }

        [Test]
        public void They_are_learned_and_made_at_a_workbench_and_wear()
        {
            var recipes = RecipeBook.CreateDefault(Catalog);
            foreach (var (id, item) in new[] { ("machete", ItemIds.Machete), ("salvaged_sword", ItemIds.Sword) })
            {
                Assert.IsTrue(recipes.TryGet(id, out var r));
                Assert.AreEqual(item, r.OutputItemId);
                Assert.IsTrue(r.NeedsWorkbench);
                Assert.Greater(r.ScrapToLearn, 0);
                Assert.IsTrue(Catalog.TryGet(item, out var def));
                Assert.IsTrue(Durability.Wears(item));
            }
        }
    }
}
