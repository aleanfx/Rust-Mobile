using System;

namespace Oxfall.Core.Items
{
    public enum ItemCategory
    {
        Resource,
        Tool,
        Placeable,
    }

    /// <summary>What a tool is good at. Gathering yields depend on it.</summary>
    public enum ToolKind
    {
        None,
        Rock,
        Hatchet,
        Pickaxe,
        Torch,
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

        public ItemDefinition(string id, string displayName, ItemCategory category, int maxStack,
            ToolKind tool = ToolKind.None, bool skinnable = false, string description = "")
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
        }

        public override string ToString() => Id;
    }
}
