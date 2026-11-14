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

        private static readonly string[] Components = { ItemIds.Gears, ItemIds.MetalSpring, ItemIds.MetalPipe };

        /// <summary>What a crate in the ruins may hold beyond its scrap and parts: things worth the walk.</summary>
        private static readonly (string Id, int Min, int Max)[] CrateFinds =
        {
            (ItemIds.PistolBullet, 8, 16), (ItemIds.Shell, 6, 12), (ItemIds.Bandage, 2, 4), (ItemIds.MedicalSyringe, 1, 2),
            (ItemIds.Gunpowder, 20, 50), (ItemIds.Revolver, 1, 1), (ItemIds.Blunderbuss, 1, 1), (ItemIds.MetalHatchet, 1, 1),
            (ItemIds.MetalPickaxe, 1, 1), (ItemIds.HideVest, 1, 1), (ItemIds.RifleBullet, 6, 12),
        };

        public static List<Yield> Barrel(Random random)
        {
            var loot = new List<Yield>(5)
            {
                new Yield(ItemIds.MetalFragments, 10 + random.Next(16)),
                new Yield(ItemIds.Scrap, 2 + random.Next(4)),
            };
            if (random.NextDouble() < 0.55) loot.Add(new Yield(ItemIds.Cloth, 5 + random.Next(11)));
            if (random.NextDouble() < 0.35) loot.Add(new Yield(ItemIds.Arrow, 3 + random.Next(6)));
            if (random.NextDouble() < 0.2) loot.Add(new Yield(ItemIds.Leather, 2 + random.Next(4)));
            if (random.NextDouble() < 0.12) loot.Add(new Yield(Finds[random.Next(Finds.Length)], 1));
            if (random.NextDouble() < 0.15) loot.Add(new Yield(Components[random.Next(Components.Length)], 1));
            return loot;
        }

        /// <summary>
        /// What a crate in the ruins holds: more scrap than a barrel, usually a part or two for the
        /// workbench's recipes, and one find among rounds, medicine, gunpowder and now and then a gun.
        /// </summary>
        public static List<Yield> Crate(Random random)
        {
            var loot = new List<Yield>(5)
            {
                new Yield(ItemIds.Scrap, 8 + random.Next(8)),
                new Yield(ItemIds.MetalFragments, 20 + random.Next(31)),
            };
            int parts = random.NextDouble() < 0.7 ? 1 + (random.NextDouble() < 0.3 ? 1 : 0) : 0;
            for (int i = 0; i < parts; i++) loot.Add(new Yield(Components[random.Next(Components.Length)], 1));
            var find = CrateFinds[random.Next(CrateFinds.Length)];
            loot.Add(new Yield(find.Id, find.Min + random.Next(find.Max - find.Min + 1)));
            return loot;
        }

        /// <summary>What a supply drop holds, one of each (some always, some rolled).</summary>
        private static readonly (string Id, int Min, int Max)[] DropFinds =
        {
            (ItemIds.Revolver, 1, 1), (ItemIds.Blunderbuss, 1, 1), (ItemIds.MedicalSyringe, 2, 3), (ItemIds.SatchelCharge, 1, 2),
            (ItemIds.HideVest, 1, 1), (ItemIds.MetalHatchet, 1, 1), (ItemIds.MetalPickaxe, 1, 1), (ItemIds.SheetMetalDoor, 1, 1),
            (ItemIds.SemiRifle, 1, 1), (ItemIds.RifleBullet, 30, 60),
        };

        /// <summary>
        /// What a supply drop holds: rounds, medicine, gunpowder, metal and scrap always, and three of the
        /// finds (guns, charges, armour, metal tools) — worth a run across the island and a fight.
        /// </summary>
        public static List<Yield> SupplyDrop(Random random)
        {
            var loot = new List<Yield>(9)
            {
                new Yield(ItemIds.PistolBullet, 20 + random.Next(21)),
                new Yield(ItemIds.Shell, 10 + random.Next(11)),
                new Yield(ItemIds.Bandage, 3 + random.Next(3)),
                new Yield(ItemIds.Gunpowder, 60 + random.Next(61)),
                new Yield(ItemIds.MetalFragments, 150 + random.Next(151)),
                new Yield(ItemIds.Scrap, 40 + random.Next(31)),
            };
            var picks = new List<int>();
            while (picks.Count < 3)
            {
                int i = random.Next(DropFinds.Length);
                if (!picks.Contains(i)) picks.Add(i);
            }
            foreach (int i in picks)
            {
                var find = DropFinds[i];
                loot.Add(new Yield(find.Id, find.Min + random.Next(find.Max - find.Min + 1)));
            }
            return loot;
        }

        /// <summary>
        /// What a bandit had on him: rounds for his revolver, scrap and cloth, sometimes a bandage or a
        /// part, and now and then the revolver itself.
        /// </summary>
        public static List<Yield> Bandit(Random random)
        {
            var loot = new List<Yield>(6)
            {
                new Yield(ItemIds.PistolBullet, 4 + random.Next(7)),
                new Yield(ItemIds.Scrap, 6 + random.Next(9)),
                new Yield(ItemIds.Cloth, 5 + random.Next(11)),
            };
            if (random.NextDouble() < 0.3) loot.Add(new Yield(ItemIds.Bandage, 1));
            if (random.NextDouble() < 0.25) loot.Add(new Yield(Components[random.Next(Components.Length)], 1));
            if (random.NextDouble() < 0.12) loot.Add(new Yield(ItemIds.Revolver, 1));
            return loot;
        }
    }
}
