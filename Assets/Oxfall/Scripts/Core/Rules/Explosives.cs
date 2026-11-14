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
            var piece = Piece;
            w.Byte((byte)piece.Type);
            if (piece.IsEdge)
            {
                w.Int(piece.Edge.X);
                w.Int(piece.Edge.Z);
                w.Byte((byte)piece.Edge.Axis);
            }
            else
            {
                w.Int(piece.Cell.X);
                w.Int(piece.Cell.Z);
                w.Byte(0);
            }
        }

        public static ChargeTarget Read(NetReader r)
        {
            var surface = (ChargeSurface)r.Byte();
            if (surface == ChargeSurface.Ground) return Ground;
            if (surface != ChargeSurface.Piece && surface != ChargeSurface.Door) throw new ProtocolException("Unknown charge surface.");
            var type = (PieceType)r.Byte();
            if (!Enum.IsDefined(typeof(PieceType), type)) throw new ProtocolException("Unknown piece type.");
            int x = r.Int(), z = r.Int();
            var axis = (EdgeAxis)r.Byte();
            if (Math.Abs(x) > 4096 || Math.Abs(z) > 4096) throw new ProtocolException("Coordinate off the island.");
            if (PieceRef.IsEdgeType(type) && !Enum.IsDefined(typeof(EdgeAxis), axis)) throw new ProtocolException("Unknown edge axis.");
            if (surface == ChargeSurface.Door)
            {
                if (type != PieceType.Doorway) throw new ProtocolException("Only a doorway has a door.");
                return OnDoor(new Edge(x, z, axis));
            }
            var piece = PieceRef.IsEdgeType(type) ? PieceRef.OnEdge(new Edge(x, z, axis), type)
                : type == PieceType.Roof ? PieceRef.Roof(new Cell(x, z)) : PieceRef.Foundation(new Cell(x, z));
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
