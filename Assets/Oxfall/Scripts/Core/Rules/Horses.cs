using System;
using System.Collections.Generic;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    /// <summary>A horse: where it stands, how fast it goes, its wind, who rides it and how fed it is.</summary>
    public sealed class Horse
    {
        public readonly int Id;
        /// <summary>The meadow it grazes in at the start of a wipe (and wanders back to, riderless).</summary>
        public readonly float HomeX, HomeZ;
        public float X, Y, Z, Yaw;
        /// <summary>Metres a second along where it faces (negative: backing up).</summary>
        public float Speed;
        /// <summary>How long it can still gallop (0..<see cref="Riding.MaxStamina"/>).</summary>
        public float Stamina = Riding.MaxStamina;
        /// <summary>How fed it is (0..<see cref="Riding.MaxFood"/>): a hungry horse tires sooner.</summary>
        public float Food = Riding.MaxFood;
        public ushort Driver;

        public Horse(int id, float x, float y, float z, float yaw)
        {
            Id = id;
            HomeX = X = x;
            Y = y;
            HomeZ = Z = z;
            Yaw = yaw;
        }

        public bool Galloping => Driver != 0 && Speed > Riding.TrotSpeed + 0.5f;
    }

    /// <summary>
    /// Horses, ridden with a phone: the stick pushes it on the way the rider looks (a trot), sprint
    /// makes it gallop while it has wind; let go, it stops. It keeps to the ground: no swimming the
    /// sea, no climbing cliffs. Galloping uses up its wind, which comes back as it walks or rests,
    /// and it eats as it goes: fed berries it gallops for longer. Nobody riding it, it grazes.
    /// </summary>
    public static class Riding
    {
        public const float WalkSpeed = 2.5f;
        public const float TrotSpeed = 7.5f;
        public const float GallopSpeed = 12.5f;
        public const float BackSpeed = 2f;
        /// <summary>How quickly the speed follows the stick (metres a second, each second).</summary>
        public const float Acceleration = 9f;
        /// <summary>Degrees a second it turns towards where the rider looks, at most.</summary>
        public const float TurnRate = 200f;
        public const float MaxStamina = 100f;
        /// <summary>Wind used a second at a gallop, and won back a second otherwise.</summary>
        public const float StaminaDrain = 9f;
        public const float StaminaRegen = 6f;
        public const float MaxFood = 100f;
        /// <summary>Food used a second of riding.</summary>
        public const float FoodDrain = 0.25f;
        /// <summary>Food one berry gives (fed from the rider's pockets with the use button).</summary>
        public const float FoodPerBerry = 20f;
        /// <summary>Under this food it tires twice as fast.</summary>
        public const float Hungry = 25f;
        /// <summary>Deeper water than this (over the ground) and it will not go on.</summary>
        public const float MaxWade = 0.8f;
        /// <summary>The steepest rise it takes (metres up per metre on).</summary>
        public const float MaxSlope = 0.9f;
        public const float MountReach = 2.8f;
        /// <summary>The rider's feet over the ground the horse stands on.</summary>
        public const float SeatHeight = 1.15f;
        /// <summary>How far from home a grazing horse strays.</summary>
        public const float GrazeRange = 12f;

        /// <summary>The ground it walks on (or the bottom of shallow water).</summary>
        public static float Floor(IslandGround ground, float x, float z) => Monuments.SurfaceHeight(ground, x, z);

        /// <summary>Whether a horse can stand at (x, z): not deep in the sea, not off the island.</summary>
        public static bool Walkable(IslandGround ground, float x, float z)
        {
            float limit = IslandGround.HalfSize - 5f;
            if (Math.Abs(x) > limit || Math.Abs(z) > limit) return false;
            return Floor(ground, x, z) > IslandGround.WaterLevel - MaxWade;
        }

        /// <summary>
        /// One step of riding. <paramref name="forward"/> and <paramref name="side"/> (-1..1) are the
        /// stick; it turns towards <paramref name="lookYaw"/> as it goes; <paramref name="sprint"/>
        /// gallops while it has wind. Gives back whether it was stopped (by water or a slope).
        /// </summary>
        public static bool Step(Horse h, float forward, float side, bool sprint, float lookYaw, float dt, IslandGround ground)
        {
            if (dt <= 0f) return false;
            forward = Clamp(forward, -1f, 1f);
            side = Clamp(side, -1f, 1f);
            float push = Math.Max(Math.Abs(forward), Math.Abs(side)) ;
            bool gallop = sprint && forward > 0.3f && h.Stamina > 0.5f;
            float want = forward < -0.2f ? -BackSpeed * -forward
                : push < 0.05f ? 0f
                : gallop ? GallopSpeed
                : WalkSpeed + (TrotSpeed - WalkSpeed) * Math.Min(1f, push);
            h.Speed += Clamp(want - h.Speed, -Acceleration * 1.6f * dt, Acceleration * dt);
            // It heads where the rider looks (the stick sideways bends that, like reins).
            float heading = Wrap(lookYaw + (forward >= -0.2f ? side * 60f : 0f));
            if (Math.Abs(h.Speed) > 0.2f || push > 0.05f)
            {
                float turn = DeltaAngle(h.Yaw, heading);
                float most = TurnRate * dt;
                h.Yaw = Wrap(h.Yaw + Clamp(turn, -most, most));
            }

            // Wind: spent galloping (twice as fast hungry), won back otherwise; food goes as it is ridden.
            if (h.Galloping) h.Stamina = Math.Max(0f, h.Stamina - StaminaDrain * (h.Food < Hungry ? 2f : 1f) * dt);
            else h.Stamina = Math.Min(MaxStamina, h.Stamina + StaminaRegen * dt);
            if (h.Driver != 0) h.Food = Math.Max(0f, h.Food - FoodDrain * dt);

            float yaw = h.Yaw * (float)Math.PI / 180f;
            float nx = h.X + (float)Math.Sin(yaw) * h.Speed * dt, nz = h.Z + (float)Math.Cos(yaw) * h.Speed * dt;
            if (!Passable(ground, h.X, h.Z, nx, nz))
            {
                h.Speed = 0f;
                h.Y = Floor(ground, h.X, h.Z);
                return true;
            }
            h.X = nx;
            h.Z = nz;
            h.Y = Floor(ground, nx, nz);
            return false;
        }

        /// <summary>Whether it can go from one spot to the next: dry enough and not up a cliff.</summary>
        public static bool Passable(IslandGround ground, float x0, float z0, float x1, float z1)
        {
            if (!Walkable(ground, x1, z1)) return false;
            float run = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            if (run < 1e-4f) return true;
            float rise = Floor(ground, x1, z1) - Floor(ground, x0, z0);
            return rise <= Math.Max(0.15f, MaxSlope * run);
        }

        /// <summary>Whether a ridden horse could have gone from where it was to here in this time.</summary>
        public static bool Believable(Horse h, float x, float z, float dt, IslandGround ground, float slack = 1.5f)
        {
            if (float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z)) return false;
            dt = Math.Min(1f, Math.Max(0f, dt));
            float dx = x - h.X, dz = z - h.Z, far = GallopSpeed * 1.1f * dt + slack;
            return dx * dx + dz * dz <= far * far && Walkable(ground, x, z);
        }

        public static bool CanMount(Horse h, float x, float y, float z)
        {
            if (h.Driver != 0) return false;
            float dx = x - h.X, dy = y - h.Y, dz = z - h.Z;
            return dx * dx + dz * dz <= MountReach * MountReach && Math.Abs(dy) < 2.5f;
        }

        public static void Seat(Horse h, out float x, out float y, out float z)
        {
            x = h.X;
            y = h.Y + SeatHeight;
            z = h.Z;
        }

        /// <summary>Where a rider getting off stands: beside it on the left, on the ground.</summary>
        public static void Landing(Horse h, IslandGround ground, out float x, out float y, out float z)
        {
            float side = (h.Yaw - 90f) * (float)Math.PI / 180f;
            x = h.X + (float)Math.Sin(side) * 1.3f;
            z = h.Z + (float)Math.Cos(side) * 1.3f;
            if (!Walkable(ground, x, z)) { x = h.X; z = h.Z; }
            y = Floor(ground, x, z) + 0.05f;
        }

        /// <summary>Berries fed to it from <paramref name="berries"/> carried: how many it eats (until full).</summary>
        public static int Feed(Horse h, int berries)
        {
            int eats = Math.Min(berries, (int)Math.Ceiling((MaxFood - h.Food) / FoodPerBerry - 1e-3f));
            if (eats <= 0) return 0;
            h.Food = Math.Min(MaxFood, h.Food + eats * FoodPerBerry);
            // A fed horse gets its wind back at once.
            h.Stamina = Math.Min(MaxStamina, h.Stamina + eats * 15f);
            return eats;
        }

        /// <summary>
        /// Nobody riding it: it grazes, now and then ambling a few steps, back towards home if it
        /// was ridden away (<paramref name="roll"/>, 0..1, picks when it moves on).
        /// </summary>
        public static void Graze(Horse h, float dt, float roll, IslandGround ground)
        {
            h.Stamina = Math.Min(MaxStamina, h.Stamina + StaminaRegen * dt);
            float dx = h.HomeX - h.X, dz = h.HomeZ - h.Z;
            float away = (float)Math.Sqrt(dx * dx + dz * dz);
            bool amble = h.Speed > 0.1f ? roll > 0.004f : roll < 0.004f;
            if (!amble)
            {
                h.Speed = 0f;
                return;
            }
            if (h.Speed <= 0.1f)
            {
                // Sets off: home when far from it, else somewhere near.
                h.Yaw = away > GrazeRange
                    ? Wrap((float)(Math.Atan2(dx, dz) * 180.0 / Math.PI))
                    : Wrap(h.Yaw + (roll * 250000f % 140f) - 70f);
            }
            h.Speed = 1.1f;
            float yaw = h.Yaw * (float)Math.PI / 180f;
            float nx = h.X + (float)Math.Sin(yaw) * h.Speed * dt, nz = h.Z + (float)Math.Cos(yaw) * h.Speed * dt;
            if (!Passable(ground, h.X, h.Z, nx, nz) || IslandGround.WaterLevel + 0.4f > Floor(ground, nx, nz))
            {
                h.Speed = 0f;
                h.Yaw = Wrap(h.Yaw + 120f);
                return;
            }
            h.X = nx;
            h.Z = nz;
            h.Y = Floor(ground, nx, nz);
        }

        /// <summary>
        /// The horses of a wipe: a few, in open grassy ground away from the shore and the ruins (the
        /// same spots for a seed). <paramref name="blocked"/> says where trees and rocks stand.
        /// </summary>
        public static List<Horse> Spawn(IslandGround ground, int seed, Func<float, float, bool> blocked = null, int count = 6)
        {
            var list = new List<Horse>();
            var random = new Random(seed ^ 0x5a17e);
            for (int tries = 0; tries < 4000 && list.Count < count; tries++)
            {
                float x = (float)(random.NextDouble() * 2 - 1) * (IslandGround.HalfSize - 40f);
                float z = (float)(random.NextDouble() * 2 - 1) * (IslandGround.HalfSize - 40f);
                float h = ground.Height(x, z);
                if (h < IslandGround.WaterLevel + 2f || Monuments.NoBuilding(ground, x, z)) continue;
                float slope = Math.Abs(ground.Height(x + 2f, z) - h) + Math.Abs(ground.Height(x, z + 2f) - h);
                if (slope > 0.8f) continue;
                if (blocked != null && blocked(x, z)) continue;
                bool crowded = false;
                foreach (var other in list)
                    if ((other.X - x) * (other.X - x) + (other.Z - z) * (other.Z - z) < 40f * 40f) crowded = true;
                if (crowded) continue;
                list.Add(new Horse(list.Count + 1, x, Floor(ground, x, z), z, (float)random.NextDouble() * 360f));
            }
            return list;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        private static float DeltaAngle(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        private static float Wrap(float yaw)
        {
            yaw %= 360f;
            return yaw < 0f ? yaw + 360f : yaw;
        }
    }
}
