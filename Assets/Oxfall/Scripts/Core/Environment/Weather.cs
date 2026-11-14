using System;

namespace Oxfall.Core.Environment
{
    /// <summary>
    /// The island's weather, from its seed and the time: mostly fair, now and then rain blowing in
    /// for a while (heavy enough for thunder at times), and low fog some mornings and after rain.
    /// The same seed and time give the same weather on every machine; the server says what it is,
    /// alone the game works it out. Changes are slow: a shower takes minutes to come and to go.
    /// </summary>
    public static class Weather
    {
        /// <summary>How long one stretch of weather lasts before the next is drawn, seconds.</summary>
        public const float Spell = 420f;

        /// <summary>A fresh island starts fair for this long (the first steps are not taken in a storm).</summary>
        public const float FairStart = 600f;

        /// <summary>Rain this hard or harder brings thunder.</summary>
        public const float Storm = 0.75f;

        /// <summary>The weather at <paramref name="seconds"/> into the island's life: rain and fog, each 0..1.</summary>
        public static void At(int seed, double seconds, out float rain, out float fog)
        {
            if (seconds < FairStart)
            {
                rain = 0f;
                fog = 0f;
                return;
            }
            double t = (seconds - FairStart) / Spell;
            float wet = Smooth(seed, t, 0x51a3);
            float mist = Smooth(seed, t * 0.7 + 11.3, 0x2f91);
            // About a quarter of the time it rains; a few of those are storms.
            rain = Ease(Clamp01((wet - 0.6f) / 0.3f));
            // Fog: on its own some of the time, and with the rain.
            fog = Math.Max(rain * 0.65f, Ease(Clamp01((mist - 0.68f) / 0.22f)) * 0.8f);
        }

        /// <summary>Smooth noise over time: random values a spell apart, eased between.</summary>
        private static float Smooth(int seed, double t, int salt)
        {
            long i = (long)Math.Floor(t);
            float f = (float)(t - i);
            float a = Hash(seed, i, salt), b = Hash(seed, i + 1, salt);
            return a + (b - a) * Ease(f);
        }

        private static float Hash(int seed, long i, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)salt * 0x85EBCA77u;
                h ^= (uint)i * 0xC2B2AE3Du;
                h ^= (uint)(i >> 32) * 0x27D4EB2Fu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        private static float Ease(float x) => x * x * (3f - 2f * x);

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
