using System;
using System.Collections.Generic;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    public enum BoatKind : byte
    {
        Rowboat = 0,
        /// <summary>A rowboat with an outboard motor: twice as fast while it has fuel, rowed like any other without.</summary>
        Motorboat = 1,
    }

    /// <summary>A boat on the sea: where it floats, which way it points, how fast it goes, who steers it and its fuel.</summary>
    public sealed class Boat
    {
        public readonly int Id;
        public readonly BoatKind Kind;
        public float X, Z, Yaw, Speed;

        /// <summary>Fuel in the motor's tank, in cans of fuel (a motorboat only).</summary>
        public float Fuel;

        /// <summary>The player rowing it (0: nobody, it drifts to a stop).</summary>
        public ushort Driver;

        public Boat(int id, float x, float z, float yaw, BoatKind kind = BoatKind.Rowboat)
        {
            Id = id;
            X = x;
            Z = z;
            Yaw = yaw;
            Kind = kind;
        }

        /// <summary>Whether its motor runs (a motorboat with fuel left).</summary>
        public bool Motored => Kind == BoatKind.Motorboat && Fuel > 0f;
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

        // The motorboat, while its motor runs.
        public const float MotorSpeed = 15f;
        public const float MotorReverse = 4f;
        public const float MotorAcceleration = 5.5f;
        public const float MotorTurnRate = 62f;
        /// <summary>Seconds of full throttle one can of fuel gives.</summary>
        public const float SecondsPerFuel = 5f;
        /// <summary>What its tank holds, in cans.</summary>
        public const int TankSize = 200;
        /// <summary>Fuel a motorboat floats with at the start of a wipe: enough to learn to drive it.</summary>
        public const int StartFuel = 30;
        /// <summary>Every this many boats round the island is a motorboat.</summary>
        public const int MotorEvery = 3;

        /// <summary>The fastest this boat goes forward now (its motor's speed while it runs).</summary>
        public static float TopSpeed(Boat boat) => boat.Motored ? MotorSpeed : MaxSpeed;

        /// <summary>The fastest a boat of this kind ever goes forward (the server's limit for believing a driver).</summary>
        public static float LimitSpeed(Boat boat) => boat.Kind == BoatKind.Motorboat ? MotorSpeed : MaxSpeed;

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
            bool motor = boat.Motored;
            float top = motor ? MotorSpeed : MaxSpeed, back = motor ? MotorReverse : MaxReverse;
            float accel = motor ? MotorAcceleration : Acceleration, turnRate = motor ? MotorTurnRate : TurnRate;
            if (motor && Math.Abs(throttle) >= 0.05f) Burn(boat, Math.Abs(throttle) * dt);
            float target = throttle >= 0f ? throttle * top : throttle * back;
            if (Math.Abs(throttle) < 0.05f)
                boat.Speed = boat.Speed > 0f ? Math.Max(0f, boat.Speed - Drag * dt) : Math.Min(0f, boat.Speed + Drag * dt);
            else if (boat.Speed < target)
                boat.Speed = Math.Min(target, boat.Speed + accel * dt);
            else
                // Faster than the oars can keep (the motor just ran dry): it slows as if let go.
                boat.Speed = Math.Max(target, boat.Speed - (boat.Speed > top ? Drag : accel * 1.5f) * dt);
            float speedShare = Math.Min(1f, Math.Abs(boat.Speed) / top);
            float turn = steer * (StillTurnRate + (turnRate - StillTurnRate) * speedShare) * dt;
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

        /// <summary>
        /// Whether a boat could have gone from where it was to (x, z) in this time, at <paramref name="top"/>
        /// metres a second (0: the fastest its kind goes).
        /// </summary>
        public static bool Believable(Boat boat, float x, float z, float dt, IslandGround ground, float slack = 1.5f, float top = 0f)
        {
            if (float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z)) return false;
            float dx = x - boat.X, dz = z - boat.Z;
            float allowed = (top > 0f ? top : LimitSpeed(boat)) * Math.Min(1f, Math.Max(0f, dt)) + slack;
            return dx * dx + dz * dz <= allowed * allowed && Floats(ground, x, z);
        }

        /// <summary>Burns the fuel of this many seconds at full throttle.</summary>
        public static void Burn(Boat boat, float fullThrottleSeconds)
        {
            if (boat.Kind != BoatKind.Motorboat || fullThrottleSeconds <= 0f) return;
            boat.Fuel = Math.Max(0f, boat.Fuel - fullThrottleSeconds / SecondsPerFuel);
        }

        /// <summary>How many cans of fuel the tank takes now (0 for a rowboat).</summary>
        public static int TankSpace(Boat boat) =>
            boat.Kind != BoatKind.Motorboat ? 0 : Math.Max(0, TankSize - (int)Math.Ceiling(boat.Fuel - 1e-3f));

        /// <summary>Whether a player at this spot (in it, or standing by) can pour fuel into it.</summary>
        public static bool CanRefuel(Boat boat, ushort playerId, float x, float y, float z)
        {
            if (boat.Kind != BoatKind.Motorboat) return false;
            if (boat.Driver == playerId && playerId != 0) return true;
            float dx = x - boat.X, dz = z - boat.Z;
            return dx * dx + dz * dz <= BoardReach * BoardReach && y < IslandGround.WaterLevel + 3f;
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
        /// (the same spots for a seed), bow out to sea; every <see cref="MotorEvery"/>th a motorboat.
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
                    var kind = k % MotorEvery == MotorEvery - 1 ? BoatKind.Motorboat : BoatKind.Rowboat;
                    var boat = new Boat(k + 1, bx, bz, Wrap((float)(Math.Atan2(sx, sz) * 180.0 / Math.PI)), kind);
                    if (kind == BoatKind.Motorboat) boat.Fuel = StartFuel;
                    boats.Add(boat);
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
