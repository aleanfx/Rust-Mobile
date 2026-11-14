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
        NeedsDoorway,
        NoPiece,
        NotAnUpgrade,
    }

    /// <summary>
    /// What a piece is made of. Everything goes up as twig (cheap, falls to a few hits) and is
    /// upgraded with the hammer. Saved and sent as numbers: only ever add at the end.
    /// </summary>
    public enum BuildingTier
    {
        Twig,
        Wood,
        Stone,
        Metal,
    }

    /// <summary>What a door is made of. Sent and saved as numbers: only ever add at the end.</summary>
    public enum DoorKind : byte
    {
        Wood = 1,
        Metal = 2,
    }

    public static class DoorKinds
    {
        public static bool IsKnown(DoorKind kind) => kind == DoorKind.Wood || kind == DoorKind.Metal;

        /// <summary>The item that hangs as this door (and comes back when it is taken down).</summary>
        public static string ItemOf(DoorKind kind) => kind == DoorKind.Metal ? ItemIds.SheetMetalDoor : ItemIds.WoodenDoor;

        public static bool TryKindOf(string itemId, out DoorKind kind)
        {
            kind = itemId == ItemIds.SheetMetalDoor ? DoorKind.Metal : DoorKind.Wood;
            return itemId == ItemIds.WoodenDoor || itemId == ItemIds.SheetMetalDoor;
        }

        public static string Name(DoorKind kind) => kind == DoorKind.Metal ? "Puerta de chapa" : "Puerta de madera";
    }

    public static class BuildingTiers
    {
        public const BuildingTier Highest = BuildingTier.Metal;

        public static float MaxHealth(BuildingTier tier)
        {
            switch (tier)
            {
                case BuildingTier.Twig: return 10f;
                case BuildingTier.Wood: return 250f;
                case BuildingTier.Stone: return 500f;
                case BuildingTier.Metal: return 1000f;
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
        }

        public static string Name(BuildingTier tier)
        {
            switch (tier)
            {
                case BuildingTier.Twig: return "Ramas";
                case BuildingTier.Wood: return "Madera";
                case BuildingTier.Stone: return "Piedra";
                case BuildingTier.Metal: return "Metal";
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
        }

        /// <summary>The next tier up, or false at the top.</summary>
        public static bool TryNext(BuildingTier tier, out BuildingTier next)
        {
            next = tier + 1;
            return tier < Highest;
        }
    }

    /// <summary>
    /// Names one placed piece: a foundation or roof by its cell, a wall or doorway by its edge
    /// (an edge holds one piece, so a wall and a doorway on the same edge are the same place).
    /// </summary>
    public readonly struct PieceRef : IEquatable<PieceRef>
    {
        public PieceType Type { get; }
        public Cell Cell { get; }
        public Edge Edge { get; }

        private PieceRef(PieceType type, Cell cell, Edge edge) { Type = type; Cell = cell; Edge = edge; }

        public static PieceRef Foundation(Cell cell) => new PieceRef(PieceType.Foundation, cell, default);
        public static PieceRef Roof(Cell cell) => new PieceRef(PieceType.Roof, cell, default);
        public static PieceRef OnEdge(Edge edge, PieceType type)
        {
            if (!IsEdgeType(type)) throw new ArgumentException("Only walls and doorways go on edges.", nameof(type));
            return new PieceRef(type, default, edge);
        }

        public static bool IsEdgeType(PieceType type) => type == PieceType.Wall || type == PieceType.Doorway;
        public bool IsEdge => IsEdgeType(Type);

        public bool Equals(PieceRef other) => IsEdge
            ? other.IsEdge && Edge.Equals(other.Edge)
            : Type == other.Type && Cell.Equals(other.Cell);
        public override bool Equals(object obj) => obj is PieceRef p && Equals(p);
        public override int GetHashCode() => IsEdge ? Edge.GetHashCode() * 3 + 1 : Cell.GetHashCode() * 3 + (Type == PieceType.Roof ? 2 : 0);
        public override string ToString() => IsEdge ? $"{Type} {Edge}" : $"{Type} {Cell}";
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
        private readonly Dictionary<PieceRef, PieceState> _states = new Dictionary<PieceRef, PieceState>();
        private readonly Dictionary<Edge, bool> _doors = new Dictionary<Edge, bool>();
        private readonly Dictionary<Edge, DoorKind> _doorKinds = new Dictionary<Edge, DoorKind>();

        private sealed class PieceState
        {
            public BuildingTier Tier;
            public float Health;
        }

        public event Action<PieceType, Cell> CellPieceAdded;
        public event Action<PieceType, Edge> EdgePieceAdded;
        public event Action<PieceType, Cell> CellPieceRemoved;
        public event Action<PieceType, Edge> EdgePieceRemoved;
        /// <summary>A piece was upgraded to a new tier (and healed to that tier's full health).</summary>
        public event Action<PieceRef, BuildingTier> PieceUpgraded;
        /// <summary>Any piece came down (demolished, destroyed, or left with nothing under it), with the tier it had.</summary>
        public event Action<PieceRef, BuildingTier> PieceRemoved;
        public event Action<Edge> DoorPlaced;
        public event Action<Edge> DoorRemoved;
        /// <summary>A door opened (true) or closed (false).</summary>
        public event Action<Edge, bool> DoorToggled;

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
        public bool HasDoor(Edge edge) => _doors.ContainsKey(edge);
        public bool IsDoorOpen(Edge edge) => _doors.TryGetValue(edge, out bool open) && open;

        /// <summary>What the door in a doorway is made of (still known while <see cref="DoorRemoved"/> is raised).</summary>
        public DoorKind DoorKindOf(Edge edge) => _doorKinds.TryGetValue(edge, out var kind) ? kind : DoorKind.Wood;
        public IEnumerable<KeyValuePair<Edge, bool>> Doors => _doors;
        public int PieceCount => _states.Count;

        public bool Exists(PieceRef piece) => _states.ContainsKey(piece);

        /// <summary>The piece standing on this edge, if any.</summary>
        public bool TryGetEdgePiece(Edge edge, out PieceRef piece)
        {
            if (_edges.TryGetValue(edge, out var type))
            {
                piece = PieceRef.OnEdge(edge, type);
                return true;
            }
            piece = default;
            return false;
        }

        public bool TryGetTier(PieceRef piece, out BuildingTier tier)
        {
            if (_states.TryGetValue(piece, out var state))
            {
                tier = state.Tier;
                return true;
            }
            tier = default;
            return false;
        }

        /// <summary>Health left, or 0 if there is no such piece.</summary>
        public float Health(PieceRef piece) => _states.TryGetValue(piece, out var state) ? state.Health : 0f;

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
            AddState(PieceRef.Foundation(cell));
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
            AddState(PieceRef.OnEdge(edge, type));
            EdgePieceAdded?.Invoke(type, edge);
            return PlaceResult.Ok;
        }

        public PlaceResult PlaceRoof(Cell cell)
        {
            var r = CanPlaceRoof(cell);
            if (r != PlaceResult.Ok) return r;
            _roofs.Add(cell);
            AddState(PieceRef.Roof(cell));
            CellPieceAdded?.Invoke(PieceType.Roof, cell);
            return PlaceResult.Ok;
        }

        /// <summary>Removes a foundation and everything that stood only on it.</summary>
        public bool RemoveFoundation(Cell cell)
        {
            if (!_foundations.Remove(cell)) return false;
            Forget(PieceRef.Foundation(cell));
            CellPieceRemoved?.Invoke(PieceType.Foundation, cell);
            RemoveRoof(cell);
            foreach (var e in cell.Edges)
            {
                if (!_edges.ContainsKey(e)) continue;
                if (_foundations.ContainsKey(e.CellA) || _foundations.ContainsKey(e.CellB)) continue;
                TakeEdge(e);
            }
            RemoveUnsupportedRoofsAround(cell);
            return true;
        }

        /// <summary>Removes a wall or doorway, and any roof left with no wall under it.</summary>
        public bool RemoveEdge(Edge edge)
        {
            if (!_edges.ContainsKey(edge)) return false;
            TakeEdge(edge);
            RemoveRoofIfUnsupported(edge.CellA);
            RemoveRoofIfUnsupported(edge.CellB);
            return true;
        }

        public bool RemoveRoof(Cell cell)
        {
            if (!_roofs.Remove(cell)) return false;
            Forget(PieceRef.Roof(cell));
            CellPieceRemoved?.Invoke(PieceType.Roof, cell);
            return true;
        }

        /// <summary>Removes any piece, and what stood only on it.</summary>
        public bool Remove(PieceRef piece)
        {
            switch (piece.Type)
            {
                case PieceType.Foundation: return RemoveFoundation(piece.Cell);
                case PieceType.Roof: return RemoveRoof(piece.Cell);
                default: return RemoveEdge(piece.Edge);
            }
        }

        public PlaceResult CanUpgrade(PieceRef piece, BuildingTier tier)
        {
            if (!_states.TryGetValue(piece, out var state)) return PlaceResult.NoPiece;
            return tier > state.Tier && tier <= BuildingTiers.Highest ? PlaceResult.Ok : PlaceResult.NotAnUpgrade;
        }

        /// <summary>Raises a piece to a higher tier, at that tier's full health. Paying is the caller's job.</summary>
        public PlaceResult Upgrade(PieceRef piece, BuildingTier tier)
        {
            var r = CanUpgrade(piece, tier);
            if (r != PlaceResult.Ok) return r;
            var state = _states[piece];
            state.Tier = tier;
            state.Health = BuildingTiers.MaxHealth(tier);
            PieceUpgraded?.Invoke(Key(piece), tier);
            return PlaceResult.Ok;
        }

        /// <summary>Takes health off a piece; at zero it falls, with what stood on it. True if it fell.</summary>
        public bool Damage(PieceRef piece, float amount)
        {
            if (amount <= 0f || !_states.TryGetValue(piece, out var state)) return false;
            state.Health -= amount;
            if (state.Health > 0f) return false;
            Remove(Key(piece));
            return true;
        }

        public PlaceResult CanPlaceDoor(Edge edge)
        {
            if (!_edges.TryGetValue(edge, out var type) || type != PieceType.Doorway) return PlaceResult.NeedsDoorway;
            return _doors.ContainsKey(edge) ? PlaceResult.Occupied : PlaceResult.Ok;
        }

        /// <summary>Hangs a door in a doorway, closed.</summary>
        public PlaceResult PlaceDoor(Edge edge, DoorKind kind = DoorKind.Wood)
        {
            var r = CanPlaceDoor(edge);
            if (r != PlaceResult.Ok) return r;
            _doors.Add(edge, false);
            _doorKinds[edge] = kind;
            DoorPlaced?.Invoke(edge);
            return PlaceResult.Ok;
        }

        /// <summary>Opens a closed door or closes an open one. False if there is no door.</summary>
        public bool ToggleDoor(Edge edge)
        {
            if (!_doors.TryGetValue(edge, out bool open)) return false;
            _doors[edge] = !open;
            DoorToggled?.Invoke(edge, !open);
            return true;
        }

        public bool RemoveDoor(Edge edge)
        {
            if (!_doors.Remove(edge)) return false;
            DoorRemoved?.Invoke(edge);
            _doorKinds.Remove(edge);
            return true;
        }

        private void AddState(PieceRef piece) =>
            _states[piece] = new PieceState { Tier = BuildingTier.Twig, Health = BuildingTiers.MaxHealth(BuildingTier.Twig) };

        /// <summary>The stored ref for a piece, with its real type (a ref made from an edge may name the wrong one).</summary>
        private PieceRef Key(PieceRef piece) =>
            piece.IsEdge && _edges.TryGetValue(piece.Edge, out var type) ? PieceRef.OnEdge(piece.Edge, type) : piece;

        /// <summary>Takes the piece off an edge, and its door with it.</summary>
        private void TakeEdge(Edge edge)
        {
            var type = _edges[edge];
            RemoveDoor(edge);
            _edges.Remove(edge);
            Forget(PieceRef.OnEdge(edge, type));
            EdgePieceRemoved?.Invoke(type, edge);
        }

        private void Forget(PieceRef piece)
        {
            var tier = _states.TryGetValue(piece, out var state) ? state.Tier : BuildingTier.Twig;
            _states.Remove(piece);
            PieceRemoved?.Invoke(piece, tier);
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

    /// <summary>
    /// What building costs. Pieces go up as twig, cheap and weak; the hammer upgrades them. Taking a
    /// piece down gives back half of everything put into it.
    /// </summary>
    public static class BuildCosts
    {
        public const int TwigWood = 50;
        public const int WoodUpgrade = 150;
        public const int StoneUpgrade = 200;
        public const int MetalUpgrade = 200;

        /// <summary>The cost of placing a piece (as twig).</summary>
        public static IReadOnlyList<Ingredient> For(PieceType type)
        {
            switch (type)
            {
                case PieceType.Foundation:
                case PieceType.Wall:
                case PieceType.Doorway:
                case PieceType.Roof:
                    return new[] { new Ingredient(ItemIds.Wood, TwigWood) };
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>The cost of raising a piece to this tier from the one below.</summary>
        public static IReadOnlyList<Ingredient> Upgrade(PieceType type, BuildingTier tier)
        {
            switch (tier)
            {
                case BuildingTier.Wood: return new[] { new Ingredient(ItemIds.Wood, WoodUpgrade) };
                case BuildingTier.Stone: return new[] { new Ingredient(ItemIds.Stone, StoneUpgrade) };
                case BuildingTier.Metal: return new[] { new Ingredient(ItemIds.MetalFragments, MetalUpgrade) };
                default: throw new ArgumentOutOfRangeException(nameof(tier), "Twig is placed, not upgraded to.");
            }
        }

        /// <summary>Half of the placing cost and of every upgrade up to this tier, rounded down.</summary>
        public static IReadOnlyList<Ingredient> Refund(PieceType type, BuildingTier tier)
        {
            var totals = new Dictionary<string, int>();
            void Add(IReadOnlyList<Ingredient> costs)
            {
                foreach (var c in costs)
                {
                    totals.TryGetValue(c.ItemId, out int n);
                    totals[c.ItemId] = n + c.Amount;
                }
            }
            Add(For(type));
            for (var t = BuildingTier.Wood; t <= tier; t++) Add(Upgrade(type, t));

            var refund = new List<Ingredient>(totals.Count);
            foreach (var pair in totals)
                if (pair.Value / 2 > 0) refund.Add(new Ingredient(pair.Key, pair.Value / 2));
            return refund;
        }

        public static bool CanAfford(Inventory inventory, PieceType type) => CanAfford(inventory, For(type));

        /// <summary>Takes the cost, all or nothing.</summary>
        public static bool TryPay(Inventory inventory, PieceType type) => TryPay(inventory, For(type));

        public static bool CanAfford(Inventory inventory, IReadOnlyList<Ingredient> costs)
        {
            foreach (var c in costs)
                if (!inventory.Has(c.ItemId, c.Amount)) return false;
            return true;
        }

        /// <summary>Takes the cost, all or nothing.</summary>
        public static bool TryPay(Inventory inventory, IReadOnlyList<Ingredient> costs)
        {
            if (!CanAfford(inventory, costs)) return false;
            foreach (var c in costs) inventory.Remove(c.ItemId, c.Amount);
            return true;
        }

        /// <summary>Describes a cost for the player: "150 Madera".</summary>
        public static string Describe(IReadOnlyList<Ingredient> costs, ItemCatalog catalog)
        {
            var parts = new List<string>(costs.Count);
            foreach (var c in costs)
                parts.Add($"{c.Amount} {(catalog.TryGet(c.ItemId, out var d) ? d.DisplayName : c.ItemId)}");
            return string.Join(" + ", parts);
        }
    }
}
