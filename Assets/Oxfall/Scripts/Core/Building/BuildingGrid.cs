using System;
using System.Collections.Generic;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;

namespace Oxfall.Core.Building
{
    public enum PieceType
    {
        Foundation,
        Wall,
        Doorway,
        Roof,
    }

    /// <summary>A square of the building grid.</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public int X { get; }
        public int Z { get; }
        public Cell(int x, int z) { X = x; Z = z; }

        public Edge South => new Edge(X, Z, EdgeAxis.AlongX);
        public Edge North => new Edge(X, Z + 1, EdgeAxis.AlongX);
        public Edge West => new Edge(X, Z, EdgeAxis.AlongZ);
        public Edge East => new Edge(X + 1, Z, EdgeAxis.AlongZ);

        public IEnumerable<Edge> Edges
        {
            get { yield return South; yield return North; yield return West; yield return East; }
        }

        public bool Equals(Cell other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => (X * 73856093) ^ (Z * 19349663);
        public override string ToString() => $"({X},{Z})";
    }

    public enum EdgeAxis
    {
        /// <summary>Runs along the X axis, at z = Z × cell size.</summary>
        AlongX,
        /// <summary>Runs along the Z axis, at x = X × cell size.</summary>
        AlongZ,
    }

    /// <summary>A side shared by two cells. Walls and doorways stand on edges.</summary>
    public readonly struct Edge : IEquatable<Edge>
    {
        public int X { get; }
        public int Z { get; }
        public EdgeAxis Axis { get; }
        public Edge(int x, int z, EdgeAxis axis) { X = x; Z = z; Axis = axis; }

        /// <summary>The two cells that share this edge.</summary>
        public Cell CellA => Axis == EdgeAxis.AlongX ? new Cell(X, Z - 1) : new Cell(X - 1, Z);
        public Cell CellB => new Cell(X, Z);

        public bool Equals(Edge other) => X == other.X && Z == other.Z && Axis == other.Axis;
        public override bool Equals(object obj) => obj is Edge e && Equals(e);
        public override int GetHashCode() => ((X * 73856093) ^ (Z * 19349663)) * 2 + (int)Axis;
        public override string ToString() => $"{Axis}({X},{Z})";
    }

    public enum PlaceResult
    {
        Ok,
        Occupied,
        NeedsFoundation,
        NeedsWall,
        NotEnoughResources,
    }

    /// <summary>
    /// The rules of where pieces may go, independent of terrain and visuals. Terrain checks
    /// (water, slope) belong to the game; this class answers "is the grid free and supported".
    /// </summary>
    public sealed class BuildingGrid
    {
        public const float CellSize = 3f;
        public const float WallHeight = 3f;

        private readonly Dictionary<Cell, float> _foundations = new Dictionary<Cell, float>();
        private readonly Dictionary<Edge, PieceType> _edges = new Dictionary<Edge, PieceType>();
        private readonly HashSet<Cell> _roofs = new HashSet<Cell>();

        public event Action<PieceType, Cell> CellPieceAdded;
        public event Action<PieceType, Edge> EdgePieceAdded;
        public event Action<PieceType, Cell> CellPieceRemoved;
        public event Action<PieceType, Edge> EdgePieceRemoved;

        public int FoundationCount => _foundations.Count;
        public IEnumerable<KeyValuePair<Cell, float>> Foundations => _foundations;
        public IEnumerable<KeyValuePair<Edge, PieceType>> EdgePieces => _edges;
        public IEnumerable<Cell> Roofs => _roofs;

        public static Cell CellAt(float worldX, float worldZ) =>
            new Cell((int)Math.Floor(worldX / CellSize), (int)Math.Floor(worldZ / CellSize));

        public bool HasFoundation(Cell cell) => _foundations.ContainsKey(cell);
        public bool TryGetFoundationTop(Cell cell, out float top) => _foundations.TryGetValue(cell, out top);
        public bool HasEdgePiece(Edge edge) => _edges.ContainsKey(edge);
        public bool HasRoof(Cell cell) => _roofs.Contains(cell);

        /// <summary>Height of the floor a wall on this edge stands on: the higher of its foundations.</summary>
        public float EdgeBaseHeight(Edge edge)
        {
            bool a = _foundations.TryGetValue(edge.CellA, out float ta);
            bool b = _foundations.TryGetValue(edge.CellB, out float tb);
            if (a && b) return Math.Max(ta, tb);
            if (a) return ta;
            if (b) return tb;
            return 0f;
        }

        public PlaceResult CanPlaceFoundation(Cell cell) =>
            _foundations.ContainsKey(cell) ? PlaceResult.Occupied : PlaceResult.Ok;

        public PlaceResult CanPlaceEdge(Edge edge)
        {
            if (_edges.ContainsKey(edge)) return PlaceResult.Occupied;
            if (!_foundations.ContainsKey(edge.CellA) && !_foundations.ContainsKey(edge.CellB)) return PlaceResult.NeedsFoundation;
            return PlaceResult.Ok;
        }

        public PlaceResult CanPlaceRoof(Cell cell)
        {
            if (_roofs.Contains(cell)) return PlaceResult.Occupied;
            if (!_foundations.ContainsKey(cell)) return PlaceResult.NeedsFoundation;
            foreach (var e in cell.Edges)
                if (_edges.ContainsKey(e)) return PlaceResult.Ok;
            return PlaceResult.NeedsWall;
        }

        public PlaceResult PlaceFoundation(Cell cell, float topHeight)
        {
            var r = CanPlaceFoundation(cell);
            if (r != PlaceResult.Ok) return r;
            _foundations.Add(cell, topHeight);
            CellPieceAdded?.Invoke(PieceType.Foundation, cell);
            return PlaceResult.Ok;
        }

        public PlaceResult PlaceEdge(Edge edge, PieceType type)
        {
            if (type != PieceType.Wall && type != PieceType.Doorway)
                throw new ArgumentException("Only walls and doorways go on edges.", nameof(type));
            var r = CanPlaceEdge(edge);
            if (r != PlaceResult.Ok) return r;
            _edges.Add(edge, type);
            EdgePieceAdded?.Invoke(type, edge);
            return PlaceResult.Ok;
        }

        public PlaceResult PlaceRoof(Cell cell)
        {
            var r = CanPlaceRoof(cell);
            if (r != PlaceResult.Ok) return r;
            _roofs.Add(cell);
            CellPieceAdded?.Invoke(PieceType.Roof, cell);
            return PlaceResult.Ok;
        }

        /// <summary>Removes a foundation and everything that stood only on it.</summary>
        public bool RemoveFoundation(Cell cell)
        {
            if (!_foundations.Remove(cell)) return false;
            CellPieceRemoved?.Invoke(PieceType.Foundation, cell);
            if (_roofs.Remove(cell)) CellPieceRemoved?.Invoke(PieceType.Roof, cell);
            foreach (var e in cell.Edges)
            {
                if (!_edges.TryGetValue(e, out var type)) continue;
                if (_foundations.ContainsKey(e.CellA) || _foundations.ContainsKey(e.CellB)) continue;
                _edges.Remove(e);
                EdgePieceRemoved?.Invoke(type, e);
            }
            RemoveUnsupportedRoofsAround(cell);
            return true;
        }

        /// <summary>Removes a wall or doorway, and any roof left with no wall under it.</summary>
        public bool RemoveEdge(Edge edge)
        {
            if (!_edges.TryGetValue(edge, out var type)) return false;
            _edges.Remove(edge);
            EdgePieceRemoved?.Invoke(type, edge);
            RemoveRoofIfUnsupported(edge.CellA);
            RemoveRoofIfUnsupported(edge.CellB);
            return true;
        }

        public bool RemoveRoof(Cell cell)
        {
            if (!_roofs.Remove(cell)) return false;
            CellPieceRemoved?.Invoke(PieceType.Roof, cell);
            return true;
        }

        private void RemoveUnsupportedRoofsAround(Cell cell)
        {
            RemoveRoofIfUnsupported(new Cell(cell.X + 1, cell.Z));
            RemoveRoofIfUnsupported(new Cell(cell.X - 1, cell.Z));
            RemoveRoofIfUnsupported(new Cell(cell.X, cell.Z + 1));
            RemoveRoofIfUnsupported(new Cell(cell.X, cell.Z - 1));
        }

        private void RemoveRoofIfUnsupported(Cell cell)
        {
            if (!_roofs.Contains(cell)) return;
            bool supported = _foundations.ContainsKey(cell);
            if (supported)
            {
                supported = false;
                foreach (var e in cell.Edges)
                    if (_edges.ContainsKey(e)) { supported = true; break; }
            }
            if (!supported) RemoveRoof(cell);
        }
    }

    /// <summary>What each piece costs to place. Twig tier: cheap, weak, meant to be upgraded later.</summary>
    public static class BuildCosts
    {
        public static IReadOnlyList<Ingredient> For(PieceType type)
        {
            switch (type)
            {
                case PieceType.Foundation: return new[] { new Ingredient(ItemIds.Wood, 50) };
                case PieceType.Wall: return new[] { new Ingredient(ItemIds.Wood, 50) };
                case PieceType.Doorway: return new[] { new Ingredient(ItemIds.Wood, 50) };
                case PieceType.Roof: return new[] { new Ingredient(ItemIds.Wood, 50) };
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static bool CanAfford(Inventory inventory, PieceType type)
        {
            foreach (var c in For(type))
                if (!inventory.Has(c.ItemId, c.Amount)) return false;
            return true;
        }

        /// <summary>Takes the cost, all or nothing.</summary>
        public static bool TryPay(Inventory inventory, PieceType type)
        {
            if (!CanAfford(inventory, type)) return false;
            foreach (var c in For(type)) inventory.Remove(c.ItemId, c.Amount);
            return true;
        }
    }
}
