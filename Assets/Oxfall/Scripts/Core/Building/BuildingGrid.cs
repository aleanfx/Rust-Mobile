using System;
using System.Collections.Generic;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;

namespace Oxfall.Core.Building
{
    /// <summary>Saved and sent as numbers: only ever add at the end.</summary>
    public enum PieceType
    {
        Foundation,
        Wall,
        Doorway,
        /// <summary>A floor laid over a storey's walls: its ceiling, and the next storey's floor.</summary>
        Roof,
        /// <summary>A flight of stairs filling a square, up to the storey above (no floor over it).</summary>
        Stairs,
        /// <summary>A wall with an opening to look and shoot through.</summary>
        Window,
    }

    /// <summary>
    /// A square of the building grid, on one storey: 0 is the ground floor (on foundations), 1 the
    /// one above (on the ground floor's roof), and so on.
    /// </summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public int X { get; }
        public int Z { get; }
        public int Level { get; }
        public Cell(int x, int z, int level = 0) { X = x; Z = z; Level = level; }

        public Edge South => new Edge(X, Z, EdgeAxis.AlongX, Level);
        public Edge North => new Edge(X, Z + 1, EdgeAxis.AlongX, Level);
        public Edge West => new Edge(X, Z, EdgeAxis.AlongZ, Level);
        public Edge East => new Edge(X + 1, Z, EdgeAxis.AlongZ, Level);

        /// <summary>The same square on the ground floor, and on the storeys below and above.</summary>
        public Cell Ground => new Cell(X, Z);
        public Cell Below => new Cell(X, Z, Level - 1);
        public Cell Above => new Cell(X, Z, Level + 1);

        public IEnumerable<Edge> Edges
        {
            get { yield return South; yield return North; yield return West; yield return East; }
        }

        public bool Equals(Cell other) => X == other.X && Z == other.Z && Level == other.Level;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => ((X * 73856093) ^ (Z * 19349663)) + Level * 83492791;
        public override string ToString() => Level == 0 ? $"({X},{Z})" : $"({X},{Z}@{Level})";
    }

    public enum EdgeAxis
    {
        /// <summary>Runs along the X axis, at z = Z × cell size.</summary>
        AlongX,
        /// <summary>Runs along the Z axis, at x = X × cell size.</summary>
        AlongZ,
    }

    /// <summary>A side shared by two cells of one storey. Walls, doorways and windows stand on edges.</summary>
    public readonly struct Edge : IEquatable<Edge>
    {
        public int X { get; }
        public int Z { get; }
        public EdgeAxis Axis { get; }
        public int Level { get; }
        public Edge(int x, int z, EdgeAxis axis, int level = 0) { X = x; Z = z; Axis = axis; Level = level; }

        /// <summary>The two cells that share this edge.</summary>
        public Cell CellA => Axis == EdgeAxis.AlongX ? new Cell(X, Z - 1, Level) : new Cell(X - 1, Z, Level);
        public Cell CellB => new Cell(X, Z, Level);

        /// <summary>The same edge on the storey above.</summary>
        public Edge Above => new Edge(X, Z, Axis, Level + 1);
        public Edge Below => new Edge(X, Z, Axis, Level - 1);

        public bool Equals(Edge other) => X == other.X && Z == other.Z && Axis == other.Axis && Level == other.Level;
        public override bool Equals(object obj) => obj is Edge e && Equals(e);
        public override int GetHashCode() => (((X * 73856093) ^ (Z * 19349663)) * 2 + (int)Axis) + Level * 83492791;
        public override string ToString() => Level == 0 ? $"{Axis}({X},{Z})" : $"{Axis}({X},{Z}@{Level})";
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
        /// <summary>A storey higher than buildings go.</summary>
        TooHigh,
        /// <summary>Above the ground floor, a piece needs the floor (a roof) under it.</summary>
        NeedsFloor,
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

        /// <summary>A door's health: a wooden one falls to one charge, a sheet metal one to two.</summary>
        public static float MaxHealth(DoorKind kind) => kind == DoorKind.Metal ? 250f : 125f;
    }

    /// <summary>
    /// The holes in a doorway and a window, measured on the piece: across from its middle, and up
    /// from its storey's floor. Drawing, colliders and blasts all go by these.
    /// </summary>
    public static class Openings
    {
        public const float DoorwayHalfWidth = 0.6f;
        public const float DoorwayHeight = 2.2f;
        public const float WindowHalfWidth = 0.6f;
        public const float WindowBottom = 1.0f;
        public const float WindowTop = 2.0f;

        public static bool InDoorway(float across, float up) => Math.Abs(across) < DoorwayHalfWidth && up < DoorwayHeight;

        public static bool InWindow(float across, float up) =>
            Math.Abs(across) < WindowHalfWidth && up > WindowBottom && up < WindowTop;
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

        public static PieceRef Foundation(Cell cell) => new PieceRef(PieceType.Foundation, cell.Ground, default);
        public static PieceRef Roof(Cell cell) => new PieceRef(PieceType.Roof, cell, default);
        public static PieceRef Stairs(Cell cell) => new PieceRef(PieceType.Stairs, cell, default);
        public static PieceRef OnEdge(Edge edge, PieceType type)
        {
            if (!IsEdgeType(type)) throw new ArgumentException("Only walls, doorways and windows go on edges.", nameof(type));
            return new PieceRef(type, default, edge);
        }

        /// <summary>The piece of this kind in this cell (foundation, roof or stairs).</summary>
        public static PieceRef InCell(PieceType type, Cell cell) =>
            type == PieceType.Roof ? Roof(cell) : type == PieceType.Stairs ? Stairs(cell) : Foundation(cell);

        public static bool IsEdgeType(PieceType type) => type == PieceType.Wall || type == PieceType.Doorway || type == PieceType.Window;
        public bool IsEdge => IsEdgeType(Type);

        /// <summary>The storey it is on.</summary>
        public int Level => IsEdge ? Edge.Level : Cell.Level;

        public bool Equals(PieceRef other) => IsEdge
            ? other.IsEdge && Edge.Equals(other.Edge)
            : Type == other.Type && Cell.Equals(other.Cell);
        public override bool Equals(object obj) => obj is PieceRef p && Equals(p);
        public override int GetHashCode() => IsEdge ? Edge.GetHashCode() * 3 + 1
            : Cell.GetHashCode() * 3 + (Type == PieceType.Roof ? 2 : 0) + (Type == PieceType.Stairs ? 7 : 0);
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

        /// <summary>The highest storey (0 is the ground floor): four storeys in all.</summary>
        public const int MaxLevel = 3;

        /// <summary>How thick a roof (a floor above) is: standing on it is this much above its storey's top.</summary>
        public const float FloorThickness = 0.2f;

        private readonly Dictionary<Cell, float> _foundations = new Dictionary<Cell, float>();
        private readonly Dictionary<Edge, PieceType> _edges = new Dictionary<Edge, PieceType>();
        private readonly HashSet<Cell> _roofs = new HashSet<Cell>();
        private readonly Dictionary<Cell, int> _stairs = new Dictionary<Cell, int>();
        private readonly List<Edge> _fallingEdges = new List<Edge>();
        private readonly List<Cell> _fallingCells = new List<Cell>();
        private readonly Dictionary<PieceRef, PieceState> _states = new Dictionary<PieceRef, PieceState>();
        private readonly Dictionary<Edge, bool> _doors = new Dictionary<Edge, bool>();
        private readonly Dictionary<Edge, DoorKind> _doorKinds = new Dictionary<Edge, DoorKind>();
        private readonly Dictionary<Edge, float> _doorHealth = new Dictionary<Edge, float>();

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
        /// <summary>
        /// A floor came down (a foundation, or a roof that was the floor above), with the height its
        /// top had: for what stood on it.
        /// </summary>
        public event Action<Cell, float> FoundationRemoved;
        public event Action<Edge> DoorPlaced;
        public event Action<Edge> DoorRemoved;
        /// <summary>A door opened (true) or closed (false).</summary>
        public event Action<Edge, bool> DoorToggled;
        /// <summary>A piece's health changed and it still stands (hurt by a blast, or repaired): the health it has now.</summary>
        public event Action<PieceRef, float> PieceHealthChanged;
        /// <summary>The same for a door.</summary>
        public event Action<Edge, float> DoorHealthChanged;

        public int FoundationCount => _foundations.Count;

        /// <summary>Whether any foundation's square comes within <paramref name="distance"/> of (x, z) across the ground.</summary>
        public bool FoundationNear(float x, float z, float distance)
        {
            if (_foundations.Count == 0) return false;
            int reach = (int)Math.Ceiling(distance / CellSize) + 1;
            var centre = CellAt(x, z);
            for (int cx = centre.X - reach; cx <= centre.X + reach; cx++)
            for (int cz = centre.Z - reach; cz <= centre.Z + reach; cz++)
            {
                if (!_foundations.ContainsKey(new Cell(cx, cz))) continue;
                // The nearest point of that square.
                float nx = Math.Max(cx * CellSize, Math.Min(x, (cx + 1) * CellSize));
                float nz = Math.Max(cz * CellSize, Math.Min(z, (cz + 1) * CellSize));
                if ((nx - x) * (nx - x) + (nz - z) * (nz - z) <= distance * distance) return true;
            }
            return false;
        }
        public IEnumerable<KeyValuePair<Cell, float>> Foundations => _foundations;
        public IEnumerable<KeyValuePair<Edge, PieceType>> EdgePieces => _edges;
        public IEnumerable<Cell> Roofs => _roofs;
        public IEnumerable<Cell> Stairs => _stairs.Keys;

        /// <summary>Which side of its square a flight of stairs comes out on at the top (0 north, 1 east, 2 south, 3 west).</summary>
        public int StairsFacing(Cell cell) => _stairs.TryGetValue(cell, out int facing) ? facing : 0;

        public static Cell CellAt(float worldX, float worldZ, int level = 0) =>
            new Cell((int)Math.Floor(worldX / CellSize), (int)Math.Floor(worldZ / CellSize), level);

        /// <summary>A foundation stands under this square (on any storey).</summary>
        public bool HasFoundation(Cell cell) => _foundations.ContainsKey(cell.Ground);

        /// <summary>The top of the foundation under this square (on any storey).</summary>
        public bool TryGetFoundationTop(Cell cell, out float top) => _foundations.TryGetValue(cell.Ground, out top);
        public bool HasEdgePiece(Edge edge) => _edges.ContainsKey(edge);
        public bool HasRoof(Cell cell) => _roofs.Contains(cell);
        public bool HasStairs(Cell cell) => _stairs.ContainsKey(cell);

        /// <summary>Something to stand on in this square at its storey: a foundation, or the roof of the storey below.</summary>
        public bool HasFloor(Cell cell) => cell.Level == 0 ? _foundations.ContainsKey(cell) : cell.Level > 0 && _roofs.Contains(cell.Below);

        /// <summary>Where a storey of this square begins (the foundation's top, a wall's height for each storey up), if it stands on a foundation.</summary>
        public bool TryGetStoreyBase(Cell cell, out float height)
        {
            if (!_foundations.TryGetValue(cell.Ground, out float top))
            {
                height = 0f;
                return false;
            }
            height = top + cell.Level * WallHeight;
            return true;
        }

        /// <summary>
        /// The height of the floor of this square's storey: the foundation's top on the ground, the top
        /// of the roof below (a wall's height a storey, and the roof's thickness) higher up. Whether
        /// there is a floor there or not.
        /// </summary>
        public bool TryGetFloorHeight(Cell cell, out float height)
        {
            if (!TryGetStoreyBase(cell, out height)) return false;
            if (cell.Level > 0) height += FloorThickness;
            return true;
        }

        /// <summary>
        /// The highest floor (a foundation, or a roof walked on) under (x, z) no higher than <paramref name="y"/>,
        /// with a little slack: where something set down there rests.
        /// </summary>
        public bool TryGetFloorBelow(float x, float y, float z, out float height)
        {
            var column = CellAt(x, z);
            height = 0f;
            if (!_foundations.TryGetValue(column, out float top) || top > y + 0.5f) return false;
            height = top;
            for (int level = 1; level <= MaxLevel + 1; level++)
            {
                var floor = new Cell(column.X, column.Z, level);
                if (!HasFloor(floor)) continue;
                float h = top + level * WallHeight + FloorThickness;
                if (h > y + 0.5f) break;
                height = h;
            }
            return true;
        }

        /// <summary>The storey a height above this square is on (by where storeys begin), from 0 to <see cref="MaxLevel"/>.</summary>
        public int LevelAt(Cell column, float y)
        {
            if (!_foundations.TryGetValue(column.Ground, out float top)) return 0;
            return Math.Max(0, Math.Min(MaxLevel, (int)Math.Floor((y - top + 0.5f) / WallHeight)));
        }
        public bool HasDoor(Edge edge) => _doors.ContainsKey(edge);
        public bool IsDoorOpen(Edge edge) => _doors.TryGetValue(edge, out bool open) && open;

        /// <summary>What the door in a doorway is made of (still known while <see cref="DoorRemoved"/> is raised).</summary>
        public DoorKind DoorKindOf(Edge edge) => _doorKinds.TryGetValue(edge, out var kind) ? kind : DoorKind.Wood;
        public IEnumerable<KeyValuePair<Edge, bool>> Doors => _doors;
        public int PieceCount => _states.Count;

        /// <summary>Every piece standing.</summary>
        public IEnumerable<PieceRef> Pieces => _states.Keys;

        /// <summary>The middle of a piece across the ground: its square's, or its edge's.</summary>
        public static void Centre(PieceRef piece, out float x, out float z)
        {
            if (piece.IsEdge)
            {
                var e = piece.Edge;
                x = (e.X + (e.Axis == EdgeAxis.AlongX ? 0.5f : 0f)) * CellSize;
                z = (e.Z + (e.Axis == EdgeAxis.AlongZ ? 0.5f : 0f)) * CellSize;
                return;
            }
            x = (piece.Cell.X + 0.5f) * CellSize;
            z = (piece.Cell.Z + 0.5f) * CellSize;
        }

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
        public float Health(PieceRef piece) => _states.TryGetValue(Key(piece), out var state) ? state.Health : 0f;

        /// <summary>A piece's health when whole (its tier's), or 0 if there is no such piece.</summary>
        public float MaxHealth(PieceRef piece) =>
            _states.TryGetValue(Key(piece), out var state) ? BuildingTiers.MaxHealth(state.Tier) : 0f;

        /// <summary>A door's health, or 0 if there is no door.</summary>
        public float DoorHealth(Edge edge) =>
            !_doors.ContainsKey(edge) ? 0f : _doorHealth.TryGetValue(edge, out float h) ? h : DoorKinds.MaxHealth(DoorKindOf(edge));

        /// <summary>The pieces and doors not at full health, with what they have left.</summary>
        public IEnumerable<KeyValuePair<PieceRef, float>> Damaged
        {
            get
            {
                foreach (var pair in _states)
                    if (pair.Value.Health < BuildingTiers.MaxHealth(pair.Value.Tier))
                        yield return new KeyValuePair<PieceRef, float>(pair.Key, pair.Value.Health);
            }
        }

        public IEnumerable<KeyValuePair<Edge, float>> DamagedDoors => _doorHealth;

        /// <summary>Height a wall on this edge stands at: the higher of its foundations, or the floor of its storey higher up.</summary>
        public float EdgeBaseHeight(Edge edge)
        {
            bool a = _foundations.TryGetValue(edge.CellA.Ground, out float ta);
            bool b = _foundations.TryGetValue(edge.CellB.Ground, out float tb);
            float lift = edge.Level == 0 ? 0f : edge.Level * WallHeight + FloorThickness;
            if (a && b) return Math.Max(ta, tb) + lift;
            if (a) return ta + lift;
            if (b) return tb + lift;
            return lift;
        }

        public PlaceResult CanPlaceFoundation(Cell cell)
        {
            if (cell.Level != 0) return PlaceResult.TooHigh;
            return _foundations.ContainsKey(cell) ? PlaceResult.Occupied : PlaceResult.Ok;
        }

        public PlaceResult CanPlaceEdge(Edge edge)
        {
            if (edge.Level < 0 || edge.Level > MaxLevel) return PlaceResult.TooHigh;
            if (_edges.ContainsKey(edge)) return PlaceResult.Occupied;
            if (!_foundations.ContainsKey(edge.CellA.Ground) && !_foundations.ContainsKey(edge.CellB.Ground)) return PlaceResult.NeedsFoundation;
            if (!EdgeSupported(edge)) return PlaceResult.NeedsFloor;
            return PlaceResult.Ok;
        }

        public PlaceResult CanPlaceRoof(Cell cell)
        {
            if (cell.Level < 0 || cell.Level > MaxLevel) return PlaceResult.TooHigh;
            if (_roofs.Contains(cell) || _stairs.ContainsKey(cell)) return PlaceResult.Occupied;
            if (!_foundations.ContainsKey(cell.Ground)) return PlaceResult.NeedsFoundation;
            foreach (var e in cell.Edges)
                if (_edges.ContainsKey(e)) return PlaceResult.Ok;
            return PlaceResult.NeedsWall;
        }

        /// <summary>Stairs go up from a floor to the storey above, where no roof may cover them.</summary>
        public PlaceResult CanPlaceStairs(Cell cell)
        {
            if (cell.Level < 0 || cell.Level >= MaxLevel) return PlaceResult.TooHigh;
            if (_stairs.ContainsKey(cell) || _roofs.Contains(cell)) return PlaceResult.Occupied;
            if (!StairsSupported(cell)) return cell.Level == 0 ? PlaceResult.NeedsFoundation : PlaceResult.NeedsFloor;
            return PlaceResult.Ok;
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
            if (!PieceRef.IsEdgeType(type))
                throw new ArgumentException("Only walls, doorways and windows go on edges.", nameof(type));
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

        /// <param name="facing">The side the top comes out on: 0 north, 1 east, 2 south, 3 west.</param>
        public PlaceResult PlaceStairs(Cell cell, int facing = 0)
        {
            if (facing < 0 || facing > 3) throw new ArgumentOutOfRangeException(nameof(facing));
            var r = CanPlaceStairs(cell);
            if (r != PlaceResult.Ok) return r;
            _stairs.Add(cell, facing);
            AddState(PieceRef.Stairs(cell));
            CellPieceAdded?.Invoke(PieceType.Stairs, cell);
            return PlaceResult.Ok;
        }

        /// <summary>Removes a foundation and everything that stood only on it, storey by storey.</summary>
        public bool RemoveFoundation(Cell cell)
        {
            cell = cell.Ground;
            if (!_foundations.TryGetValue(cell, out float top)) return false;
            _foundations.Remove(cell);
            Forget(PieceRef.Foundation(cell));
            CellPieceRemoved?.Invoke(PieceType.Foundation, cell);
            Settle();
            FoundationRemoved?.Invoke(cell, top);
            return true;
        }

        /// <summary>Removes a wall, doorway or window, and whatever it alone held up.</summary>
        public bool RemoveEdge(Edge edge)
        {
            if (!_edges.ContainsKey(edge)) return false;
            TakeEdge(edge);
            Settle();
            return true;
        }

        /// <summary>Removes a roof, and whatever stood only on it (the storey above's walls and stairs).</summary>
        public bool RemoveRoof(Cell cell)
        {
            if (!TakeRoof(cell)) return false;
            Settle();
            return true;
        }

        public bool RemoveStairs(Cell cell)
        {
            if (!_stairs.Remove(cell)) return false;
            Forget(PieceRef.Stairs(cell));
            CellPieceRemoved?.Invoke(PieceType.Stairs, cell);
            return true;
        }

        /// <summary>Removes any piece, and what stood only on it.</summary>
        public bool Remove(PieceRef piece)
        {
            switch (piece.Type)
            {
                case PieceType.Foundation: return RemoveFoundation(piece.Cell);
                case PieceType.Roof: return RemoveRoof(piece.Cell);
                case PieceType.Stairs: return RemoveStairs(piece.Cell);
                default: return RemoveEdge(piece.Edge);
            }
        }

        private bool TakeRoof(Cell cell)
        {
            if (!_roofs.Remove(cell)) return false;
            Forget(PieceRef.Roof(cell));
            CellPieceRemoved?.Invoke(PieceType.Roof, cell);
            // What stood on it falls (a roof is the floor of the storey above).
            if (TryGetStoreyBase(cell, out float storey)) FoundationRemoved?.Invoke(cell, storey + WallHeight + FloorThickness);
            return true;
        }

        /// <summary>
        /// Takes down, until nothing more falls, every piece left with nothing to hold it up: walls with
        /// no foundation (or, above, no floor) on either side, roofs with no wall round them, stairs
        /// with no floor under them.
        /// </summary>
        private void Settle()
        {
            bool fell = true;
            while (fell)
            {
                fell = false;
                _fallingEdges.Clear();
                foreach (var e in _edges.Keys)
                    if (!EdgeHeld(e)) _fallingEdges.Add(e);
                foreach (var e in _fallingEdges) TakeEdge(e);
                fell |= _fallingEdges.Count > 0;

                _fallingCells.Clear();
                foreach (var c in _roofs)
                    if (!RoofHeld(c)) _fallingCells.Add(c);
                foreach (var c in _fallingCells) TakeRoof(c);
                fell |= _fallingCells.Count > 0;

                _fallingCells.Clear();
                foreach (var c in _stairs.Keys)
                    if (!StairsSupported(c)) _fallingCells.Add(c);
                foreach (var c in _fallingCells) RemoveStairs(c);
                fell |= _fallingCells.Count > 0;
            }
        }

        private bool EdgeHeld(Edge e) =>
            (_foundations.ContainsKey(e.CellA.Ground) || _foundations.ContainsKey(e.CellB.Ground)) && EdgeSupported(e);

        /// <summary>Above the ground, a wall stands on a floor on either side of it, or on the wall under it.</summary>
        private bool EdgeSupported(Edge e) => e.Level == 0 || HasFloor(e.CellA) || HasFloor(e.CellB) || _edges.ContainsKey(e.Below);

        /// <summary>Stairs stand on a floor, or on the stairs below them (a stairwell going up storey after storey).</summary>
        private bool StairsSupported(Cell c) => HasFloor(c) || (c.Level > 0 && _stairs.ContainsKey(c.Below));

        private bool RoofHeld(Cell c)
        {
            if (!_foundations.ContainsKey(c.Ground)) return false;
            foreach (var e in c.Edges)
                if (_edges.ContainsKey(e)) return true;
            return false;
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
            if (amount <= 0f || !_states.TryGetValue(Key(piece), out var state)) return false;
            return SetHealth(piece, state.Health - amount);
        }

        /// <summary>
        /// Sets a piece's health (at most its tier's). At zero or less it falls, with what stood on it:
        /// true then. False if it still stands, or there is no such piece.
        /// </summary>
        public bool SetHealth(PieceRef piece, float health)
        {
            var key = Key(piece);
            if (!_states.TryGetValue(key, out var state)) return false;
            if (health <= 0f)
            {
                Remove(key);
                return true;
            }
            health = Math.Min(health, BuildingTiers.MaxHealth(state.Tier));
            if (health == state.Health) return false;
            state.Health = health;
            PieceHealthChanged?.Invoke(key, health);
            return false;
        }

        /// <summary>Takes health off a door; at zero it is gone (its lock with it). True if it broke.</summary>
        public bool DamageDoor(Edge edge, float amount) => amount > 0f && HasDoor(edge) && SetDoorHealth(edge, DoorHealth(edge) - amount);

        /// <summary>Sets a door's health (at most its kind's); at zero or less it is gone. True if it broke.</summary>
        public bool SetDoorHealth(Edge edge, float health)
        {
            if (!HasDoor(edge)) return false;
            if (health <= 0f)
            {
                RemoveDoor(edge);
                return true;
            }
            float max = DoorKinds.MaxHealth(DoorKindOf(edge));
            health = Math.Min(health, max);
            if (health == DoorHealth(edge)) return false;
            if (health >= max) _doorHealth.Remove(edge);
            else _doorHealth[edge] = health;
            DoorHealthChanged?.Invoke(edge, health);
            return false;
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
            _doorHealth.Remove(edge);
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
                case PieceType.Window:
                    return new[] { new Ingredient(ItemIds.Wood, TwigWood) };
                case PieceType.Stairs:
                    return new[] { new Ingredient(ItemIds.Wood, TwigWood * 2) };
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
