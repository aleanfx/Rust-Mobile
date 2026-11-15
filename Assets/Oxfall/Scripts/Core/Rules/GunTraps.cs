using System;
using Oxfall.Core.Items;

namespace Oxfall.Core.Rules
{
    /// <summary>
    /// The shotgun trap, as on the PC game: a blunderbuss on a box, loaded with shells, that fires at
    /// anyone who steps in front of it who is not its owner, in their team or authorized on the tool
    /// cupboard whose reach it stands in. Every shot spends a shell; empty, it is only a box. A base's
    /// door guard: no electricity needed.
    /// </summary>
    public static class GunTraps
    {
        /// <summary>How far it reaches, in metres.</summary>
        public const float Range = 8f;

        /// <summary>Half the width of what it covers, in degrees either side of where it points.</summary>
        public const float HalfCone = 55f;

        /// <summary>Seconds between two shots.</summary>
        public const float Interval = 1.5f;

        /// <summary>Shells it holds.</summary>
        public const int Slots = 6;

        /// <summary>A shot's damage up close (eight pellets), and up to how far it is all felt.</summary>
        public const float MaxDamage = 72f;
        public const float FullDamageRange = 4f;

        /// <summary>The share of the damage still felt at the far end of its reach.</summary>
        public const float FarShare = 0.3f;

        /// <summary>The height of its barrel above the ground it stands on.</summary>
        public const float MuzzleHeight = 0.55f;

        /// <summary>What it takes in: shells only.</summary>
        public static bool Takes(string itemId) => itemId == ItemIds.Shell;

        /// <summary>A shot's damage at this distance (before what the target wears takes its share).</summary>
        public static float DamageAt(float distance)
        {
            if (distance > Range) return 0f;
            if (distance <= FullDamageRange) return MaxDamage;
            float t = (distance - FullDamageRange) / (Range - FullDamageRange);
            return MaxDamage * (1f - t * (1f - FarShare));
        }

        /// <summary>Whether a point (a player's feet) is in front of the trap, within its reach and its cone.</summary>
        public static bool Covers(Deployable trap, float x, float y, float z)
        {
            float dx = x - trap.X, dz = z - trap.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 > Range * Range || Math.Abs(y - trap.Y) > 3f) return false;
            if (d2 < 0.04f) return true;
            double yaw = trap.Yaw * Math.PI / 180.0;
            double along = (dx * Math.Sin(yaw) + dz * Math.Cos(yaw)) / Math.Sqrt(d2);
            return along >= Math.Cos(HalfCone * Math.PI / 180.0);
        }

        /// <summary>
        /// Whether it leaves a player alone: its owner, someone in their team, or anyone authorized on
        /// the cupboard whose reach it stands in.
        /// </summary>
        public static bool Spares(Settlement settlement, Deployable trap, int owner, Func<int, int, bool> sameTeam)
        {
            if (owner == trap.Owner) return true;
            if (sameTeam != null && trap.Owner != 0 && sameTeam(owner, trap.Owner)) return true;
            return settlement.TryGetCupboardAt(trap.X, trap.Z, out var cupboard) && settlement.IsAuthorized(cupboard.Id, owner);
        }
    }
}
