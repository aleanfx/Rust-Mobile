using System;
using Oxfall.Core.Building;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    /// <summary>
    /// High external walls (a palisade of sharpened logs, or one of stone blocks) and the ladder that
    /// gets over them. A wall stands on the ground on its own, outside the building grid: 4 m long,
    /// 5 m high, too high to jump and too long to walk round quickly, so a compound of them keeps
    /// raiders, animals and bullets out until a charge brings one down or someone climbs a ladder.
    /// A wall runs along its own X (sideways from the way it faces, which is <see cref="Deployable.Yaw"/>);
    /// a ladder stands with its back to a wall, the way it faces being away from it.
    /// </summary>
    public static class HighWalls
    {
        public const float Length = 4f;
        public const float HalfLength = Length * 0.5f;
        public const float Height = 5f;
        public const float Thickness = 0.4f;

        /// <summary>How far below where it stands a wall goes into the ground (so slopes leave no gap under it).</summary>
        public const float Sunk = 1.5f;

        /// <summary>How high a ladder reaches: a little over a high wall, so one climbs onto its top.</summary>
        public const float LadderHeight = 5.6f;

        /// <summary>How far a ladder's foot stands from what it leans on.</summary>
        public const float LadderGap = 0.3f;

        /// <summary>Metres a second up or down a ladder.</summary>
        public const float ClimbSpeed = 2.6f;

        public static bool IsWall(DeployableKind kind) => kind == DeployableKind.WoodWall || kind == DeployableKind.StoneWall;

        /// <summary>Half the length of a deployable's footprint along its X (0: it is round).</summary>
        public static float FootprintHalfLength(DeployableKind kind) =>
            IsWall(kind) ? HalfLength : kind == DeployableKind.Ladder ? 0.3f : 0f;

        /// <summary>The way a deployable's X runs across the ground, for its yaw (as an engine turns +X by yaw degrees round +Y).</summary>
        public static void Right(float yaw, out float x, out float z)
        {
            double r = yaw * Math.PI / 180.0;
            x = (float)Math.Cos(r);
            z = (float)-Math.Sin(r);
        }

        /// <summary>The way a deployable faces across the ground (its +Z).</summary>
        public static void Forward(float yaw, out float x, out float z)
        {
            double r = yaw * Math.PI / 180.0;
            x = (float)Math.Sin(r);
            z = (float)Math.Cos(r);
        }

        /// <summary>The two ends of a deployable's footprint (both its middle for a round one).</summary>
        public static void Ends(DeployableKind kind, float x, float z, float yaw, out float ax, out float az, out float bx, out float bz,
            float shrink = 0f)
        {
            float h = Math.Max(0f, FootprintHalfLength(kind) - shrink);
            Right(yaw, out float rx, out float rz);
            ax = x - rx * h;
            az = z - rz * h;
            bx = x + rx * h;
            bz = z + rz * h;
        }

        /// <summary>
        /// Whether two deployables' footprints overlap. Two walls may meet end to end, or at a corner
        /// of a right angle or more, but not cross or stand side by side.
        /// </summary>
        public static bool Overlap(DeployableKind kind, float x, float z, float yaw, DeployableKind otherKind, float ox, float oz, float oyaw)
        {
            if (IsWall(kind) && IsWall(otherKind))
            {
                Ends(kind, x, z, yaw, out float a0x, out float a0z, out float a1x, out float a1z, 0.35f);
                Ends(otherKind, ox, oz, oyaw, out float b0x, out float b0z, out float b1x, out float b1z, 0.35f);
                return SegmentDistance(a0x, a0z, a1x, a1z, b0x, b0z, b1x, b1z) < 0.3f;
            }
            Ends(kind, x, z, yaw, out float p0x, out float p0z, out float p1x, out float p1z);
            Ends(otherKind, ox, oz, oyaw, out float q0x, out float q0z, out float q1x, out float q1z);
            float room = Deployables.Radius(kind) + Deployables.Radius(otherKind);
            return SegmentDistance(p0x, p0z, p1x, p1z, q0x, q0z, q1x, q1z) < room;
        }

        /// <summary>The nearest point of a wall to (x, y, z), and how far it is.</summary>
        public static float DistanceTo(Deployable wall, float x, float y, float z, out float px, out float py, out float pz)
        {
            Ends(wall.Kind, wall.X, wall.Z, wall.Yaw, out float ax, out float az, out float bx, out float bz);
            float t = Project(x, z, ax, az, bx, bz);
            px = ax + (bx - ax) * t;
            pz = az + (bz - az) * t;
            py = Math.Max(wall.Y, Math.Min(wall.Y + Height, y));
            float dx = x - px, dy = y - py, dz = z - pz;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>Whether a high wall stands across the line from (x0, y0, z0) to (x1, y1, z1) (that one aside).</summary>
        public static bool LineBlocked(Settlement settlement, float x0, float y0, float z0, float x1, float y1, float z1, int except = 0)
        {
            if (settlement == null || settlement.WallCount == 0) return false;
            foreach (var d in settlement.Deployables)
            {
                if (!IsWall(d.Kind) || d.Id == except) continue;
                Ends(d.Kind, d.X, d.Z, d.Yaw, out float ax, out float az, out float bx, out float bz);
                if (!Cross(x0, z0, x1, z1, ax, az, bx, bz, out float t)) continue;
                float y = y0 + (y1 - y0) * t;
                if (y >= d.Y - Sunk && y <= d.Y + Height) return true;
            }
            return false;
        }

        /// <summary>Whether a high wall stands within <paramref name="pad"/> of (x, z) across the ground (for what walks).</summary>
        public static bool Near(Settlement settlement, float x, float z, float pad)
        {
            if (settlement == null || settlement.WallCount == 0) return false;
            foreach (var d in settlement.Deployables)
            {
                if (!IsWall(d.Kind)) continue;
                Ends(d.Kind, d.X, d.Z, d.Yaw, out float ax, out float az, out float bx, out float bz);
                float t = Project(x, z, ax, az, bx, bz);
                float dx = x - (ax + (bx - ax) * t), dz = z - (az + (bz - az) * t);
                float room = pad + Thickness * 0.5f;
                if (dx * dx + dz * dz < room * room) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a wall can stand at (x, y, z) facing <paramref name="yaw"/>: along all its length out
        /// of the water and the ruins, clear of trunks, boulders and foundations, and on ground that
        /// neither drops away under it more than it is sunk nor rises over it.
        /// </summary>
        public static ActionResult CanStandWall(Settlement settlement, float x, float y, float z, float yaw)
        {
            var ground = settlement.Ground;
            if (Math.Abs(y - ground.Height(x, z)) > 0.7f) return ActionResult.Invalid;
            Right(yaw, out float rx, out float rz);
            Forward(yaw, out float fx, out float fz);
            const int samples = 9;
            for (int i = 0; i < samples; i++)
            {
                float along = -HalfLength + Length * i / (samples - 1);
                float px = x + rx * along, pz = z + rz * along;
                float g = ground.Height(px, pz);
                if (g < IslandGround.WaterLevel + 0.3f) return ActionResult.InWater;
                if (Monuments.NoBuilding(ground, px, pz)) return ActionResult.Monument;
                if (settlement.Obstacles != null && settlement.Obstacles.Blocks(px, pz, 0.3f)) return ActionResult.Obstructed;
                if (y - g > Sunk - 0.1f || g - y > 2.5f) return ActionResult.TooSteep;
                foreach (float side in new[] { -1f, 1f })
                {
                    float sx = px + fx * side * Thickness * 0.5f, sz = pz + fz * side * Thickness * 0.5f;
                    if (settlement.Grid.HasFoundation(BuildingGrid.CellAt(sx, sz))) return ActionResult.OnBuilding;
                }
            }
            return ActionResult.Ok;
        }

        /// <summary>Whether a ladder at (x, y, z) facing <paramref name="yaw"/> has a wall at its back to lean on (a high wall, or one of a building).</summary>
        public static bool LadderSupported(Settlement settlement, float x, float y, float z, float yaw)
        {
            Forward(yaw, out float fx, out float fz);
            float bx = x - fx * (LadderGap + 0.15f), bz = z - fz * (LadderGap + 0.15f);
            foreach (var d in settlement.Deployables)
            {
                if (!IsWall(d.Kind)) continue;
                if (d.Y + Height < y + 2f || d.Y - Sunk > y + 0.5f) continue;
                Ends(d.Kind, d.X, d.Z, d.Yaw, out float ax, out float az, out float ex, out float ez);
                float t = Project(bx, bz, ax, az, ex, ez);
                float dx = bx - (ax + (ex - ax) * t), dz = bz - (az + (ez - az) * t);
                if (dx * dx + dz * dz < 0.35f * 0.35f) return true;
            }
            return BuildingWallAt(settlement.Grid, bx, y + 1.5f, bz, 0.35f);
        }

        /// <summary>Whether a wall, doorway or window of a building stands within <paramref name="near"/> of (x, z) at height y.</summary>
        public static bool BuildingWallAt(BuildingGrid grid, float x, float y, float z, float near)
        {
            if (grid.FoundationCount == 0) return false;
            const float S = BuildingGrid.CellSize;
            for (int pass = 0; pass < 2; pass++)
            {
                bool alongX = pass == 0;
                float across = alongX ? z : x, along = alongX ? x : z;
                int line = (int)Math.Round(across / S);
                if (Math.Abs(across - line * S) > near) continue;
                int cell = (int)Math.Floor(along / S);
                for (int level = 0; level <= BuildingGrid.MaxLevel; level++)
                {
                    var edge = alongX ? new Edge(cell, line, EdgeAxis.AlongX, level) : new Edge(line, cell, EdgeAxis.AlongZ, level);
                    if (!grid.TryGetEdgePiece(edge, out _)) continue;
                    float h = y - grid.EdgeBaseHeight(edge);
                    if (h >= -0.5f && h <= BuildingGrid.WallHeight) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether someone with their feet at (x, y, z) is on this ladder (in front of it, between its
        /// foot and its top), and how high its top is.
        /// </summary>
        public static bool OnLadder(Deployable ladder, float x, float y, float z, out float top)
        {
            top = ladder.Y + LadderHeight;
            if (ladder.Kind != DeployableKind.Ladder) return false;
            float dx = x - ladder.X, dz = z - ladder.Z;
            Right(ladder.Yaw, out float rx, out float rz);
            Forward(ladder.Yaw, out float fx, out float fz);
            float side = dx * rx + dz * rz, ahead = dx * fx + dz * fz;
            return Math.Abs(side) < 0.55f && ahead > -0.45f && ahead < 0.9f && y > ladder.Y - 0.3f && y < top - 0.2f;
        }

        /// <summary>The ladder someone with their feet at (x, y, z) is on, if any.</summary>
        public static bool TryLadderAt(Settlement settlement, float x, float y, float z, out float top)
        {
            top = 0f;
            if (settlement == null || settlement.LadderCount == 0) return false;
            foreach (var d in settlement.Deployables)
                if (d.Kind == DeployableKind.Ladder && OnLadder(d, x, y, z, out top)) return true;
            return false;
        }

        // ---- Plane geometry.

        /// <summary>Where along a→b the point nearest (x, z) is, from 0 to 1.</summary>
        private static float Project(float x, float z, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
            if (len < 1e-8f) return 0f;
            return Math.Max(0f, Math.Min(1f, ((x - ax) * dx + (z - az) * dz) / len));
        }

        private static float PointSegment(float x, float z, float ax, float az, float bx, float bz)
        {
            float t = Project(x, z, ax, az, bx, bz);
            float dx = x - (ax + (bx - ax) * t), dz = z - (az + (bz - az) * t);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The shortest distance between two segments across the ground.</summary>
        public static float SegmentDistance(float a0x, float a0z, float a1x, float a1z, float b0x, float b0z, float b1x, float b1z)
        {
            if (Cross(a0x, a0z, a1x, a1z, b0x, b0z, b1x, b1z, out _)) return 0f;
            return Math.Min(Math.Min(PointSegment(a0x, a0z, b0x, b0z, b1x, b1z), PointSegment(a1x, a1z, b0x, b0z, b1x, b1z)),
                Math.Min(PointSegment(b0x, b0z, a0x, a0z, a1x, a1z), PointSegment(b1x, b1z, a0x, a0z, a1x, a1z)));
        }

        /// <summary>Whether segment p crosses segment q, and where along p (0 to 1).</summary>
        private static bool Cross(float p0x, float p0z, float p1x, float p1z, float q0x, float q0z, float q1x, float q1z, out float t)
        {
            t = 0f;
            float rx = p1x - p0x, rz = p1z - p0z, sx = q1x - q0x, sz = q1z - q0z;
            float denom = rx * sz - rz * sx;
            if (Math.Abs(denom) < 1e-8f) return false;
            float qpx = q0x - p0x, qpz = q0z - p0z;
            t = (qpx * sz - qpz * sx) / denom;
            float u = (qpx * rz - qpz * rx) / denom;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }
    }
}
