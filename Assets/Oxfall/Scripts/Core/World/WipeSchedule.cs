using System;

namespace Oxfall.Core.World
{
    /// <summary>
    /// The weekly wipe: every Thursday at 19:00 UTC (3 pm in Venezuela, the genre's usual wipe day)
    /// the island starts over with a new seed, made from the wipe's date so every server agrees.
    /// </summary>
    public static class WipeSchedule
    {
        public const DayOfWeek Day = DayOfWeek.Thursday;
        public const int HourUtc = 19;

        /// <summary>When the current wipe started.</summary>
        public static DateTime LastWipe(DateTime utcNow)
        {
            var today = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, HourUtc, 0, 0, DateTimeKind.Utc);
            int back = ((int)today.DayOfWeek - (int)Day + 7) % 7;
            var wipe = today.AddDays(-back);
            return wipe > utcNow ? wipe.AddDays(-7) : wipe;
        }

        public static DateTime NextWipe(DateTime utcNow) => LastWipe(utcNow).AddDays(7);

        /// <summary>The island's seed for the wipe that started then: its date, as 20261112.</summary>
        public static int SeedFor(DateTime wipe) => wipe.Year * 10000 + wipe.Month * 100 + wipe.Day;
    }
}
