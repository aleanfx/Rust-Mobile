using System;

namespace Oxfall.Core.Items
{
    /// <summary>
    /// Tools and weapons wear out, as on the PC game: every swing that lands, arrow loosed or shot
    /// fired uses up a little of them, and once used up they break and are gone, so new ones must be
    /// made. Better ones last longer. Everything else never wears.
    /// </summary>
    public static class Durability
    {
        /// <summary>How many uses a new one has (0: it never wears).</summary>
        public static int Uses(string itemId)
        {
            switch (itemId)
            {
                case ItemIds.Rock: return 120;
                case ItemIds.StoneHatchet: return 160;
                case ItemIds.StonePickaxe: return 160;
                case ItemIds.MetalHatchet: return 400;
                case ItemIds.MetalPickaxe: return 400;
                case ItemIds.WoodenSpear: return 120;
                case ItemIds.WoodenBow: return 150;
                case ItemIds.Blunderbuss: return 80;
                case ItemIds.Revolver: return 250;
                case ItemIds.SemiRifle: return 300;
                default: return 0;
            }
        }

        public static bool Wears(string itemId) => Uses(itemId) > 0;

        /// <summary>How much of it is left, 1 new to 0 broken (1 for anything that never wears).</summary>
        public static float Left(ItemStack stack)
        {
            if (stack.IsEmpty) return 1f;
            int uses = Uses(stack.ItemId);
            return uses <= 0 ? 1f : Math.Max(0f, 1f - stack.Used / (float)uses);
        }
    }
}
