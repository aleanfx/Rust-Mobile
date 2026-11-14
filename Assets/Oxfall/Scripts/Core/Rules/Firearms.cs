using System;
using Oxfall.Core.Items;

namespace Oxfall.Core.Rules
{
    /// <summary>
    /// What a gun is like: the round it fires, how many pellets a shot holds and how hard each hits,
    /// how far it carries, how fast it fires and how many shots before it must be reloaded.
    /// </summary>
    public sealed class GunSpec
    {
        public ToolKind Tool { get; }
        public string Ammo { get; }
        public int Pellets { get; }
        public float PelletDamage { get; }

        /// <summary>How far each pellet strays from the crosshair, at most, in degrees.</summary>
        public float SpreadDegrees { get; }

        /// <summary>Full damage up to this far; then less and less, down to <see cref="FarShare"/> at <see cref="Range"/>; nothing past it.</summary>
        public float FullDamageRange { get; }
        public float Range { get; }
        public float FarShare { get; }

        /// <summary>The least time between two shots, and how many before a reload (and how long that takes).</summary>
        public float FireSeconds { get; }
        public int Capacity { get; }
        public float ReloadSeconds { get; }

        public GunSpec(ToolKind tool, string ammo, int pellets, float pelletDamage, float spreadDegrees,
            float fullDamageRange, float range, float farShare, float fireSeconds, int capacity, float reloadSeconds)
        {
            Tool = tool;
            Ammo = ammo;
            Pellets = pellets;
            PelletDamage = pelletDamage;
            SpreadDegrees = spreadDegrees;
            FullDamageRange = fullDamageRange;
            Range = range;
            FarShare = farShare;
            FireSeconds = fireSeconds;
            Capacity = capacity;
            ReloadSeconds = reloadSeconds;
        }

        /// <summary>The most one shot can take off a target right in front of the muzzle.</summary>
        public float MaxDamage => Pellets * PelletDamage;
    }

    /// <summary>
    /// The guns, after the PC game's first ones: a pipe shotgun that ends a fight at a few metres and
    /// does little at thirty, and a revolver that takes four body shots and reaches across a field.
    /// Each shot spends one round from the backpack; the gun holds a few before it must be reloaded.
    /// </summary>
    public static class Firearms
    {
        public static readonly GunSpec Blunderbuss = new GunSpec(ToolKind.Shotgun, ItemIds.Shell,
            pellets: 8, pelletDamage: 11f, spreadDegrees: 4.5f, fullDamageRange: 6f, range: 30f, farShare: 0.2f,
            fireSeconds: 0.6f, capacity: 1, reloadSeconds: 2.2f);

        public static readonly GunSpec Revolver = new GunSpec(ToolKind.Revolver, ItemIds.PistolBullet,
            pellets: 1, pelletDamage: 30f, spreadDegrees: 0.7f, fullDamageRange: 20f, range: 90f, farShare: 0.45f,
            fireSeconds: 0.3f, capacity: 6, reloadSeconds: 3f);

        /// <summary>The top gun: fast and true at range, three body shots, sixteen to a magazine.</summary>
        public static readonly GunSpec Rifle = new GunSpec(ToolKind.Rifle, ItemIds.RifleBullet,
            pellets: 1, pelletDamage: 38f, spreadDegrees: 0.35f, fullDamageRange: 45f, range: 160f, farShare: 0.55f,
            fireSeconds: 0.2f, capacity: 16, reloadSeconds: 3.4f);

        /// <summary>How long after a shot its hits may still be reported (lag).</summary>
        public const float HitWindowSeconds = 1.5f;

        /// <summary>Room the server allows round a target for where each side saw the other a moment ago.</summary>
        public const float LagSlack = 1.2f;

        public static bool IsGun(ToolKind tool) => tool == ToolKind.Shotgun || tool == ToolKind.Revolver || tool == ToolKind.Rifle;

        public static bool TryGet(ToolKind tool, out GunSpec gun)
        {
            switch (tool)
            {
                case ToolKind.Shotgun: gun = Blunderbuss; return true;
                case ToolKind.Revolver: gun = Revolver; return true;
                case ToolKind.Rifle: gun = Rifle; return true;
                default: gun = null; return false;
            }
        }

        /// <summary>The share of its damage a pellet still has this far from the muzzle.</summary>
        public static float Falloff(GunSpec gun, float distance)
        {
            if (distance >= gun.Range) return 0f;
            if (distance <= gun.FullDamageRange) return 1f;
            float t = (distance - gun.FullDamageRange) / (gun.Range - gun.FullDamageRange);
            return 1f + (gun.FarShare - 1f) * t;
        }

        /// <summary>What this many pellets take off a target this far away, before what it wears.</summary>
        public static float Damage(GunSpec gun, int pellets, float distance) =>
            Math.Max(0, Math.Min(pellets, gun.Pellets)) * gun.PelletDamage * Falloff(gun, distance);

        /// <summary>
        /// Where pellet <paramref name="index"/> of a shot goes, off the crosshair (yaw, pitch in degrees):
        /// the same for every copy given the shot's seed, so whoever watches sees the same spray.
        /// </summary>
        public static (float Yaw, float Pitch) PelletOffset(GunSpec gun, int seed, int index)
        {
            if (gun.Pellets <= 1 && index == 0)
            {
                // One bullet strays a little, the same way for everyone.
                float a1 = Hash(seed, 0) * 6.2831853f, r1 = (float)Math.Sqrt(Hash(seed, 1)) * gun.SpreadDegrees;
                return (r1 * (float)Math.Cos(a1), r1 * (float)Math.Sin(a1));
            }
            // A ring of pellets round a middle one, each nudged at random.
            float angle = (index + Hash(seed, index * 2) * 0.6f) / Math.Max(1, gun.Pellets - 1) * 6.2831853f + Hash(seed, 99) * 6.2831853f;
            float radius = index == 0 ? Hash(seed, 7) * 0.25f : 0.45f + Hash(seed, index * 2 + 1) * 0.55f;
            radius *= gun.SpreadDegrees;
            return (radius * (float)Math.Cos(angle), radius * (float)Math.Sin(angle));
        }

        /// <summary>The way a shot points, from its yaw and pitch in degrees (yaw 0: +Z; pitch up is positive).</summary>
        public static (float X, float Y, float Z) Direction(float yaw, float pitch)
        {
            double y = yaw * Math.PI / 180.0, p = pitch * Math.PI / 180.0;
            return ((float)(Math.Sin(y) * Math.Cos(p)), (float)Math.Sin(p), (float)(Math.Cos(y) * Math.Cos(p)));
        }

        /// <summary>
        /// Whether a target standing at (tx, ty, tz) (its feet) and <paramref name="height"/> tall could
        /// have been hit by a shot from (ox, oy, oz) along yaw/pitch: in range, and inside the spray (with
        /// room for its body and for lag).
        /// </summary>
        public static bool CouldHit(GunSpec gun, float ox, float oy, float oz, float yaw, float pitch,
            float tx, float ty, float tz, float height)
        {
            var d = Direction(yaw, pitch);
            // The point of the target's middle line nearest the shot's line.
            float best = float.MaxValue, along = 0f;
            for (int i = 0; i <= 4; i++)
            {
                float py = ty + height * i / 4f;
                float vx = tx - ox, vy = py - oy, vz = tz - oz;
                float t = vx * d.X + vy * d.Y + vz * d.Z;
                if (t < -0.5f) continue;
                float cx = vx - d.X * t, cy = vy - d.Y * t, cz = vz - d.Z * t;
                float off = (float)Math.Sqrt(cx * cx + cy * cy + cz * cz);
                if (off < best)
                {
                    best = off;
                    along = Math.Max(0f, t);
                }
            }
            if (best == float.MaxValue || along > gun.Range + LagSlack) return false;
            float cone = along * (float)Math.Tan((gun.SpreadDegrees * 1.5f + 1.5f) * Math.PI / 180.0);
            return best <= cone + 0.6f + LagSlack;
        }

        private static float Hash(int seed, int i)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)(i + 1) * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }

    /// <summary>
    /// A gun in someone's hands: how many rounds it has before a reload, when it last fired and when
    /// the reload is done. The server keeps one per player and gun; the phone keeps its own to match.
    /// </summary>
    public sealed class GunState
    {
        public int Rounds { get; private set; } = -1;
        public float LastShot { get; private set; } = float.NegativeInfinity;
        public float ReloadEndsAt { get; private set; } = float.NegativeInfinity;

        /// <summary>Whether it is being reloaded at <paramref name="now"/>.</summary>
        public bool Reloading(float now) => Rounds == 0 && now < ReloadEndsAt;

        /// <summary>Rounds in it now (a finished reload fills it).</summary>
        public int Loaded(GunSpec gun, float now) => Rounds < 0 || (Rounds == 0 && now >= ReloadEndsAt) ? gun.Capacity : Rounds;

        /// <summary>
        /// Whether it can fire at <paramref name="now"/>: loaded, and not too soon after the last shot
        /// (<paramref name="leeway"/> shaves the waits, for a server hearing a phone through lag).
        /// </summary>
        public bool CanFire(GunSpec gun, float now, float leeway = 1f) =>
            !(Rounds == 0 && now < ReloadEndsAt - gun.ReloadSeconds * (1f - leeway))
            && now - LastShot >= gun.FireSeconds * leeway;

        /// <summary>Fires one round (the caller has checked <see cref="CanFire"/>); the last one starts the reload.</summary>
        public void Fire(GunSpec gun, float now)
        {
            int rounds = Rounds <= 0 ? gun.Capacity : Rounds;
            Rounds = rounds - 1;
            LastShot = now;
            if (Rounds == 0) ReloadEndsAt = now + gun.ReloadSeconds;
        }

        /// <summary>Starts a reload early (from a half-empty gun), unless it is full or already reloading.</summary>
        public bool Reload(GunSpec gun, float now)
        {
            int loaded = Loaded(gun, now);
            if (loaded >= gun.Capacity || Rounds == 0) return false;
            Rounds = 0;
            ReloadEndsAt = now + gun.ReloadSeconds;
            return true;
        }
    }
}
