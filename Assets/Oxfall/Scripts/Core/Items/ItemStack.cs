using System;

namespace Oxfall.Core.Items
{
    /// <summary>An amount of one item type in one slot, with an optional cosmetic skin, and for a tool how worn it is.</summary>
    public readonly struct ItemStack : IEquatable<ItemStack>
    {
        public static readonly ItemStack Empty = default;

        public string ItemId { get; }
        public int Amount { get; }

        /// <summary>0 means the default look. Stacks with different skins never merge.</summary>
        public int SkinId { get; }

        /// <summary>For a tool or weapon, how many of its uses are spent (0: new; see <see cref="Durability"/>).</summary>
        public int Used { get; }

        public bool IsEmpty => ItemId == null || Amount <= 0;

        public ItemStack(string itemId, int amount, int skinId = 0, int used = 0)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount > 0 && string.IsNullOrEmpty(itemId)) throw new ArgumentException("A non-empty stack needs an item id.", nameof(itemId));
            ItemId = amount == 0 ? null : itemId;
            Amount = amount;
            SkinId = amount == 0 ? 0 : skinId;
            Used = amount == 0 ? 0 : Math.Max(0, used);
        }

        public ItemStack WithAmount(int amount) => new ItemStack(ItemId, amount, SkinId, Used);

        public ItemStack WithUsed(int used) => new ItemStack(ItemId, Amount, SkinId, used);

        public bool CanMergeWith(ItemStack other) =>
            !IsEmpty && !other.IsEmpty && ItemId == other.ItemId && SkinId == other.SkinId && Used == other.Used;

        public bool Equals(ItemStack other) =>
            (IsEmpty && other.IsEmpty) || (ItemId == other.ItemId && Amount == other.Amount && SkinId == other.SkinId && Used == other.Used);

        public override bool Equals(object obj) => obj is ItemStack other && Equals(other);

        public override int GetHashCode() => IsEmpty ? 0 : (((ItemId.GetHashCode() * 397) ^ Amount) * 31 + SkinId) * 17 + Used;

        public static bool operator ==(ItemStack a, ItemStack b) => a.Equals(b);
        public static bool operator !=(ItemStack a, ItemStack b) => !a.Equals(b);

        public override string ToString() => IsEmpty ? "(empty)" : $"{ItemId} x{Amount}" + (SkinId != 0 ? $" (skin {SkinId})" : "") + (Used != 0 ? $" (used {Used})" : "");
    }
}
