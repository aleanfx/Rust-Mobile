using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Tests
{
    /// <summary>Metal armour: learned at the workbench, worn over the hide set, and it takes more of each blow.</summary>
    public class ArmourTests
    {
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();
        private static readonly string[] Metal = { ItemIds.MetalFacemask, ItemIds.MetalChestPlate, ItemIds.RoadsignKilt };

        private static Survivor Dressed(params string[] items)
        {
            var s = new Survivor(Catalog);
            foreach (var id in items)
            {
                s.Inventory.Add(id, 1);
                int slot = Enumerable.Range(0, s.Inventory.CarrySlots).First(i => s.Inventory[i].ItemId == id);
                Assert.IsTrue(s.Inventory.Wear(slot), id);
            }
            return s;
        }

        [Test]
        public void Each_piece_is_worn_where_it_belongs()
        {
            var catalog = Catalog;
            Assert.AreEqual(WearSlot.Head, catalog.Get(ItemIds.MetalFacemask).Wear);
            Assert.AreEqual(WearSlot.Chest, catalog.Get(ItemIds.MetalChestPlate).Wear);
            Assert.AreEqual(WearSlot.Legs, catalog.Get(ItemIds.RoadsignKilt).Wear);
            foreach (var id in Metal) Assert.AreEqual(ItemCategory.Attire, catalog.Get(id).Category, id);
        }

        [Test]
        public void Metal_takes_more_of_a_blow_than_hide()
        {
            var hide = Dressed(ItemIds.HideHood, ItemIds.HideVest, ItemIds.HidePants, ItemIds.HideBoots);
            var metal = Dressed(ItemIds.MetalFacemask, ItemIds.MetalChestPlate, ItemIds.RoadsignKilt, ItemIds.HideBoots);
            Assert.AreEqual(0.38f, hide.Inventory.Protection, 0.001f);
            Assert.AreEqual(0.65f, metal.Inventory.Protection, 0.001f);
            foreach (var (h, m) in new[] { (ItemIds.HideHood, ItemIds.MetalFacemask), (ItemIds.HideVest, ItemIds.MetalChestPlate), (ItemIds.HidePants, ItemIds.RoadsignKilt) })
                Assert.Greater(Catalog.Get(m).Protection, Catalog.Get(h).Protection, m);
        }

        [Test]
        public void Metal_armour_is_learned_and_made_at_a_workbench()
        {
            var book = RecipeBook.CreateDefault(Catalog);
            foreach (var id in Metal)
            {
                var recipe = book.All.Single(r => r.OutputItemId == id);
                Assert.IsTrue(recipe.NeedsWorkbench, id);
                Assert.Greater(recipe.ScrapToLearn, 0, id);
                Assert.IsTrue(recipe.Ingredients.Any(i => i.ItemId == ItemIds.MetalFragments), id);
            }
        }
    }
}
