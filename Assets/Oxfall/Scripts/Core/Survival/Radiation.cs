using System;
using Oxfall.Core.Items;
using Oxfall.Core.World;

namespace Oxfall.Core.Survival
{
    /// <summary>
    /// The military ruins are still hot: inside them a player soaks up radiation (less in good
    /// clothes), and past a threshold what was soaked up eats at their health until it wears off
    /// outside. A quick raid of the crates is safe naked; staying long needs clothes. The gas
    /// station is clean. The same rules on the server (which hurts) and the phone (which shows it).
    /// </summary>
    public static class Radiation
    {
        /// <summary>How far round a military ruin's middle it reaches (its yard and a little round it), full strength in the inner part.</summary>
        public const float Reach = Monuments.Radius + 6f;
        public const float Core = Monuments.Radius - 2f;

        /// <summary>Radiation soaked up per second at full strength, unprotected.</summary>
        public const float Dose = 3f;

        /// <summary>The most a player can hold, and what fades away per second out of it.</summary>
        public const float Max = 500f;
        public const float Fade = 2f;

        /// <summary>Above this it hurts: health per second for each point over it, up to <see cref="MaxHurt"/>.</summary>
        public const float Safe = 50f;
        public const float HurtPerPoint = 1f / 100f;
        public const float MaxHurt = 1f;

        /// <summary>How hot it is at (x, z): 0 out of every military ruin, 1 in the heart of one.</summary>
        public static float Level(IslandGround ground, float x, float z)
        {
            float best = 0f;
            foreach (var m in Monuments.Find(ground))
            {
                if (m.Kind != MonumentKind.Outpost) continue;
                float dx = x - m.X, dz = z - m.Z;
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d >= Reach) continue;
                best = Math.Max(best, d <= Core ? 1f : 1f - (d - Core) / (Reach - Core));
            }
            return best;
        }

        /// <summary>The share of the dose the clothes worn keep off (0 to 0.9).</summary>
        public static float Resistance(Inventory inventory)
        {
            if (inventory == null || !inventory.HasWear) return 0f;
            float total = 0f;
            for (int i = inventory.CarrySlots; i < inventory.SlotCount; i++)
                if (!inventory[i].IsEmpty && inventory.Catalog.TryGet(inventory[i].ItemId, out var def)) total += def.RadiationResist;
            return Math.Min(0.9f, total);
        }

        /// <summary>Radiation per second soaked up at (x, z) wearing what <paramref name="inventory"/> holds.</summary>
        public static float DoseAt(IslandGround ground, Inventory inventory, float x, float z)
        {
            float level = Level(ground, x, z);
            return level <= 0f ? 0f : Dose * level * (1f - Resistance(inventory));
        }

        /// <summary>Health per second this much held radiation takes.</summary>
        public static float Hurt(float held) => held <= Safe ? 0f : Math.Min(MaxHurt, (held - Safe) * HurtPerPoint);

        /// <summary>What is held after <paramref name="seconds"/> soaking up <paramref name="dose"/> a second (or fading, with none).</summary>
        public static float Next(float held, float dose, float seconds)
        {
            held = dose > 0f ? held + dose * seconds : held - Fade * seconds;
            return held < 0f ? 0f : held > Max ? Max : held;
        }
    }
}
