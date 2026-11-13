using System;
using System.Collections.Generic;
using Oxfall.Core.Survival;

namespace Oxfall.Core.Items
{
    /// <summary>Every item type the game knows, looked up by id.</summary>
    public sealed class ItemCatalog
    {
        private readonly Dictionary<string, ItemDefinition> _items = new Dictionary<string, ItemDefinition>();

        public IEnumerable<ItemDefinition> All => _items.Values;

        public ItemCatalog(IEnumerable<ItemDefinition> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            foreach (var item in items)
            {
                if (_items.ContainsKey(item.Id))
                    throw new ArgumentException($"Duplicate item id '{item.Id}'.", nameof(items));
                _items.Add(item.Id, item);
            }
        }

        public ItemDefinition Get(string id)
        {
            if (id != null && _items.TryGetValue(id, out var item)) return item;
            throw new KeyNotFoundException($"Unknown item id '{id}'.");
        }

        public bool TryGet(string id, out ItemDefinition item)
        {
            if (id == null) { item = null; return false; }
            return _items.TryGetValue(id, out item);
        }

        public bool Contains(string id) => id != null && _items.ContainsKey(id);

        /// <summary>The MVP item set. Stack sizes follow the survival-genre convention players expect.</summary>
        public static ItemCatalog CreateDefault() => new ItemCatalog(new[]
        {
            new ItemDefinition(ItemIds.Wood, "Madera", ItemCategory.Resource, 1000,
                description: "Para fabricar herramientas y construir."),
            new ItemDefinition(ItemIds.Stone, "Piedra", ItemCategory.Resource, 1000,
                description: "Para herramientas y, más adelante, muros de piedra."),
            new ItemDefinition(ItemIds.MetalOre, "Mineral de metal", ItemCategory.Resource, 1000,
                description: "Se funde en un horno para sacar metal."),
            new ItemDefinition(ItemIds.Cloth, "Tela", ItemCategory.Resource, 1000,
                description: "Sale del cáñamo. Para antorchas y ropa."),

            new ItemDefinition(ItemIds.Berries, "Moras", ItemCategory.Food, 20,
                description: "Llévalas en la mano y toca COMER: algo de comida y de agua.",
                nourishment: new Nourishment(35f, 15f)),

            new ItemDefinition(ItemIds.Rock, "Roca", ItemCategory.Tool, 1, ToolKind.Rock, skinnable: true,
                description: "Golpea árboles y piedras. Mejor que nada."),
            new ItemDefinition(ItemIds.StoneHatchet, "Hacha de piedra", ItemCategory.Tool, 1, ToolKind.Hatchet, skinnable: true,
                description: "Tala árboles: el doble de madera que la roca."),
            new ItemDefinition(ItemIds.StonePickaxe, "Pico de piedra", ItemCategory.Tool, 1, ToolKind.Pickaxe, skinnable: true,
                description: "Pica piedra y mineral: el doble que la roca."),
            new ItemDefinition(ItemIds.Torch, "Antorcha", ItemCategory.Tool, 1, ToolKind.Torch, skinnable: true,
                description: "Alumbra de noche."),
            new ItemDefinition(ItemIds.Hammer, "Martillo", ItemCategory.Tool, 1, ToolKind.Hammer, skinnable: true,
                description: "Apunta a lo que construiste y toca MEJORAR: ramas, madera, piedra. También demuele."),

            new ItemDefinition(ItemIds.BuildingPlan, "Plano de construcción", ItemCategory.Placeable, 1,
                description: "Llévalo en la mano para construir."),
            new ItemDefinition(ItemIds.Campfire, "Fogata", ItemCategory.Placeable, 5,
                description: "Da luz y calor, y cocina."),
            new ItemDefinition(ItemIds.WoodenDoor, "Puerta de madera", ItemCategory.Placeable, 1,
                description: "Va en un marco de puerta. Tócala con ABRIR para abrirla o cerrarla."),
        });
    }
}
