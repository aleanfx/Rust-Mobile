using System;
using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Survival
{
    /// <summary>
    /// How warm a player feels, in degrees: mild by day, cold at night, colder in the rain and much
    /// colder in the sea. Clothes keep the warmth in (hide more than burlap, metal hardly at all), a
    /// roof keeps the rain off and the wind out, and a fire warms whoever stands near. Below
    /// <see cref="Chilly"/> health no longer comes back on its own; below <see cref="Freezing"/> the
    /// cold hurts, the more the colder. The same rules on the server (which hurts) and the phone
    /// (which shows it), from what both know: the clock, the weather, the clothes and the fires.
    /// </summary>
    public static class Temperature
    {
        /// <summary>The air at noon on a fair day, and at night.</summary>
        public const float DayAir = 21f;
        public const float NightAir = 4f;

        /// <summary>How much colder hard rain makes the air out in it.</summary>
        public const float RainChill = 8f;

        /// <summary>How much colder it is in the sea or a pond, up to the chest (and wet clothes keep in only half).</summary>
        public const float WaterChill = 10f;

        /// <summary>What a roof overhead is worth, besides keeping the rain off.</summary>
        public const float ShelterWarmth = 4f;

        /// <summary>A campfire's warmth right beside it, and how far it reaches; a lit furnace's.</summary>
        public const float FireHeat = 16f;
        public const float FireReach = 4.5f;
        public const float FurnaceHeat = 10f;
        public const float FurnaceReach = 3f;

        /// <summary>Below this health stops coming back on its own.</summary>
        public const float Chilly = 10f;

        /// <summary>Below this the cold hurts.</summary>
        public const float Freezing = 5f;

        /// <summary>Health per second for each degree below freezing, and the most it takes.</summary>
        public const float HurtPerDegree = 0.05f;
        public const float MaxHurt = 0.6f;

        /// <summary>The air out in the open, by how much daylight there is (0 to 1) and how hard it rains (0 to 1).</summary>
        public static float Air(float daylight, float rain) =>
            NightAir + (DayAir - NightAir) * Clamp01(daylight) - RainChill * Clamp01(rain);

        /// <summary>How many degrees the clothes worn keep in.</summary>
        public static float Insulation(Inventory inventory)
        {
            if (inventory == null || !inventory.HasWear) return 0f;
            float total = 0f;
            for (int i = inventory.CarrySlots; i < inventory.SlotCount; i++)
                if (!inventory[i].IsEmpty && inventory.Catalog.TryGet(inventory[i].ItemId, out var def)) total += def.Warmth;
            return total;
        }

        /// <summary>The warmth of the nearest fires at (x, y, z): campfires always burn, furnaces while lit.</summary>
        public static float FireWarmth(Settlement settlement, float x, float y, float z)
        {
            if (settlement == null) return 0f;
            float best = 0f;
            foreach (var d in settlement.Deployables)
            {
                float heat, reach;
                if (d.Kind == DeployableKind.Campfire) { heat = FireHeat; reach = FireReach; }
                else if (d.Kind == DeployableKind.Furnace && settlement.IsLit(d.Id)) { heat = FurnaceHeat; reach = FurnaceReach; }
                else continue;
                float dx = x - d.X, dy = y - d.Y, dz = z - d.Z;
                if (Math.Abs(dy) > 2.5f) continue;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                if (distance >= reach) continue;
                best = Math.Max(best, heat * (1f - distance / reach));
            }
            return best;
        }

        /// <summary>Whether a roof is overhead at (x, y, z): inside a building with a roof above this storey or a higher one.</summary>
        public static bool Sheltered(BuildingGrid grid, float x, float y, float z)
        {
            if (grid == null || grid.FoundationCount == 0) return false;
            var cell = BuildingGrid.CellAt(x, z);
            if (!grid.TryGetFoundationTop(cell, out float top) || y < top - 0.6f) return false;
            for (int level = 0; level <= BuildingGrid.MaxLevel; level++)
                if (grid.HasRoof(new Cell(cell.X, cell.Z, level)) && top + (level + 1) * BuildingGrid.WallHeight > y + 1.2f) return true;
            return false;
        }

        /// <summary>How warm someone feels, from the air, their clothes, a fire near them, a roof over them and whether they are in water.</summary>
        public static float Felt(float daylight, float rain, float insulation, float fire, bool sheltered, bool inWater)
        {
            float air = Air(daylight, sheltered ? 0f : rain);
            if (sheltered) air += ShelterWarmth;
            if (inWater)
            {
                air -= WaterChill;
                insulation *= 0.5f;
            }
            return air + insulation + fire;
        }

        /// <summary>Everything at once, for a player with feet at (x, y, z) wearing what <paramref name="inventory"/> holds.</summary>
        public static float At(Settlement settlement, Inventory inventory, float daylight, float rain, float x, float y, float z, bool inWater) =>
            Felt(daylight, rain, Insulation(inventory), FireWarmth(settlement, x, y, z), Sheltered(settlement?.Grid, x, y, z), inWater);

        /// <summary>Health per second the cold takes at this temperature.</summary>
        public static float Hurt(float felt) => felt >= Freezing ? 0f : Math.Min(MaxHurt, (Freezing - felt) * HurtPerDegree);

        /// <summary>Feet this far under the water line are in the water (the water reaches the chest).</summary>
        public const float WadeDepth = 1.0f;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
