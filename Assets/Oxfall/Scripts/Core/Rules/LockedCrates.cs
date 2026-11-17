using System;
using System.Collections.Generic;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    /// <summary>
    /// Locked crates, as on the PC game: now and then a heavy military crate turns up in the yard of
    /// one of the outposts, and everyone hears of it. Whoever reaches it starts hacking it open, which
    /// takes minutes and wakes the guards; it opens on its own when the time is up, to whoever is
    /// standing there then, with the best loot on the island. Left alone, it is taken away again.
    /// </summary>
    public sealed class LockedCrates
    {
        /// <summary>How long a hack takes (a quarter hour on the PC game: five minutes suits a phone).</summary>
        public const float HackSeconds = 300f;

        /// <summary>How long it lies there once open, before it rots away (sooner once emptied).</summary>
        public const float OpenSeconds = 900f;

        /// <summary>How long it waits for someone to start hacking it before it is taken away.</summary>
        public const float UnhackedSeconds = 2700f;

        /// <summary>How far round the crate guards hear it start and come for the one hacking it.</summary>
        public const float AlarmRadius = 60f;

        /// <summary>The first some minutes into a fresh island, then one every <see cref="Every"/> give or take <see cref="Jitter"/>.</summary>
        public const float FirstAfter = 1200f, Every = 3600f, Jitter = 600f;

        /// <summary>After a crate goes, at least this long before the next.</summary>
        public const float Respite = 900f;

        private readonly Random _random;
        private float _next;

        public LockedCrates(int seed, float now = 0f)
        {
            _random = new Random(seed ^ 0x10cced);
            _next = now + FirstAfter;
        }

        /// <summary>When the next crate turns up (once none is left on the island).</summary>
        public float Next => _next;

        /// <summary>
        /// Time moves on to <paramref name="now"/>; <paramref name="present"/>: whether a locked crate is on
        /// the island already. True when a new one should be set down now.
        /// </summary>
        public bool Due(float now, bool present)
        {
            if (present)
            {
                _next = Math.Max(_next, now + Respite);
                return false;
            }
            if (now < _next) return false;
            _next = now + Every + ((float)_random.NextDouble() * 2f - 1f) * Jitter;
            return true;
        }

        /// <summary>Where the next crate goes: the yard of one of the outposts (any ruin when there is none). False on an island without ruins.</summary>
        public bool PickSpot(IslandGround ground, out float x, out float y, out float z, out float yaw)
        {
            var monuments = Monuments.Find(ground);
            var outposts = new List<Monument>();
            foreach (var m in monuments)
                if (m.Kind == MonumentKind.Outpost) outposts.Add(m);
            if (outposts.Count == 0) outposts.AddRange(monuments);
            if (outposts.Count == 0)
            {
                x = y = z = yaw = 0f;
                return false;
            }
            (x, y, z, yaw) = Monuments.CrateSpot(outposts[_random.Next(outposts.Count)]);
            return true;
        }

        /// <summary>The ruin a point lies in, by name (null when in none), for telling everyone where a crate is.</summary>
        public static string PlaceName(IslandGround ground, float x, float z)
        {
            foreach (var m in Monuments.Find(ground))
            {
                float r = Monuments.Radius + 4f;
                if ((m.X - x) * (m.X - x) + (m.Z - z) * (m.Z - z) < r * r) return m.Name;
            }
            return null;
        }

        /// <summary>"4:59": what the crate's screen shows with <paramref name="left"/> seconds to go.</summary>
        public static string Clock(float left)
        {
            int s = Math.Max(0, (int)Math.Ceiling(left - 1e-3f));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
