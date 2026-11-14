using System;
using Oxfall.Core.Survival;

namespace Oxfall.Core.Items
{
    public enum ItemCategory
    {
        Resource,
        Tool,
        Placeable,
        Food,
        Ammo,
        /// <summary>Clothing and armour, worn in the four wear slots.</summary>
        Attire,
        /// <summary>Bandages and syringes: used from the hand like food, they heal.</summary>
        Medical,
    }

    /// <summary>Where on the body a piece of clothing goes (one of each at a time).</summary>
    public enum WearSlot
    {
        None,
        Head,
        Chest,
        Legs,
        Feet,
    }

    /// <summary>What a tool is good at. Gathering yields depend on it.</summary>
    public enum ToolKind
    {
        None,
        Rock,
        Hatchet,
        Pickaxe,
        Torch,
        Hammer,
        MetalHatchet,
        MetalPickaxe,
        Spear,
        Bow,
        /// <summary>A satchel charge: thrown at a wall or a door, it goes off a few seconds later.</summary>
        Explosive,
        /// <summary>A gun that fires a spray of shot (handmade shells), one at a time.</summary>
        Shotgun,
        /// <summary>A gun that fires one bullet at a time, six before it is reloaded.</summary>
        Revolver,
    }

    /// <summary>
    /// Static description of an item type. Instances in an inventory are <see cref="ItemStack"/>s
    /// that point here by <see cref="Id"/>.
    /// </summary>
    public sealed class ItemDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public ItemCategory Category { get; }
        public int MaxStack { get; }
        public ToolKind Tool { get; }

        /// <summary>Whether a cosmetic skin can be applied. Skins never change stats.</summary>
        public bool Skinnable { get; }

        /// <summary>One line for the player on what it is for.</summary>
        public string Description { get; }

        /// <summary>What eating one gives back; nothing for anything that isn't food.</summary>
        public Nourishment Nourishment { get; }

        /// <summary>For clothing: where it is worn.</summary>
        public WearSlot Wear { get; }

        /// <summary>For clothing: the share of each blow, arrow or bite it takes (0 to 1; worn pieces add up).</summary>
        public float Protection { get; }

        public ItemDefinition(string id, string displayName, ItemCategory category, int maxStack,
            ToolKind tool = ToolKind.None, bool skinnable = false, string description = "",
            Nourishment nourishment = default, WearSlot wear = WearSlot.None, float protection = 0f)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Item id is required.", nameof(id));
            if (maxStack < 1) throw new ArgumentOutOfRangeException(nameof(maxStack), "Max stack must be at least 1.");
            Id = id;
            DisplayName = displayName ?? id;
            Category = category;
            MaxStack = maxStack;
            Tool = tool;
            Skinnable = skinnable;
            Description = description ?? "";
            Nourishment = nourishment;
            Wear = wear;
            Protection = protection;
        }

        public override string ToString() => Id;
    }
}
