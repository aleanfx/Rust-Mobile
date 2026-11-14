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
            new ItemDefinition(ItemIds.MetalFragments, "Fragmentos de metal", ItemCategory.Resource, 1000,
                description: "Salen del horno, fundiendo mineral de metal."),
            new ItemDefinition(ItemIds.Charcoal, "Carbón", ItemCategory.Resource, 1000,
                description: "Lo que deja la madera al arder en el horno."),
            new ItemDefinition(ItemIds.Leather, "Cuero", ItemCategory.Resource, 1000,
                description: "Sale de los animales que cazas. Para ropa y armaduras."),
            new ItemDefinition(ItemIds.SulfurOre, "Mineral de azufre", ItemCategory.Resource, 1000,
                description: "De las rocas con vetas amarillas. Se funde en el horno para sacar azufre."),
            new ItemDefinition(ItemIds.Sulfur, "Azufre", ItemCategory.Resource, 1000,
                description: "Sale del horno. Con carbón se hace pólvora."),
            new ItemDefinition(ItemIds.Gunpowder, "Pólvora", ItemCategory.Resource, 1000,
                description: "Carbón y azufre molidos juntos. Para las cargas explosivas."),

            new ItemDefinition(ItemIds.Berries, "Moras", ItemCategory.Food, 20,
                description: "Llévalas en la mano y toca COMER: algo de comida y de agua.",
                nourishment: new Nourishment(35f, 15f)),
            new ItemDefinition(ItemIds.RawMeat, "Carne cruda", ItemCategory.Food, 20,
                description: "Alimenta poco y da sed. Ásala en una fogata: ábrela y métela dentro.",
                nourishment: new Nourishment(15f, -10f)),
            new ItemDefinition(ItemIds.CookedMeat, "Carne asada", ItemCategory.Food, 20,
                description: "La mejor comida de la isla.",
                nourishment: new Nourishment(75f, 5f)),

            new ItemDefinition(ItemIds.Rock, "Roca", ItemCategory.Tool, 1, ToolKind.Rock, skinnable: true,
                description: "Golpea árboles y piedras. Mejor que nada."),
            new ItemDefinition(ItemIds.StoneHatchet, "Hacha de piedra", ItemCategory.Tool, 1, ToolKind.Hatchet, skinnable: true,
                description: "Tala árboles: el doble de madera que la roca."),
            new ItemDefinition(ItemIds.StonePickaxe, "Pico de piedra", ItemCategory.Tool, 1, ToolKind.Pickaxe, skinnable: true,
                description: "Pica piedra y mineral: el doble que la roca."),
            new ItemDefinition(ItemIds.Torch, "Antorcha", ItemCategory.Tool, 1, ToolKind.Torch, skinnable: true,
                description: "Alumbra de noche."),
            new ItemDefinition(ItemIds.Hammer, "Martillo", ItemCategory.Tool, 1, ToolKind.Hammer, skinnable: true,
                description: "Apunta a lo que construiste y toca MEJORAR: ramas, madera, piedra, metal. También demuele."),
            new ItemDefinition(ItemIds.MetalHatchet, "Hacha de metal", ItemCategory.Tool, 1, ToolKind.MetalHatchet, skinnable: true,
                description: "Tala árboles mucho más rápido que la de piedra."),
            new ItemDefinition(ItemIds.MetalPickaxe, "Pico de metal", ItemCategory.Tool, 1, ToolKind.MetalPickaxe, skinnable: true,
                description: "Pica piedra y mineral mucho más rápido que el de piedra."),
            new ItemDefinition(ItemIds.WoodenSpear, "Lanza de madera", ItemCategory.Tool, 1, ToolKind.Spear, skinnable: true,
                description: "Un palo largo con la punta endurecida al fuego: llega más lejos y hace más daño que cualquier herramienta."),
            new ItemDefinition(ItemIds.WoodenBow, "Arco de madera", ItemCategory.Tool, 1, ToolKind.Bow, skinnable: true,
                description: "Tira flechas: para cazar lo que corre más que tú y pelear de lejos. Apunta un poco por encima: la flecha cae."),
            new ItemDefinition(ItemIds.SatchelCharge, "Carga explosiva", ItemCategory.Tool, 10, ToolKind.Explosive,
                description: "Lánzala a una pared o una puerta: explota a los 5 segundos. Madera: 2 cargas; piedra: 4; metal: 8. "
                    + "Puerta de madera: 1; de chapa: 2. Rompe también cajas y armarios cercanos si no hay pared en medio. Aléjate."),
            new ItemDefinition(ItemIds.Arrow, "Flecha", ItemCategory.Ammo, 64,
                description: "Madera con punta de piedra. El arco las gasta una a una."),

            // Clothing: tela basta para empezar, cuero de lo que se caza para aguantar más.
            new ItemDefinition(ItemIds.BurlapHeadwrap, "Turbante de arpillera", ItemCategory.Attire, 1, skinnable: true,
                description: "Tela basta enrollada a la cabeza.", wear: WearSlot.Head, protection: 0.04f),
            new ItemDefinition(ItemIds.BurlapShirt, "Camisa de arpillera", ItemCategory.Attire, 1, skinnable: true,
                description: "Mejor que nada contra golpes y flechas.", wear: WearSlot.Chest, protection: 0.06f),
            new ItemDefinition(ItemIds.BurlapTrousers, "Pantalón de arpillera", ItemCategory.Attire, 1, skinnable: true,
                description: "Tela basta atada con cuerda.", wear: WearSlot.Legs, protection: 0.05f),
            new ItemDefinition(ItemIds.HideHood, "Capucha de cuero", ItemCategory.Attire, 1, skinnable: true,
                description: "Cuero curtido sobre la cabeza y el cuello.", wear: WearSlot.Head, protection: 0.08f),
            new ItemDefinition(ItemIds.HideVest, "Chaleco de cuero", ItemCategory.Attire, 1, skinnable: true,
                description: "Cuero grueso sobre el pecho: aguanta bien mordiscos y flechas.", wear: WearSlot.Chest, protection: 0.14f),
            new ItemDefinition(ItemIds.HidePants, "Pantalón de cuero", ItemCategory.Attire, 1, skinnable: true,
                description: "Cuero en las piernas, cosido con tela.", wear: WearSlot.Legs, protection: 0.11f),
            new ItemDefinition(ItemIds.HideBoots, "Botas de cuero", ItemCategory.Attire, 1, skinnable: true,
                description: "Para no ir descalzo por las rocas.", wear: WearSlot.Feet, protection: 0.05f),

            new ItemDefinition(ItemIds.BuildingPlan, "Plano de construcción", ItemCategory.Placeable, 1,
                description: "Llévalo en la mano para construir."),
            new ItemDefinition(ItemIds.Campfire, "Fogata", ItemCategory.Placeable, 5,
                description: "Da luz y calor, y cocina."),
            new ItemDefinition(ItemIds.WoodenDoor, "Puerta de madera", ItemCategory.Placeable, 1,
                description: "Va en un marco de puerta. Tócala con ABRIR para abrirla o cerrarla."),
            new ItemDefinition(ItemIds.StorageBox, "Caja de almacenaje", ItemCategory.Placeable, 1,
                description: "Guarda 12 casillas de lo que quieras. Ponla dentro de tu casa."),
            new ItemDefinition(ItemIds.Furnace, "Horno", ItemCategory.Placeable, 1,
                description: "Mete madera y mineral y enciéndelo: el de metal da fragmentos, el de azufre da azufre, y la madera, carbón."),
            new ItemDefinition(ItemIds.SleepingBag, "Saco de dormir", ItemCategory.Placeable, 1,
                description: "Al morir puedes despertar en él en vez de en la playa (cada 2 minutos)."),
            new ItemDefinition(ItemIds.ToolCupboard, "Armario de herramientas", ItemCategory.Placeable, 1,
                description: "Nadie que no esté autorizado en él puede construir a su alrededor (24 m)."),
            new ItemDefinition(ItemIds.SheetMetalDoor, "Puerta de chapa", ItemCategory.Placeable, 1,
                description: "Una puerta de metal, mucho más dura que la de madera. Va en un marco de puerta."),
            new ItemDefinition(ItemIds.CodeLock, "Candado de código", ItemCategory.Placeable, 1,
                description: "Ponlo en una puerta, una caja o un armario y elige un código de 4 cifras: solo entra quien lo sepa."),
        });
    }
}
