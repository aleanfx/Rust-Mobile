using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;

namespace Oxfall.Core.Rules
{
    /// <summary>What a thrown charge stuck to.</summary>
    public enum ChargeSurface : byte
    {
        /// <summary>The ground, a rock, a tree: it hurts only who stands near.</summary>
        Ground = 0,
        /// <summary>A foundation, wall, doorway or roof.</summary>
        Piece = 1,
        /// <summary>The door hung in a doorway.</summary>
        Door = 2,
    }

    /// <summary>Where a charge stuck: on nothing built, on a piece, or on a door.</summary>
    public readonly struct ChargeTarget : IEquatable<ChargeTarget>
    {
        public readonly ChargeSurface Surface;
        public readonly PieceRef Piece;

        private ChargeTarget(ChargeSurface surface, PieceRef piece)
        {
            Surface = surface;
            Piece = piece;
        }

        public static ChargeTarget Ground => default;
        public static ChargeTarget OnPiece(PieceRef piece) => new ChargeTarget(ChargeSurface.Piece, piece);
        public static ChargeTarget OnDoor(Edge edge) => new ChargeTarget(ChargeSurface.Door, PieceRef.OnEdge(edge, PieceType.Doorway));

        public bool IsBuilt => Surface != ChargeSurface.Ground;

        public void Write(NetWriter w)
        {
            w.Byte((byte)Surface);
            if (Surface == ChargeSurface.Ground) return;
            BuildOp.WritePiece(w, Piece);
        }

        public static ChargeTarget Read(NetReader r)
        {
            var surface = (ChargeSurface)r.Byte();
            if (surface == ChargeSurface.Ground) return Ground;
            if (surface != ChargeSurface.Piece && surface != ChargeSurface.Door) throw new ProtocolException("Unknown charge surface.");
            var piece = BuildOp.ReadPiece(r);
            if (surface == ChargeSurface.Door)
            {
                if (piece.Type != PieceType.Doorway) throw new ProtocolException("Only a doorway has a door.");
                return OnDoor(piece.Edge);
            }
            return OnPiece(piece);
        }

        public bool Equals(ChargeTarget other) => Surface == other.Surface && (Surface == ChargeSurface.Ground || Piece.Equals(other.Piece));
        public override bool Equals(object obj) => obj is ChargeTarget t && Equals(t);
        public override int GetHashCode() => Surface == ChargeSurface.Ground ? 0 : Piece.GetHashCode() * 4 + (int)Surface;
        public override string ToString() => Surface == ChargeSurface.Ground ? "ground" : Surface == ChargeSurface.Door ? "door " + Piece.Edge : Piece.ToString();
    }

    /// <summary>A charge stuck somewhere, its fuse burning.</summary>
    public readonly struct LiveCharge
    {
        public readonly int Id;
        /// <summary>Who threw it (their player id online; 0 alone).</summary>
        public readonly ushort Thrower;
        public readonly float X, Y, Z;
        /// <summary>The way the surface it stuck to faces (for how it lies on it).</summary>
        public readonly float NX, NY, NZ;
        public readonly ChargeTarget Target;
        /// <summary>When it goes off, on the clock of whoever keeps it.</summary>
        public readonly float GoesOffAt;

        public LiveCharge(int id, ushort thrower, float x, float y, float z, float nx, float ny, float nz, ChargeTarget target, float goesOffAt)
        {
            Id = id;
            Thrower = thrower;
            X = x;
            Y = y;
            Z = z;
            NX = nx;
            NY = ny;
            NZ = nz;
            Target = target;
            GoesOffAt = goesOffAt;
        }

        public override string ToString() => $"charge {Id} on {Target} at ({X:0.0},{Y:0.0},{Z:0.0})";
    }

    /// <summary>
    /// Satchel charges: thrown at a wall or a door, they stick, fizz for a few seconds and go off.
    /// Every charge takes the same bite out of what it stuck to, so what decides how many a raid needs
    /// is what the base is built of: twig falls to one, wood to two, stone to four, metal to eight; a
    /// wooden door to one and a sheet metal door to two. Anyone close is hurt too.
    /// </summary>
    public static class Explosives
    {
        public const float FuseSeconds = 5f;

        /// <summary>The farthest a charge can be thrown to stick (from the eyes).</summary>
        public const float ThrowReach = 5f;

        /// <summary>The least time between two throws.</summary>
        public const float ThrowSeconds = 1f;

        /// <summary>Health one charge takes off the piece or door it stuck to.</summary>
        public const float BlastDamage = 125f;

        /// <summary>How far the blast hurts people, and how much right next to it (less farther off).</summary>
        public const float BlastRadius = 4.5f;
        public const float PlayerDamage = 80f;

        /// <summary>How far from a piece's middle a charge can say it stuck to it (a wall is 3 m across and 3 m high).</summary>
        public const float StickReach = 2.6f;

        /// <summary>How far a blast reaches what is set down (boxes, cupboards...), and how much it takes off one right next to it.</summary>
        public const float DeployableReach = 2.5f;
        public const float DeployableDamage = 75f;

        /// <summary>Health taken from a deployable this far from the blast (its middle).</summary>
        public static float DamageToDeployable(float distance)
        {
            if (distance >= DeployableReach) return 0f;
            float t = Math.Max(0f, distance - 0.6f) / (DeployableReach - 0.6f);
            return DeployableDamage * (1f - t);
        }

        /// <summary>Health taken from someone this far from the blast (their middle), before what they wear.</summary>
        public static float DamageToPlayer(float distance)
        {
            if (distance >= BlastRadius) return 0f;
            float t = Math.Max(0f, distance - 0.5f) / (BlastRadius - 0.5f);
            return PlayerDamage * (1f - t) * (1f - t * 0.35f);
        }

        /// <summary>How many charges bring down something with this much health.</summary>
        public static int ChargesFor(float health) => health <= 0f ? 0 : (int)Math.Ceiling(health / BlastDamage - 1e-4);

        /// <summary>Whether a charge said to be at (x, y, z) could have stuck to that target.</summary>
        public static bool CanStick(BuildingGrid grid, ChargeTarget target, float x, float y, float z)
        {
            switch (target.Surface)
            {
                case ChargeSurface.Ground:
                    return true;
                case ChargeSurface.Door:
                {
                    if (!grid.HasDoor(target.Piece.Edge)) return false;
                    var p = PlayerActions.EdgePoint(grid, target.Piece.Edge);
                    return Near(x, y, z, p, StickReach);
                }
                default:
                {
                    var piece = BuildOps.Stored(grid, target.Piece);
                    if (!grid.Exists(piece)) return false;
                    var p = PlayerActions.PiecePoint(grid, piece);
                    return Near(x, y, z, p, StickReach);
                }
            }
        }

        /// <summary>
        /// The blast, on what the charge stuck to. What changed goes into <paramref name="changes"/> as
        /// health ops (a piece or door at 0 has fallen, with whatever stood on it). False if the
        /// target is gone (a charge on the ground, or on something an earlier charge already brought down).
        /// </summary>
        public static bool Blast(BuildingGrid grid, ChargeTarget target, List<BuildOp> changes)
        {
            switch (target.Surface)
            {
                case ChargeSurface.Door:
                {
                    var edge = target.Piece.Edge;
                    // The door went with an earlier charge: this one hangs on the frame.
                    if (!grid.HasDoor(edge)) return Blast(grid, OnFrame(grid, edge), changes);
                    float left = grid.DoorHealth(edge) - BlastDamage;
                    grid.SetDoorHealth(edge, left);
                    changes?.Add(BuildOp.DoorHealth(edge, Math.Max(0f, left)));
                    return true;
                }
                case ChargeSurface.Piece:
                {
                    var piece = BuildOps.Stored(grid, target.Piece);
                    if (!grid.Exists(piece)) return false;
                    float left = grid.Health(piece) - BlastDamage;
                    grid.SetHealth(piece, left);
                    changes?.Add(BuildOp.Health(piece, Math.Max(0f, left)));
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// The blast on what is set down near it, where no wall stands between: each thing loses health
        /// by how close it is and goes when it has none left (a box spilling what it held into a bag,
        /// where the settlement decides that). A dead player's bag is not hurt. What changed goes into
        /// <paramref name="changes"/> as health ops.
        /// </summary>
        public static void Splash(Settlement settlement, float x, float y, float z, List<BuildOp> changes)
        {
            var near = new List<(int Id, float Amount)>();
            foreach (var d in settlement.Deployables)
            {
                if (Deployables.MaxHealth(d.Kind) <= 0f) continue;
                float dy = d.Y + 0.4f;
                float amount = DamageToDeployable(Distance(x, y, z, d.X, dy, d.Z));
                if (amount > 0f && !Shielded(settlement.Grid, x, y, z, d.X, dy, d.Z)) near.Add((d.Id, amount));
            }
            foreach (var (id, amount) in near)
            {
                float left = settlement.DeployableHealth(id) - amount;
                if (!settlement.SetDeployableHealth(id, left)) continue;
                changes?.Add(BuildOp.DeployableHealth(id, Math.Max(0f, left)));
            }
        }

        /// <summary>
        /// Whether a wall, a doorway's frame or a shut door stands between a blast at (x0, y0, z0) and
        /// (x1, y1, z1): what is inside a base is safe from a charge on its outside until that wall falls.
        /// </summary>
        public static bool Shielded(BuildingGrid grid, float x0, float y0, float z0, float x1, float y1, float z1)
        {
            const float S = BuildingGrid.CellSize;
            // Walls along X stand on the lines z = k * S; walls along Z on x = k * S.
            for (int pass = 0; pass < 2; pass++)
            {
                bool alongX = pass == 0;
                float a0 = alongX ? z0 : x0, a1 = alongX ? z1 : x1;
                if (Math.Abs(a1 - a0) < 1e-5f) continue;
                int from = (int)Math.Floor(Math.Min(a0, a1) / S) + 1, to = (int)Math.Ceiling(Math.Max(a0, a1) / S) - 1;
                for (int k = from; k <= to; k++)
                {
                    float t = (k * S - a0) / (a1 - a0);
                    if (t <= 0f || t >= 1f) continue;
                    float px = x0 + (x1 - x0) * t, py = y0 + (y1 - y0) * t, pz = z0 + (z1 - z0) * t;
                    float along = alongX ? px : pz;
                    int cellAlong = (int)Math.Floor(along / S);
                    for (int level = 0; level <= BuildingGrid.MaxLevel; level++)
                    {
                        var edge = alongX ? new Edge(cellAlong, k, EdgeAxis.AlongX, level) : new Edge(k, cellAlong, EdgeAxis.AlongZ, level);
                        if (!grid.TryGetEdgePiece(edge, out var piece)) continue;
                        float h = py - grid.EdgeBaseHeight(edge);
                        if (h < -0.05f || h > BuildingGrid.WallHeight + 0.05f) continue;
                        if (piece.Type == PieceType.Wall) return true;
                        float offset = along - (cellAlong + 0.5f) * S;
                        // A window: shields but for its opening.
                        if (piece.Type == PieceType.Window)
                        {
                            if (!Openings.InWindow(offset, h)) return true;
                            continue;
                        }
                        // A doorway: its frame shields, and its opening does while a door is shut in it.
                        if (!Openings.InDoorway(offset, h) || (grid.HasDoor(edge) && !grid.IsDoorOpen(edge))) return true;
                    }
                }
            }
            return Floored(grid, x0, y0, z0, x1, y1, z1);
        }

        /// <summary>Whether a floor (a roof, seen from below or above) lies between the two points.</summary>
        private static bool Floored(BuildingGrid grid, float x0, float y0, float z0, float x1, float y1, float z1)
        {
            if (Math.Abs(y1 - y0) < 0.05f) return false;
            // A step at a time along the line: a floor is crossed where the storey under a step changes.
            int steps = Math.Max(1, (int)Math.Ceiling(Distance(x0, y0, z0, x1, y1, z1) / 0.25f));
            for (int i = 0; i < steps; i++)
            {
                float t0 = (float)i / steps, t1 = (float)(i + 1) / steps;
                float xa = x0 + (x1 - x0) * t0, ya = y0 + (y1 - y0) * t0, za = z0 + (z1 - z0) * t0;
                float xb = x0 + (x1 - x0) * t1, yb = y0 + (y1 - y0) * t1, zb = z0 + (z1 - z0) * t1;
                var column = BuildingGrid.CellAt((xa + xb) * 0.5f, (za + zb) * 0.5f);
                if (!grid.TryGetFoundationTop(column, out float top)) continue;
                float low = Math.Min(ya, yb), high = Math.Max(ya, yb);
                // The roof over storey L lies at top + (L + 1) × wall height, a floor's thickness deep.
                for (int level = 0; level <= BuildingGrid.MaxLevel; level++)
                {
                    float plane = top + (level + 1) * BuildingGrid.WallHeight + BuildingGrid.FloorThickness * 0.5f;
                    if (plane > high) break;
                    if (plane >= low && grid.HasRoof(new Cell(column.X, column.Z, level))) return true;
                }
            }
            return false;
        }

        private static float Distance(float x0, float y0, float z0, float x1, float y1, float z1)
        {
            float dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static ChargeTarget OnFrame(BuildingGrid grid, Edge edge) =>
            grid.TryGetEdgePiece(edge, out var frame) ? ChargeTarget.OnPiece(frame) : ChargeTarget.Ground;

        private static bool Near(float x, float y, float z, (float X, float Y, float Z) p, float reach)
        {
            float dx = x - p.X, dy = y - p.Y, dz = z - p.Z;
            return dx * dx + dy * dy + dz * dz <= reach * reach;
        }
    }

    /// <summary>
    /// Mending with the hammer: a share of what the piece's tier cost, for the share of health it
    /// lost; not while it is under attack (for a little while after each blast).
    /// </summary>
    public static class Repairs
    {
        /// <summary>Seconds after a blast before the piece can be mended.</summary>
        public const float Delay = 30f;

        /// <summary>What mending all of a piece's health costs, as a share of what its tier cost.</summary>
        public const float CostShare = 0.5f;

        public static IReadOnlyList<Ingredient> Cost(PieceType type, BuildingTier tier, float missing) =>
            Scale(tier == BuildingTier.Twig ? BuildCosts.For(type) : BuildCosts.Upgrade(type, tier), missing);

        public static IReadOnlyList<Ingredient> DoorCost(DoorKind kind, float missing) =>
            Scale(new[] { new Ingredient(kind == DoorKind.Metal ? ItemIds.MetalFragments : ItemIds.Wood, 150) }, missing);

        private static IReadOnlyList<Ingredient> Scale(IReadOnlyList<Ingredient> full, float missing)
        {
            missing = Math.Max(0f, Math.Min(1f, missing));
            var list = new List<Ingredient>(full.Count);
            foreach (var c in full)
                list.Add(new Ingredient(c.ItemId, Math.Max(1, (int)Math.Ceiling(c.Amount * CostShare * missing - 1e-4))));
            return list;
        }
    }
}
