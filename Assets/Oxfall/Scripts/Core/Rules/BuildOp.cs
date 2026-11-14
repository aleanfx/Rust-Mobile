using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Net;

namespace Oxfall.Core.Rules
{
    /// <summary>Kinds of change to the settlement. Sent as numbers: only ever add at the end.</summary>
    public enum BuildOpKind : byte
    {
        Foundation = 1,
        Edge = 2,
        Roof = 3,
        Upgrade = 4,
        Remove = 5,
        Door = 6,
        ToggleDoor = 7,
        Campfire = 8,
    }

    /// <summary>
    /// One change to what stands on the island. A player asks for one (the server checks and charges
    /// for it), and the server tells everyone the change it made, which each copy then applies as is.
    /// </summary>
    public readonly struct BuildOp
    {
        /// <summary>Coordinates past this are not on the island (and not worth trusting).</summary>
        private const int MaxCoordinate = 4096;

        public readonly BuildOpKind Kind;
        public readonly PieceType Type;
        public readonly int X, Z;
        public readonly EdgeAxis Axis;
        public readonly BuildingTier Tier;
        public readonly bool Open;
        public readonly float Top;
        public readonly Campfire Fire;

        private BuildOp(BuildOpKind kind, PieceType type, int x, int z, EdgeAxis axis, BuildingTier tier = BuildingTier.Twig,
            bool open = false, float top = 0f, Campfire fire = default)
        {
            Kind = kind;
            Type = type;
            X = x;
            Z = z;
            Axis = axis;
            Tier = tier;
            Open = open;
            Top = top;
            Fire = fire;
        }

        public static BuildOp Foundation(Cell cell, float top) =>
            new BuildOp(BuildOpKind.Foundation, PieceType.Foundation, cell.X, cell.Z, default, top: top);

        public static BuildOp Edge(Edge edge, PieceType type) =>
            new BuildOp(BuildOpKind.Edge, type, edge.X, edge.Z, edge.Axis);

        public static BuildOp Roof(Cell cell) => new BuildOp(BuildOpKind.Roof, PieceType.Roof, cell.X, cell.Z, default);

        public static BuildOp Upgrade(PieceRef piece, BuildingTier tier) => OnPiece(BuildOpKind.Upgrade, piece, tier);

        public static BuildOp Remove(PieceRef piece) => OnPiece(BuildOpKind.Remove, piece, BuildingTier.Twig);

        public static BuildOp Door(Edge edge) => new BuildOp(BuildOpKind.Door, PieceType.Doorway, edge.X, edge.Z, edge.Axis);

        public static BuildOp ToggleDoor(Edge edge, bool open) =>
            new BuildOp(BuildOpKind.ToggleDoor, PieceType.Doorway, edge.X, edge.Z, edge.Axis, open: open);

        public static BuildOp PlaceCampfire(Campfire fire) =>
            new BuildOp(BuildOpKind.Campfire, PieceType.Foundation, 0, 0, default, fire: fire);

        private static BuildOp OnPiece(BuildOpKind kind, PieceRef piece, BuildingTier tier) => piece.IsEdge
            ? new BuildOp(kind, piece.Type, piece.Edge.X, piece.Edge.Z, piece.Edge.Axis, tier)
            : new BuildOp(kind, piece.Type, piece.Cell.X, piece.Cell.Z, default, tier);

        public Cell Cell => new Cell(X, Z);
        public Edge EdgeAt => new Edge(X, Z, Axis);

        /// <summary>The piece an upgrade or removal is about.</summary>
        public PieceRef Piece => PieceRef.IsEdgeType(Type)
            ? PieceRef.OnEdge(EdgeAt, Type)
            : Type == PieceType.Roof ? PieceRef.Roof(Cell) : PieceRef.Foundation(Cell);

        public BuildOp WithTop(float top) => new BuildOp(Kind, Type, X, Z, Axis, Tier, Open, top, Fire);
        public BuildOp WithTier(BuildingTier tier) => new BuildOp(Kind, Type, X, Z, Axis, tier, Open, Top, Fire);
        public BuildOp WithOpen(bool open) => new BuildOp(Kind, Type, X, Z, Axis, Tier, open, Top, Fire);
        public BuildOp WithFire(Campfire fire) => new BuildOp(Kind, Type, X, Z, Axis, Tier, Open, Top, fire);

        public override string ToString() => Kind == BuildOpKind.Campfire
            ? $"Campfire {Fire.Id} ({Fire.X:0.0}, {Fire.Z:0.0})"
            : $"{Kind} {Type} ({X},{Z}{(PieceRef.IsEdgeType(Type) ? "," + Axis : "")})";

        // ---- On the wire

        public void Write(NetWriter w)
        {
            w.Byte((byte)Kind);
            switch (Kind)
            {
                case BuildOpKind.Foundation:
                    w.Int(X);
                    w.Int(Z);
                    w.Float(Top);
                    break;
                case BuildOpKind.Edge:
                    w.Int(X);
                    w.Int(Z);
                    w.Byte((byte)Axis);
                    w.Byte((byte)Type);
                    break;
                case BuildOpKind.Roof:
                    w.Int(X);
                    w.Int(Z);
                    break;
                case BuildOpKind.Upgrade:
                case BuildOpKind.Remove:
                    w.Byte((byte)Type);
                    w.Int(X);
                    w.Int(Z);
                    w.Byte((byte)Axis);
                    w.Byte((byte)Tier);
                    break;
                case BuildOpKind.Door:
                case BuildOpKind.ToggleDoor:
                    w.Int(X);
                    w.Int(Z);
                    w.Byte((byte)Axis);
                    w.Bool(Open);
                    break;
                case BuildOpKind.Campfire:
                    w.Int(Fire.Id);
                    w.Float(Fire.X);
                    w.Float(Fire.Y);
                    w.Float(Fire.Z);
                    w.Float(Fire.Yaw);
                    break;
                default:
                    throw new InvalidOperationException("Unknown build op " + Kind);
            }
        }

        public static BuildOp Read(NetReader r)
        {
            var kind = (BuildOpKind)r.Byte();
            switch (kind)
            {
                case BuildOpKind.Foundation:
                {
                    var cell = new Cell(Coordinate(r), Coordinate(r));
                    return Foundation(cell, Finite(r.Float()));
                }
                case BuildOpKind.Edge:
                {
                    var edge = new Edge(Coordinate(r), Coordinate(r), ReadAxis(r));
                    var type = ReadType(r);
                    if (!PieceRef.IsEdgeType(type)) throw new ProtocolException("Not an edge piece.");
                    return Edge(edge, type);
                }
                case BuildOpKind.Roof:
                    return Roof(new Cell(Coordinate(r), Coordinate(r)));
                case BuildOpKind.Upgrade:
                case BuildOpKind.Remove:
                {
                    var type = ReadType(r);
                    int x = Coordinate(r), z = Coordinate(r);
                    var axis = ReadAxis(r);
                    var tier = (BuildingTier)r.Byte();
                    if (!Enum.IsDefined(typeof(BuildingTier), tier)) throw new ProtocolException("Unknown tier.");
                    var piece = PieceRef.IsEdgeType(type) ? PieceRef.OnEdge(new Edge(x, z, axis), type)
                        : type == PieceType.Roof ? PieceRef.Roof(new Cell(x, z)) : PieceRef.Foundation(new Cell(x, z));
                    return kind == BuildOpKind.Upgrade ? Upgrade(piece, tier) : Remove(piece);
                }
                case BuildOpKind.Door:
                case BuildOpKind.ToggleDoor:
                {
                    var edge = new Edge(Coordinate(r), Coordinate(r), ReadAxis(r));
                    bool open = r.Bool();
                    return kind == BuildOpKind.Door ? Door(edge) : ToggleDoor(edge, open);
                }
                case BuildOpKind.Campfire:
                {
                    int id = r.Int();
                    float x = Finite(r.Float()), y = Finite(r.Float()), z = Finite(r.Float()), yaw = Finite(r.Float());
                    return PlaceCampfire(new Campfire(id, x, y, z, yaw));
                }
                default:
                    throw new ProtocolException("Unknown build op " + (byte)kind + ".");
            }
        }

        private static int Coordinate(NetReader r)
        {
            int v = r.Int();
            if (v < -MaxCoordinate || v > MaxCoordinate) throw new ProtocolException("Coordinate off the island.");
            return v;
        }

        private static float Finite(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > MaxCoordinate * 4f) throw new ProtocolException("Bad number.");
            return v;
        }

        private static EdgeAxis ReadAxis(NetReader r)
        {
            var axis = (EdgeAxis)r.Byte();
            if (!Enum.IsDefined(typeof(EdgeAxis), axis)) throw new ProtocolException("Unknown edge axis.");
            return axis;
        }

        private static PieceType ReadType(NetReader r)
        {
            var type = (PieceType)r.Byte();
            if (!Enum.IsDefined(typeof(PieceType), type)) throw new ProtocolException("Unknown piece type.");
            return type;
        }
    }

    /// <summary>Applying the server's changes to a copy of the settlement, and describing one from scratch.</summary>
    public static class BuildOps
    {
        /// <summary>
        /// Applies a change the server already made (no checks, no charges). Returns false if it did
        /// not fit this copy, which would mean the copy has drifted.
        /// </summary>
        public static bool Apply(Settlement settlement, BuildOp op)
        {
            var grid = settlement.Grid;
            switch (op.Kind)
            {
                case BuildOpKind.Foundation: return grid.PlaceFoundation(op.Cell, op.Top) == PlaceResult.Ok;
                case BuildOpKind.Edge: return grid.PlaceEdge(op.EdgeAt, op.Type) == PlaceResult.Ok;
                case BuildOpKind.Roof: return grid.PlaceRoof(op.Cell) == PlaceResult.Ok;
                case BuildOpKind.Upgrade: return grid.Upgrade(Stored(grid, op.Piece), op.Tier) == PlaceResult.Ok;
                case BuildOpKind.Remove: return grid.Remove(Stored(grid, op.Piece));
                case BuildOpKind.Door: return grid.PlaceDoor(op.EdgeAt) == PlaceResult.Ok;
                case BuildOpKind.ToggleDoor:
                    if (!grid.HasDoor(op.EdgeAt)) return false;
                    return grid.IsDoorOpen(op.EdgeAt) == op.Open || grid.ToggleDoor(op.EdgeAt);
                case BuildOpKind.Campfire:
                    settlement.AddCampfire(op.Fire);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The changes that build this settlement up from nothing, for a player who just joined.</summary>
        public static List<BuildOp> Describe(Settlement settlement)
        {
            var grid = settlement.Grid;
            var ops = new List<BuildOp>();
            var upgrades = new List<BuildOp>();
            void Tier(PieceRef piece)
            {
                if (grid.TryGetTier(piece, out var tier) && tier != BuildingTier.Twig) upgrades.Add(BuildOp.Upgrade(piece, tier));
            }

            foreach (var f in grid.Foundations)
            {
                ops.Add(BuildOp.Foundation(f.Key, f.Value));
                Tier(PieceRef.Foundation(f.Key));
            }
            foreach (var e in grid.EdgePieces)
            {
                ops.Add(BuildOp.Edge(e.Key, e.Value));
                Tier(PieceRef.OnEdge(e.Key, e.Value));
            }
            foreach (var cell in grid.Roofs)
            {
                ops.Add(BuildOp.Roof(cell));
                Tier(PieceRef.Roof(cell));
            }
            ops.AddRange(upgrades);
            foreach (var d in grid.Doors)
            {
                ops.Add(BuildOp.Door(d.Key));
                if (d.Value) ops.Add(BuildOp.ToggleDoor(d.Key, true));
            }
            foreach (var fire in settlement.Campfires) ops.Add(BuildOp.PlaceCampfire(fire));
            return ops;
        }

        /// <summary>The grid's own ref for a piece (an edge's ref may name a wall where a doorway stands).</summary>
        public static PieceRef Stored(BuildingGrid grid, PieceRef piece) =>
            piece.IsEdge && grid.TryGetEdgePiece(piece.Edge, out var stored) ? stored : piece;
    }
}
