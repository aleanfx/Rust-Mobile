using System;
using System.Collections.Generic;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    /// <summary>A rowboat on the sea: where it floats, which way it points, how fast it goes and who rows it.</summary>
    public sealed class Boat
    {
        public readonly int Id;
        public float X, Z, Yaw, Speed;

        /// <summary>The player rowing it (0: nobody, it drifts to a stop).</summary>
        public ushort Driver;

        public Boat(int id, float x, float z, float yaw)
        {
            Id = id;
            X = x;
            Z = z;
            Yaw = yaw;
        }
    }

    /// <summary>
    /// Rowboats: a few float off the shores of the island from the start of each wipe. Walk up to
    /// one, get in, and row: forward to speed up, back to slow down and reverse, sideways to turn
    /// (only while moving, as a boat does). It floats only where the sea is deep enough, so it runs
    /// aground on the beach and stops; getting out puts the rower on the nearest dry land if there
    /// is any close by, else in the water beside it. The rower's phone moves the boat; the server
    /// believes it only at a boat's speed and on water.
    /// </summary>
    public static class Boating
    {
        public const float MaxSpeed = 8f;
        public const float MaxReverse = 2.5f;
        public const float Acceleration = 3f;
        /// <summary>Speed lost per second with the oars still.</summary>
        public const float Drag = 1.6f;
        /// <summary>Degrees per second at full speed (and a little standing still, to point it).</summary>
        public const float TurnRate = 55f;
        public const float StillTurnRate = 18f;
        /// <summary>The sea must be this deep under it.</summary>
        public const float Draft = 0.5f;
        /// <summary>How near the player must stand (or swim) to get in.</summary>
        public const float BoardReach = 3.5f;
        /// <summary>The rower's feet over the water line.</summary>
        public const float SeatHeight = 0.25f;
        /// <summary>How far from the boat the rower may step out onto land.</summary>
        public const float LandingReach = 7f;
        public const int Count = 6;

        /// <summary>Whether a boat floats at (x, z).</summary>
        public static bool Floats(IslandGround ground, float x, float z)
        {
            float limit = IslandGround.HalfSize - 12f;
            if (Math.Abs(x) > limit || Math.Abs(z) > limit) return false;
            return ground.Height(x, z) < IslandGround.WaterLevel - Draft;
        }

        /// <summary>
        /// Rows one step: <paramref name="throttle"/> forward (positive) or back, <paramref name="steer"/>
        /// right (positive) or left, both from -1 to 1. Runs aground (stops dead) rather than leave the water.
        /// </summary>
        public static void Step(Boat boat, float throttle, float steer, float dt, IslandGround ground)
        {
            if (dt <= 0f) return;
            throttle = Clamp(throttle, -1f, 1f);
            steer = Clamp(steer, -1f, 1f);
            float target = throttle >= 0f ? throttle * MaxSpeed : throttle * MaxReverse;
            if (Math.Abs(throttle) < 0.05f)
                boat.Speed = boat.Speed > 0f ? Math.Max(0f, boat.Speed - Drag * dt) : Math.Min(0f, boat.Speed + Drag * dt);
            else if (boat.Speed < target)
                boat.Speed = Math.Min(target, boat.Speed + Acceleration * dt);
            else
                boat.Speed = Math.Max(target, boat.Speed - Acceleration * 1.5f * dt);
            float speedShare = Math.Min(1f, Math.Abs(boat.Speed) / MaxSpeed);
            float turn = steer * (StillTurnRate + (TurnRate - StillTurnRate) * speedShare) * dt;
            // Going backwards the bow swings the other way, as with any boat.
            boat.Yaw = Wrap(boat.Yaw + (boat.Speed < 0f ? -turn : turn));
            float rad = boat.Yaw * (float)Math.PI / 180f;
            float nx = boat.X + (float)Math.Sin(rad) * boat.Speed * dt;
            float nz = boat.Z + (float)Math.Cos(rad) * boat.Speed * dt;
            if (!Floats(ground, nx, nz))
            {
                boat.Speed = 0f;
                return;
            }
            boat.X = nx;
            boat.Z = nz;
        }

        /// <summary>Whether a rowed boat could have gone from where it was to (x, z) in this time.</summary>
        public static bool Believable(Boat boat, float x, float z, float dt, IslandGround ground, float slack = 1.5f)
        {
            if (float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z)) return false;
            float dx = x - boat.X, dz = z - boat.Z;
            float allowed = MaxSpeed * Math.Min(1f, Math.Max(0f, dt)) + slack;
            return dx * dx + dz * dz <= allowed * allowed && Floats(ground, x, z);
        }

        /// <summary>Whether a player standing (or swimming) at this spot can get into the boat.</summary>
        public static bool CanBoard(Boat boat, float x, float y, float z)
        {
            if (boat.Driver != 0) return false;
            float dx = x - boat.X, dz = z - boat.Z;
            return dx * dx + dz * dz <= BoardReach * BoardReach && y < IslandGround.WaterLevel + 3f;
        }

        /// <summary>Where the rower's feet go while in it.</summary>
        public static void Seat(Boat boat, out float x, out float y, out float z)
        {
            x = boat.X;
            y = IslandGround.WaterLevel + SeatHeight;
            z = boat.Z;
        }

        /// <summary>Where a rower getting out stands: the nearest dry land near by, else the water beside the boat.</summary>
        public static void Landing(Boat boat, IslandGround ground, out float x, out float y, out float z)
        {
            float bestD = float.MaxValue;
            x = z = 0f;
            for (float r = 1.5f; r <= LandingReach; r += 0.75f)
            {
                for (int k = 0; k < 16; k++)
                {
                    float a = k * (float)Math.PI / 8f;
                    float px = boat.X + (float)Math.Sin(a) * r, pz = boat.Z + (float)Math.Cos(a) * r;
                    if (ground.Height(px, pz) < IslandGround.WaterLevel + 0.15f) continue;
                    if (r < bestD) { bestD = r; x = px; z = pz; }
                }
                if (bestD < float.MaxValue) break;
            }
            if (bestD < float.MaxValue)
            {
                y = ground.Height(x, z) + 0.05f;
                return;
            }
            // Over the side, into the sea to the right of it.
            float side = (boat.Yaw + 90f) * (float)Math.PI / 180f;
            x = boat.X + (float)Math.Sin(side) * 1.6f;
            z = boat.Z + (float)Math.Cos(side) * 1.6f;
            y = IslandGround.WaterLevel - 1.3f;
        }

        /// <summary>
        /// The boats of a wipe: <see cref="Count"/> spread round the island, each just off a beach
        /// (the same spots for a seed), bow out to sea.
        /// </summary>
        public static List<Boat> Spawn(IslandGround ground)
        {
            var boats = new List<Boat>();
            float turn = (ground.Seed % 360 + 360) % 360;
            for (int k = 0; k < Count; k++)
            {
                float angle = (turn + k * 360f / Count) * (float)Math.PI / 180f;
                float sx = (float)Math.Sin(angle), sz = (float)Math.Cos(angle);
                bool seenLand = false;
                for (float r = 20f; r < IslandGround.HalfSize - 20f; r += 2f)
                {
                    float x = sx * r, z = sz * r;
                    if (ground.Height(x, z) > IslandGround.WaterLevel + 0.3f) { seenLand = true; continue; }
                    if (!seenLand) continue;
                    // A little out from where the land ends, where it is deep enough to float.
                    float bx = sx * (r + 5f), bz = sz * (r + 5f);
                    if (!Floats(ground, bx, bz)) continue;
                    boats.Add(new Boat(k + 1, bx, bz, Wrap((float)(Math.Atan2(sx, sz) * 180.0 / Math.PI))));
                    break;
                }
            }
            return boats;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        private static float Wrap(float yaw)
        {
            yaw %= 360f;
            return yaw < 0f ? yaw + 360f : yaw;
        }
    }
}
