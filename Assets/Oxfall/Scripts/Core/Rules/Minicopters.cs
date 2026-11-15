using System;
using System.Collections.Generic;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    /// <summary>A one-seat helicopter: where it is, how it moves, its fuel, its health and who flies it.</summary>
    public sealed class Minicopter
    {
        public readonly int Id;
        /// <summary>Where it waits at the start of a wipe (and comes back to, rebuilt, after a crash).</summary>
        public readonly float HomeX, HomeY, HomeZ, HomeYaw;
        public float X, Y, Z, Yaw;
        public float Vx, Vy, Vz;
        public float Fuel;
        public float Health = Flying.MaxHealth;
        public ushort Driver;
        /// <summary>A wreck: when (server time) it comes back rebuilt at home.</summary>
        public float RebuiltAt;

        public Minicopter(int id, float x, float y, float z, float yaw)
        {
            Id = id;
            HomeX = X = x;
            HomeY = Y = y;
            HomeZ = Z = z;
            HomeYaw = Yaw = yaw;
            Fuel = Flying.StartFuel;
        }

        public bool Wrecked => Health <= 0f;

        /// <summary>Whether its rotor turns (someone at the controls and fuel in the tank).</summary>
        public bool Running => Driver != 0 && Fuel > 0f && !Wrecked;

        /// <summary>Puts it back at home, whole, with a little fuel.</summary>
        public void Rebuild()
        {
            X = HomeX; Y = HomeY; Z = HomeZ; Yaw = HomeYaw;
            Vx = Vy = Vz = 0f;
            Fuel = Flying.StartFuel;
            Health = Flying.MaxHealth;
            Driver = 0;
            RebuiltAt = 0f;
        }
    }

    /// <summary>
    /// Minicopters, made to fly with a phone: the stick flies it the way the pilot looks (look up and
    /// push forward to climb, down to come down), sideways slips it left or right, the jump button
    /// lifts it straight up, and with the stick let go it holds where it is. It burns fuel while the
    /// rotor turns off the ground. Out of fuel, or with nobody at the controls, it drops; hitting
    /// the ground hard hurts it and the pilot, and a wreck is rebuilt where it first stood.
    /// </summary>
    public static class Flying
    {
        public const float MaxSpeed = 20f;
        public const float StrafeSpeed = 8f;
        public const float ClimbSpeed = 6f;
        /// <summary>How quickly the velocity follows the stick (per second).</summary>
        public const float Response = 1.6f;
        public const float Gravity = 9.8f;
        public const float MaxAltitude = 140f;
        /// <summary>Falling slower than this onto the ground does no harm.</summary>
        public const float SafeLanding = 6f;
        /// <summary>Health lost (the copter's, and the pilot's at a third of it) per metre a second over the safe landing.</summary>
        public const float CrashDamage = 14f;
        public const float MaxHealth = 100f;
        /// <summary>Health lost a second while it sits sunk in the sea.</summary>
        public const float SinkDamage = 12f;
        public const float SecondsPerFuel = 4f;
        public const int TankSize = 100;
        public const int StartFuel = 25;
        public const float MountReach = 3.2f;
        /// <summary>The pilot's feet over the copter's skids.</summary>
        public const float SeatHeight = 0.45f;
        /// <summary>Seconds a wreck lies before it is rebuilt at home.</summary>
        public const float RebuildSeconds = 600f;

        /// <summary>What it rests on at (x, z): the ground, a ruin's concrete pad, or the sea's surface.</summary>
        public static float Floor(IslandGround ground, float x, float z) =>
            Math.Max(Monuments.SurfaceHeight(ground, x, z), IslandGround.WaterLevel);

        public static bool OverWater(IslandGround ground, float x, float z) =>
            Monuments.SurfaceHeight(ground, x, z) < IslandGround.WaterLevel;

        /// <summary>
        /// One step of flight. The stick (<paramref name="forward"/>, <paramref name="side"/>, each
        /// -1..1) flies along the look (<paramref name="lookYaw"/>, <paramref name="lookPitch"/> in
        /// degrees, pitch positive looking down); <paramref name="climb"/> lifts straight up. Gives
        /// back the speed it hit the ground at, over the safe landing (0: no crash).
        /// </summary>
        public static float Step(Minicopter m, float forward, float side, bool climb, float lookYaw, float lookPitch,
            float dt, IslandGround ground)
        {
            if (dt <= 0f || m.Wrecked) return 0f;
            forward = Clamp(forward, -1f, 1f);
            side = Clamp(side, -1f, 1f);
            float floor = Floor(ground, m.X, m.Z);
            bool grounded = m.Y <= floor + 0.05f;
            if (m.Running)
            {
                m.Yaw = Wrap(lookYaw);
                float yaw = m.Yaw * (float)Math.PI / 180f, pitch = Clamp(lookPitch, -80f, 80f) * (float)Math.PI / 180f;
                float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);
                float flat = (float)Math.Cos(pitch), down = (float)Math.Sin(pitch);
                // Backwards it goes back level, not up into the sky behind.
                float along = forward * (forward >= 0f ? MaxSpeed : StrafeSpeed);
                float tx = fx * along * (forward >= 0f ? flat : 1f) + fz * side * StrafeSpeed;
                float tz = fz * along * (forward >= 0f ? flat : 1f) - fx * side * StrafeSpeed;
                float ty = forward > 0f ? -down * along : 0f;
                if (climb) ty = Math.Max(ty, ClimbSpeed);
                ty = Clamp(ty, -MaxSpeed * 0.6f, ClimbSpeed * 1.5f);
                // Close over the ground it eases its descent by itself, so a phone pilot lands softly.
                if (m.Y - floor < 6f) ty = Math.Max(ty, -2.5f);
                float k = Math.Min(1f, Response * dt);
                m.Vx += (tx - m.Vx) * k;
                m.Vz += (tz - m.Vz) * k;
                m.Vy += (ty - m.Vy) * Math.Min(1f, Response * 1.5f * dt);
                if (!grounded || climb || ty > 0f || Math.Abs(forward) > 0.05f || Math.Abs(side) > 0.05f)
                    m.Fuel = Math.Max(0f, m.Fuel - dt / SecondsPerFuel);
            }
            else
            {
                // Nobody flying it, or no fuel: it drops, and drifts to a stop.
                m.Vy -= Gravity * dt;
                float drag = Math.Max(0f, 1f - 0.4f * dt);
                m.Vx *= drag;
                m.Vz *= drag;
            }
            float nx = m.X + m.Vx * dt, nz = m.Z + m.Vz * dt;
            float limit = IslandGround.HalfSize - 5f;
            if (Math.Abs(nx) > limit) { nx = Math.Sign(nx) * limit; m.Vx = 0f; }
            if (Math.Abs(nz) > limit) { nz = Math.Sign(nz) * limit; m.Vz = 0f; }
            float ny = Math.Min(MaxAltitude, m.Y + m.Vy * dt);
            if (ny >= MaxAltitude && m.Vy > 0f) m.Vy = 0f;
            float newFloor = Floor(ground, nx, nz);
            float crash = 0f;
            if (ny <= newFloor)
            {
                // A wall of ground ahead (flying into a hillside) counts as hitting it too.
                float impact = Math.Max(-m.Vy, (newFloor - ny) / dt * 0.5f);
                if (impact > SafeLanding) crash = impact - SafeLanding;
                ny = newFloor;
                if (m.Vy < 0f) m.Vy = 0f;
                // On the ground it does not slide.
                m.Vx *= 0.5f;
                m.Vz *= 0.5f;
            }
            m.X = nx;
            m.Y = ny;
            m.Z = nz;
            if (crash > 0f) m.Health = Math.Max(0f, m.Health - crash * CrashDamage);
            // Sat on the sea it sinks, unless it flies off again.
            if (!m.Running && ny <= newFloor + 0.01f && OverWater(ground, nx, nz))
                m.Health = Math.Max(0f, m.Health - SinkDamage * dt);
            return crash;
        }

        /// <summary>Whether a flown copter could have gone from where it was to here in this time.</summary>
        public static bool Believable(Minicopter m, float x, float y, float z, float dt, IslandGround ground, float slack = 2f)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z)) return false;
            dt = Math.Min(1f, Math.Max(0f, dt));
            float dx = x - m.X, dz = z - m.Z;
            float flat = MaxSpeed * dt + slack;
            if (dx * dx + dz * dz > flat * flat) return false;
            float up = y - m.Y;
            if (up > ClimbSpeed * 1.5f * dt + slack) return false;
            // Down as fast as it may dive, or fall.
            if (-up > Math.Max(MaxSpeed * 0.6f, Gravity * 2f) * dt + slack) return false;
            return y >= Floor(ground, x, z) - 0.5f && y <= MaxAltitude + 1f;
        }

        public static bool CanMount(Minicopter m, float x, float y, float z)
        {
            if (m.Driver != 0 || m.Wrecked) return false;
            float dx = x - m.X, dy = y - m.Y, dz = z - m.Z;
            return dx * dx + dz * dz <= MountReach * MountReach && Math.Abs(dy) < 2.5f;
        }

        public static void Seat(Minicopter m, out float x, out float y, out float z)
        {
            x = m.X;
            y = m.Y + SeatHeight;
            z = m.Z;
        }

        /// <summary>Where a pilot getting out stands: beside it, on the ground under that spot (or falling to it).</summary>
        public static void Landing(Minicopter m, IslandGround ground, out float x, out float y, out float z)
        {
            float side = (m.Yaw - 90f) * (float)Math.PI / 180f;
            x = m.X + (float)Math.Sin(side) * 1.6f;
            z = m.Z + (float)Math.Cos(side) * 1.6f;
            y = Math.Max(Monuments.SurfaceHeight(ground, x, z) + 0.05f, m.Y);
        }

        public static int TankSpace(Minicopter m) => Math.Max(0, TankSize - (int)Math.Ceiling(m.Fuel - 1e-3f));

        /// <summary>
        /// The minicopters of a wipe: one beside each ruin, on the flat ground at the edge of its pad
        /// (the same spots for a seed).
        /// </summary>
        public static List<Minicopter> Spawn(IslandGround ground)
        {
            var list = new List<Minicopter>();
            foreach (var mon in Monuments.Find(ground))
            {
                float yaw = mon.Yaw * (float)Math.PI / 180f;
                // Try round the pad for a spot that is land and fairly level.
                for (int k = 0; k < 12; k++)
                {
                    float a = yaw + k * (float)Math.PI / 6f;
                    float r = Monuments.Radius + 4f;
                    float x = mon.X + (float)Math.Sin(a) * r, z = mon.Z + (float)Math.Cos(a) * r;
                    float h = Monuments.SurfaceHeight(ground, x, z);
                    if (h < IslandGround.WaterLevel + 0.5f) continue;
                    float slope = Math.Abs(Monuments.SurfaceHeight(ground, x + 1.5f, z) - h) + Math.Abs(Monuments.SurfaceHeight(ground, x, z + 1.5f) - h);
                    if (slope > 0.9f && k < 11) continue;
                    list.Add(new Minicopter(list.Count + 1, x, h, z, Wrap(mon.Yaw + k * 30f)));
                    break;
                }
            }
            return list;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        private static float Wrap(float yaw)
        {
            yaw %= 360f;
            return yaw < 0f ? yaw + 360f : yaw;
        }
    }
}
