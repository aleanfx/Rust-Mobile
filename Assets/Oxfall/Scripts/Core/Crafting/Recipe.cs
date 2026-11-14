using System;
using System.Collections.Generic;
using Oxfall.Core.Items;

namespace Oxfall.Core.Crafting
{
    public readonly struct Ingredient
    {
        public string ItemId { get; }
        public int Amount { get; }

        public Ingredient(string itemId, int amount)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentException("Ingredient needs an item id.", nameof(itemId));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            ItemId = itemId;
            Amount = amount;
        }
    }

    public sealed class Recipe
    {
        public string Id { get; }
        public string OutputItemId { get; }
        public int OutputAmount { get; }
        public IReadOnlyList<Ingredient> Ingredients { get; }
        public float CraftSeconds { get; }

        public Recipe(string id, string outputItemId, int outputAmount, float craftSeconds, params Ingredient[] ingredients)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Recipe id is required.", nameof(id));
            if (string.IsNullOrEmpty(outputItemId)) throw new ArgumentException("Output item is required.", nameof(outputItemId));
            if (outputAmount <= 0) throw new ArgumentOutOfRangeException(nameof(outputAmount));
            if (craftSeconds < 0) throw new ArgumentOutOfRangeException(nameof(craftSeconds));
            if (ingredients == null || ingredients.Length == 0) throw new ArgumentException("A recipe needs ingredients.", nameof(ingredients));
            Id = id;
            OutputItemId = outputItemId;
            OutputAmount = outputAmount;
            CraftSeconds = craftSeconds;
            Ingredients = Array.AsReadOnly((Ingredient[])ingredients.Clone());
        }

        public bool CanAfford(Inventory inventory)
        {
            foreach (var ing in Ingredients)
                if (!inventory.Has(ing.ItemId, ing.Amount)) return false;
            return true;
        }
    }

    public sealed class RecipeBook
    {
        private readonly Dictionary<string, Recipe> _recipes = new Dictionary<string, Recipe>();
        private readonly List<Recipe> _ordered = new List<Recipe>();

        public IReadOnlyList<Recipe> All => _ordered;

        public RecipeBook(ItemCatalog catalog, IEnumerable<Recipe> recipes)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            foreach (var r in recipes)
            {
                if (_recipes.ContainsKey(r.Id)) throw new ArgumentException($"Duplicate recipe id '{r.Id}'.");
                if (!catalog.Contains(r.OutputItemId)) throw new ArgumentException($"Recipe '{r.Id}' makes unknown item '{r.OutputItemId}'.");
                foreach (var ing in r.Ingredients)
                    if (!catalog.Contains(ing.ItemId)) throw new ArgumentException($"Recipe '{r.Id}' uses unknown item '{ing.ItemId}'.");
                _recipes.Add(r.Id, r);
                _ordered.Add(r);
            }
        }

        public Recipe Get(string id) =>
            _recipes.TryGetValue(id, out var r) ? r : throw new KeyNotFoundException($"Unknown recipe '{id}'.");

        public bool TryGet(string id, out Recipe recipe) => _recipes.TryGetValue(id, out recipe);

        /// <summary>The MVP recipes. Costs follow the survival-genre baseline so veterans feel at home.</summary>
        public static RecipeBook CreateDefault(ItemCatalog catalog) => new RecipeBook(catalog, new[]
        {
            new Recipe("stone_hatchet", ItemIds.StoneHatchet, 1, 10f,
                new Ingredient(ItemIds.Wood, 200), new Ingredient(ItemIds.Stone, 100)),
            new Recipe("stone_pickaxe", ItemIds.StonePickaxe, 1, 10f,
                new Ingredient(ItemIds.Wood, 200), new Ingredient(ItemIds.Stone, 100)),
            new Recipe("torch", ItemIds.Torch, 1, 3f,
                new Ingredient(ItemIds.Wood, 30), new Ingredient(ItemIds.Cloth, 10)),
            new Recipe("building_plan", ItemIds.BuildingPlan, 1, 3f,
                new Ingredient(ItemIds.Wood, 20)),
            new Recipe("campfire", ItemIds.Campfire, 1, 5f,
                new Ingredient(ItemIds.Wood, 100)),
            new Recipe("hammer", ItemIds.Hammer, 1, 5f,
                new Ingredient(ItemIds.Wood, 100)),
            new Recipe("wooden_spear", ItemIds.WoodenSpear, 1, 15f,
                new Ingredient(ItemIds.Wood, 300)),
            new Recipe("wooden_bow", ItemIds.WoodenBow, 1, 20f,
                new Ingredient(ItemIds.Wood, 200), new Ingredient(ItemIds.Cloth, 40)),
            new Recipe("burlap_headwrap", ItemIds.BurlapHeadwrap, 1, 5f,
                new Ingredient(ItemIds.Cloth, 10)),
            new Recipe("burlap_shirt", ItemIds.BurlapShirt, 1, 8f,
                new Ingredient(ItemIds.Cloth, 20)),
            new Recipe("burlap_trousers", ItemIds.BurlapTrousers, 1, 8f,
                new Ingredient(ItemIds.Cloth, 20)),
            new Recipe("hide_hood", ItemIds.HideHood, 1, 10f,
                new Ingredient(ItemIds.Leather, 10), new Ingredient(ItemIds.Cloth, 5)),
            new Recipe("hide_vest", ItemIds.HideVest, 1, 15f,
                new Ingredient(ItemIds.Leather, 20), new Ingredient(ItemIds.Cloth, 10)),
            new Recipe("hide_pants", ItemIds.HidePants, 1, 12f,
                new Ingredient(ItemIds.Leather, 15), new Ingredient(ItemIds.Cloth, 10)),
            new Recipe("hide_boots", ItemIds.HideBoots, 1, 10f,
                new Ingredient(ItemIds.Leather, 10)),
            new Recipe("arrows", ItemIds.Arrow, 2, 5f,
                new Ingredient(ItemIds.Wood, 25), new Ingredient(ItemIds.Stone, 10)),
            new Recipe("wooden_door", ItemIds.WoodenDoor, 1, 10f,
                new Ingredient(ItemIds.Wood, 150)),
            new Recipe("storage_box", ItemIds.StorageBox, 1, 10f,
                new Ingredient(ItemIds.Wood, 100)),
            new Recipe("sleeping_bag", ItemIds.SleepingBag, 1, 10f,
                new Ingredient(ItemIds.Cloth, 30)),
            new Recipe("furnace", ItemIds.Furnace, 1, 20f,
                new Ingredient(ItemIds.Stone, 200), new Ingredient(ItemIds.Wood, 100)),
            new Recipe("tool_cupboard", ItemIds.ToolCupboard, 1, 20f,
                new Ingredient(ItemIds.Wood, 500)),
            new Recipe("metal_hatchet", ItemIds.MetalHatchet, 1, 15f,
                new Ingredient(ItemIds.Wood, 100), new Ingredient(ItemIds.MetalFragments, 75)),
            new Recipe("metal_pickaxe", ItemIds.MetalPickaxe, 1, 15f,
                new Ingredient(ItemIds.Wood, 100), new Ingredient(ItemIds.MetalFragments, 125)),
            new Recipe("sheet_metal_door", ItemIds.SheetMetalDoor, 1, 20f,
                new Ingredient(ItemIds.MetalFragments, 150)),
            new Recipe("code_lock", ItemIds.CodeLock, 1, 10f,
                new Ingredient(ItemIds.MetalFragments, 100)),
        });
    }
}
