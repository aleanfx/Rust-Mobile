using System;

namespace Oxfall.Core.Environment
{
    /// <summary>
    /// The time of day on the island. The day (from <see cref="Sunrise"/> to <see cref="Sunset"/>)
    /// and the night each last a set number of real seconds, so nights can be kept short. The
    /// server will own the clock and send it to every player; this only counts.
    /// </summary>
    public sealed class DayCycle
    {
        public const float Sunrise = 6f;
        public const float Sunset = 20f;
        public const float DayHours = Sunset - Sunrise;
        public const float NightHours = 24f - DayHours;

        /// <summary>How high the sun gets at noon and the moon at midnight, in degrees.</summary>
        public const float SunPeak = 62f;
        public const float MoonPeak = 45f;

        /// <summary>Real seconds from sunrise to sunset, and from sunset to sunrise.</summary>
        public float DaySeconds { get; }
        public float NightSeconds { get; }

        /// <summary>The hour, from 0 up to (not including) 24.</summary>
        public float Hours { get; private set; }

        public DayCycle(float hours = 9f, float daySeconds = 1800f, float nightSeconds = 600f)
        {
            if (daySeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(daySeconds));
            if (nightSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(nightSeconds));
            DaySeconds = daySeconds;
            NightSeconds = nightSeconds;
            Set(hours);
        }

        public bool IsNight => Hours >= Sunset || Hours < Sunrise;

        /// <summary>Sets the clock, wrapping any hour into 0..24.</summary>
        public void Set(float hours)
        {
            hours %= 24f;
            if (hours < 0f) hours += 24f;
            Hours = hours;
        }

        /// <summary>Moves the clock on by some real seconds, at the day's or the night's pace.</summary>
        public void Tick(float seconds)
        {
            if (seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            // Step to each sunrise or sunset in turn, since the pace changes there.
            while (seconds > 0f)
            {
                bool night = IsNight;
                float hoursPerSecond = night ? NightHours / NightSeconds : DayHours / DaySeconds;
                float edge = night ? Sunrise : Sunset;
                float hoursToEdge = edge - Hours;
                if (hoursToEdge <= 0f) hoursToEdge += 24f;
                float secondsToEdge = hoursToEdge / hoursPerSecond;
                if (seconds < secondsToEdge)
                {
                    Set(Hours + seconds * hoursPerSecond);
                    return;
                }
                Set(edge);
                seconds -= secondsToEdge;
            }
        }

        /// <summary>How far through the day it is: 0 at sunrise, 1 at sunset, outside 0..1 at night.</summary>
        public float DayProgress => (Hours - Sunrise) / DayHours;

        /// <summary>How far through the night it is: 0 at sunset, 1 at sunrise; outside 0..1 by day.</summary>
        public float NightProgress
        {
            get
            {
                float sinceSunset = Hours - Sunset;
                if (sinceSunset < 0f) sinceSunset += 24f;
                return sinceSunset / NightHours;
            }
        }

        /// <summary>The sun's height above the horizon in degrees; 0 or less while it is down.</summary>
        public float SunElevation => IsNight ? 0f : (float)Math.Sin(Math.PI * DayProgress) * SunPeak;

        /// <summary>The moon's height in degrees; 0 or less by day.</summary>
        public float MoonElevation => IsNight ? (float)Math.Sin(Math.PI * NightProgress) * MoonPeak : 0f;

        /// <summary>
        /// How much daylight there is, 0 (night) to 1 (full day): it rises over the first hour after
        /// sunrise and falls over the last hour before sunset.
        /// </summary>
        public float Daylight
        {
            get
            {
                if (IsNight) return 0f;
                float fromEdge = Math.Min(Hours - Sunrise, Sunset - Hours);
                float t = Math.Min(1f, fromEdge);
                return t * t * (3f - 2f * t);
            }
        }

        /// <summary>The clock as the player reads it, "14:05".</summary>
        public string Clock
        {
            get
            {
                int minutes = (int)(Hours * 60f) % (24 * 60);
                return $"{minutes / 60:00}:{minutes % 60:00}";
            }
        }
    }
}
