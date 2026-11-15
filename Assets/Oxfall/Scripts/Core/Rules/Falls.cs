using System;

namespace Oxfall.Core.Rules
{
    /// <summary>
    /// Falling: a drop of up to <see cref="SafeHeight"/> does nothing; past that each metre hurts more,
    /// and a fall of about <see cref="LethalHeight"/> kills outright. Clothes and armour do not help,
    /// and landing in deep water breaks the fall.
    /// </summary>
    public static class Falls
    {
        /// <summary>A drop this high (metres) hurts nothing: off a wall, out of a first-floor window.</summary>
        public const float SafeHeight = 4f;

        /// <summary>A drop this high kills a healthy survivor.</summary>
        public const float LethalHeight = 19f;

        /// <summary>The highest drop told: more is a teleport or a lie, and is taken as this.</summary>
        public const float MaxHeight = 120f;

        /// <summary>Health taken by a drop of <paramref name="height"/> metres.</summary>
        public static float Damage(float height)
        {
            if (float.IsNaN(height) || height <= SafeHeight) return 0f;
            height = Math.Min(height, MaxHeight);
            return (height - SafeHeight) * (Survival.Vitals.MaxHealth / (LethalHeight - SafeHeight));
        }
    }
}
