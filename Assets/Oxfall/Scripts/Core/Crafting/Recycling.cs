using System.Collections.Generic;
using Oxfall.Core.Items;

namespace Oxfall.Core.Crafting
{
    /// <summary>
    /// What a recycler makes of things, one at a time: gun parts become scrap and metal; anything
    /// made from a recipe gives back half of what went into it. Raw resources, food, ammunition and
    /// medicine are not taken.
    /// </summary>
    public sealed class Recycling
    {
        /// <summary>Seconds a recycler takes over each thing.</summary>
        public const float Seconds = 4f;

        private static readonly Ingredient[] None = new Ingredient[0];
        private readonly Dictionary<string, Ingredient[]> _yields = new Dictionary<string, Ingredient[]>();

        public Recycling(ItemCatalog catalog, RecipeBook recipes)
        {
            // Parts: what they are worth as scrap (the recycler is where scrap comes from).
            _yields[ItemIds.Gears] = new[] { new Ingredient(ItemIds.Scrap, 10), new Ingredient(ItemIds.MetalFragments, 13) };
            _yields[ItemIds.MetalSpring] = new[] { new Ingredient(ItemIds.Scrap, 10), new Ingredient(ItemIds.MetalFragments, 10) };
            _yields[ItemIds.MetalPipe] = new[] { new Ingredient(ItemIds.Scrap, 5), new Ingredient(ItemIds.MetalFragments, 8) };

            foreach (var recipe in recipes.All)
            {
                string item = recipe.OutputItemId;
                if (_yields.ContainsKey(item) || !catalog.TryGet(item, out var def)) continue;
                if (def.Category == ItemCategory.Resource || def.Category == ItemCategory.Food
                    || def.Category == ItemCategory.Ammo || def.Category == ItemCategory.Medical) continue;
                var back = new List<Ingredient>();
                foreach (var ing in recipe.Ingredients)
                {
                    int amount = ing.Amount / (2 * recipe.OutputAmount);
                    if (amount > 0) back.Add(new Ingredient(ing.ItemId, amount));
                }
                if (back.Count > 0) _yields[item] = back.ToArray();
            }
        }

        /// <summary>Whether a recycler takes this item.</summary>
        public bool Takes(string itemId) => itemId != null && _yields.ContainsKey(itemId);

        /// <summary>What one of this item comes out as (nothing for what a recycler does not take).</summary>
        public IReadOnlyList<Ingredient> Yield(string itemId) =>
            itemId != null && _yields.TryGetValue(itemId, out var y) ? y : None;
    }
}
