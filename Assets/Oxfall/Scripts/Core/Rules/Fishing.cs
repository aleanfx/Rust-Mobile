using System;
using Oxfall.Core.Items;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    /// <summary>What reeling in brought up (and, as <see cref="Bite"/>, the moment a fish takes the hook).</summary>
    public enum FishCatch : byte
    {
        /// <summary>Reeled in before anything bit: the line comes back empty.</summary>
        Nothing = 0,
        /// <summary>Reeled in too late: the fish took the bait and got away.</summary>
        Escaped = 1,
        Small = 2,
        Trout = 3,
        Salmon = 4,
        /// <summary>Not a catch: a fish has just taken the hook (reel in now).</summary>
        Bite = 5,
        /// <summary>Not a catch: the line was given up (walked away, put the rod down).</summary>
        Lost = 6,
    }

    /// <summary>
    /// Simple fishing: cast the rod at water deep enough (the sea or a pond), wait for the float to
    /// dip, and reel in while it is under: a small fish most times, a trout now and then, rarely a
    /// big salmon. Reel too soon and the line comes back empty; too late and the fish is gone. Raw
    /// fish roasts at a campfire like meat. The server keeps one <see cref="Angler"/> per player.
    /// </summary>
    public static class Fishing
    {
        /// <summary>The farthest the float flies from the player, and the nearest it lands.</summary>
        public const float CastRange = 14f;
        public const float MinCast = 3f;

        /// <summary>How deep the water under the float must be.</summary>
        public const float MinDepth = 0.6f;

        /// <summary>Seconds before a fish bites: between these.</summary>
        public const float BiteMin = 5f;
        public const float BiteMax = 14f;

        /// <summary>Seconds the float stays under: reel in within them.</summary>
        public const float BiteWindow = 1.8f;

        /// <summary>Extra the server allows on the window, for the message's trip.</summary>
        public const float LagSlack = 0.7f;

        /// <summary>Walk this far from the float and the line is lost.</summary>
        public const float LineLength = CastRange + 5f;

        /// <summary>The surface of deep enough water at (x, z), if there is any there.</summary>
        public static bool WaterAt(IslandGround ground, float x, float z, out float surface)
        {
            surface = IslandGround.WaterLevel;
            if (ground == null) return false;
            foreach (var pond in ground.Ponds)
            {
                float dx = x - pond.X, dz = z - pond.Z;
                if (dx * dx + dz * dz > pond.Radius * pond.Radius * 0.7f) continue;
                if (ground.Height(x, z) > pond.Level - MinDepth * 0.5f) continue;
                surface = pond.Level;
                return true;
            }
            return ground.Height(x, z) < IslandGround.WaterLevel - MinDepth;
        }

        /// <summary>Whether a float may land at (bx, bz) cast from feet at (x, y, z).</summary>
        public static ActionResult CanCast(IslandGround ground, float x, float y, float z, float bx, float bz)
        {
            float dx = bx - x, dz = bz - z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d > CastRange + 1f) return ActionResult.TooFar;
            if (d < MinCast - 0.5f) return ActionResult.TooClose;
            if (!WaterAt(ground, bx, bz, out float surface)) return ActionResult.NoWater;
            if (y - surface > CastRange) return ActionResult.TooHigh;
            return ActionResult.Ok;
        }

        /// <summary>Seconds until a fish bites, from a seed.</summary>
        public static float BiteSeconds(int seed) => BiteMin + (BiteMax - BiteMin) * Unit(seed, 1);

        /// <summary>What a fish caught in time turns out to be, from a seed: 65% small, 27% trout, 8% salmon.</summary>
        public static FishCatch Kind(int seed)
        {
            float roll = Unit(seed, 2);
            return roll < 0.65f ? FishCatch.Small : roll < 0.92f ? FishCatch.Trout : FishCatch.Salmon;
        }

        /// <summary>Pieces of raw fish a catch gives.</summary>
        public static int Pieces(FishCatch c) => c == FishCatch.Small ? 1 : c == FishCatch.Trout ? 2 : c == FishCatch.Salmon ? 4 : 0;

        public static bool IsFish(FishCatch c) => c == FishCatch.Small || c == FishCatch.Trout || c == FishCatch.Salmon;

        /// <summary>What the player is told.</summary>
        public static string Name(FishCatch c)
        {
            switch (c)
            {
                case FishCatch.Small: return "un pez pequeño";
                case FishCatch.Trout: return "una trucha";
                case FishCatch.Salmon: return "¡un salmón grande!";
                default: return "nada";
            }
        }

        private static float Unit(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u + (uint)salt * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }

    /// <summary>One player's line in the water: where the float is, and when a fish bites.</summary>
    public sealed class Angler
    {
        public bool Casting { get; private set; }
        public float X { get; private set; }
        public float Z { get; private set; }
        public float Surface { get; private set; }
        public float BiteAt { get; private set; }
        private int _seed;

        /// <summary>The bite has been told (the server says it once).</summary>
        public bool Told { get; set; }

        /// <summary>Casts from feet at (x, y, z) to (bx, bz) at time <paramref name="now"/>.</summary>
        public ActionResult Cast(IslandGround ground, float x, float y, float z, float bx, float bz, float now, int seed)
        {
            var r = Fishing.CanCast(ground, x, y, z, bx, bz);
            if (r != ActionResult.Ok) return r;
            Fishing.WaterAt(ground, bx, bz, out float surface);
            Casting = true;
            X = bx;
            Z = bz;
            Surface = surface;
            _seed = seed;
            BiteAt = now + Fishing.BiteSeconds(seed);
            Told = false;
            return ActionResult.Ok;
        }

        /// <summary>Whether the float is under now (a fish on the hook).</summary>
        public bool Biting(float now) => Casting && now >= BiteAt && now <= BiteAt + Fishing.BiteWindow;

        /// <summary>The fish has had the bait and gone.</summary>
        public bool Missed(float now, float slack = 0f) => Casting && now > BiteAt + Fishing.BiteWindow + slack;

        /// <summary>Whether the player at (x, z) has walked too far from the float.</summary>
        public bool TooFarFrom(float x, float z)
        {
            float dx = x - X, dz = z - Z;
            return dx * dx + dz * dz > Fishing.LineLength * Fishing.LineLength;
        }

        /// <summary>Reels in at <paramref name="now"/> (with <paramref name="slack"/> for lag): what came up.</summary>
        public FishCatch Reel(float now, float slack = 0f)
        {
            if (!Casting) return FishCatch.Nothing;
            Casting = false;
            if (now < BiteAt - slack * 0.5f) return FishCatch.Nothing;
            if (now > BiteAt + Fishing.BiteWindow + slack) return FishCatch.Escaped;
            return Fishing.Kind(_seed);
        }

        /// <summary>Takes the line out without reeling (put the rod away, walked off, died).</summary>
        public void Stop() => Casting = false;

        /// <summary>
        /// Puts what was caught in the inventory and wears the rod: false when it does not fit (the
        /// fish slips back). Shared by the server and the game alone.
        /// </summary>
        public static bool Land(Survivor s, FishCatch c)
        {
            int n = Fishing.Pieces(c);
            if (n <= 0) return false;
            if (s.Inventory.SpaceFor(ItemIds.RawFish) < n) return false;
            s.Inventory.Add(ItemIds.RawFish, n);
            return true;
        }
    }
}
