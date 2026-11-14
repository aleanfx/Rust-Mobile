using System;
using System.Collections.Generic;
using Oxfall.Core.Items;

namespace Oxfall.Core.Gathering
{
    /// <summary>
    /// What the island's old barrels hold, as on the PC game's roadsides: always some metal, often
    /// cloth, sometimes arrows, now and then a whole tool or weapon someone left behind.
    /// </summary>
    public static class LootTable
    {
        private static readonly string[] Finds = { ItemIds.StoneHatchet, ItemIds.StonePickaxe, ItemIds.WoodenSpear, ItemIds.WoodenBow };

        public static List<Yield> Barrel(Random random)
        {
            var loot = new List<Yield>(4) { new Yield(ItemIds.MetalFragments, 10 + random.Next(16)) };
            if (random.NextDouble() < 0.55) loot.Add(new Yield(ItemIds.Cloth, 5 + random.Next(11)));
            if (random.NextDouble() < 0.35) loot.Add(new Yield(ItemIds.Arrow, 3 + random.Next(6)));
            if (random.NextDouble() < 0.2) loot.Add(new Yield(ItemIds.Leather, 2 + random.Next(4)));
            if (random.NextDouble() < 0.12) loot.Add(new Yield(Finds[random.Next(Finds.Length)], 1));
            return loot;
        }
    }
}
