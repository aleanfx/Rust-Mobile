using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Game.Rendering;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Construction
{
    /// <summary>Marks a building piece's object (or a door's) with the piece it shows.</summary>
    public sealed class PieceTag : MonoBehaviour
    {
        public PieceRef Piece;
        public bool IsDoor;
    }

    /// <summary>
    /// Draws what the Core's <see cref="BuildingGrid"/> holds and nothing else: it listens to the
    /// grid's events and adds, re-skins and removes objects to match. Whoever changes the grid (the
    /// player now, the server later) never touches these objects.
    /// </summary>
    public sealed class BuildingViews
    {
        private const float S = BuildingGrid.CellSize;

        private readonly BuildingGrid _grid;
        private readonly MaterialLibrary _materials;
        private readonly Transform _root;
        private static readonly int PieceTypes = System.Enum.GetValues(typeof(PieceType)).Length;

        private readonly Mesh[] _solid = new Mesh[PieceTypes];
        private readonly Mesh _stairsCollider;
        private readonly Mesh[] _twig = new Mesh[PieceTypes];
        private readonly Bounds[][] _boxes = new Bounds[PieceTypes][];
        private readonly Dictionary<PieceRef, GameObject> _pieces = new Dictionary<PieceRef, GameObject>();
        private readonly Dictionary<Edge, DoorView> _doors = new Dictionary<Edge, DoorView>();

        public BuildingGrid Grid => _grid;
        public Mesh DoorMesh { get; }
        public Mesh MetalDoorMesh { get; }

        public Mesh DoorMeshFor(DoorKind kind) => kind == DoorKind.Metal ? MetalDoorMesh : DoorMesh;

        public Material[] DoorMaterialsFor(DoorKind kind) =>
            kind == DoorKind.Metal ? new[] { _materials.SheetMetal, _materials.Metal } : new[] { _materials.Door };

        /// <summary>The door hanging in a doorway (it swings: things on it go under it).</summary>
        public bool TryGetDoor(Edge edge, out Transform door)
        {
            door = _doors.TryGetValue(edge, out var view) ? view.transform : null;
            return door != null;
        }

        /// <summary>Who opens doors: a door swings away from them.</summary>
        public Transform Viewer;

        public BuildingViews(BuildingGrid grid, MaterialLibrary materials)
        {
            _grid = grid;
            _materials = materials;
            _root = new GameObject("Buildings").transform;

            // Each solid piece is a few boxes: the same list makes its mesh and its colliders.
            _boxes[(int)PieceType.Foundation] = new[] { Box(0f, -1.25f, 0f, S, 2.5f, S) };
            _boxes[(int)PieceType.Wall] = new[] { Box(0f, 1.5f, 0f, S, BuildingGrid.WallHeight, 0.2f) };
            _boxes[(int)PieceType.Doorway] = new[]
            {
                Box(-1.05f, 1.5f, 0f, 0.9f, BuildingGrid.WallHeight, 0.2f),
                Box(1.05f, 1.5f, 0f, 0.9f, BuildingGrid.WallHeight, 0.2f),
                Box(0f, 2.6f, 0f, 1.2f, 0.8f, 0.2f),
            };
            _boxes[(int)PieceType.Roof] = new[] { Box(0f, 0.1f, 0f, S + 0.2f, BuildingGrid.FloorThickness, S + 0.2f) };
            float sill = Openings.WindowBottom, lintel = Openings.WindowTop, side = S * 0.5f - Openings.WindowHalfWidth;
            _boxes[(int)PieceType.Window] = new[]
            {
                Box(-(S * 0.5f - side * 0.5f), 1.5f, 0f, side, BuildingGrid.WallHeight, 0.2f),
                Box(S * 0.5f - side * 0.5f, 1.5f, 0f, side, BuildingGrid.WallHeight, 0.2f),
                Box(0f, sill * 0.5f, 0f, Openings.WindowHalfWidth * 2f, sill, 0.2f),
                Box(0f, (lintel + BuildingGrid.WallHeight) * 0.5f, 0f, Openings.WindowHalfWidth * 2f, BuildingGrid.WallHeight - lintel, 0.2f),
            };
            for (int i = 0; i < _solid.Length; i++)
                if (_boxes[i] != null) _solid[i] = MeshFactory.Boxes(((PieceType)i).ToString(), _boxes[i]);
            // Stairs are drawn for the ground storey's rise and squashed a little for the ones above.
            _solid[(int)PieceType.Stairs] = MeshFactory.SpiralStairs(GroundRise, false);
            _stairsCollider = MeshFactory.SpiralStairsCollider(GroundRise);
            // Twig: a solid block of sticks for floors, see-through frames for the rest.
            _twig[(int)PieceType.Foundation] = _solid[(int)PieceType.Foundation];
            _twig[(int)PieceType.Wall] = MeshFactory.TwigWall(false);
            _twig[(int)PieceType.Doorway] = MeshFactory.TwigWall(true);
            _twig[(int)PieceType.Roof] = MeshFactory.TwigRoof();
            _twig[(int)PieceType.Window] = MeshFactory.TwigWindow();
            _twig[(int)PieceType.Stairs] = MeshFactory.SpiralStairs(GroundRise, true);
            DoorMesh = MeshFactory.Door();
            MetalDoorMesh = MeshFactory.MetalDoor();

            grid.CellPieceAdded += (type, cell) => Spawn(PieceRef.InCell(type, cell));
            grid.EdgePieceAdded += (type, edge) => Spawn(PieceRef.OnEdge(edge, type));
            grid.PieceRemoved += (piece, tier) => Despawn(piece);
            grid.PieceUpgraded += (piece, tier) => Skin(piece, tier);
            grid.DoorPlaced += SpawnDoor;
            grid.DoorRemoved += DespawnDoor;
            grid.DoorToggled += (edge, open) =>
            {
                if (_doors.TryGetValue(edge, out var door)) door.SetOpen(open, Viewer != null ? Viewer.position : door.transform.position);
            };
            // Blasted pieces and doors darken, scorched, the more the less health they have left.
            grid.PieceHealthChanged += (piece, health) =>
            {
                if (_pieces.TryGetValue(piece, out var go)) Scorch(go.GetComponent<MeshRenderer>(), health / Mathf.Max(1f, grid.MaxHealth(piece)));
            };
            grid.DoorHealthChanged += (edge, health) =>
            {
                if (_doors.TryGetValue(edge, out var door))
                    Scorch(door.GetComponent<MeshRenderer>(), health / DoorKinds.MaxHealth(grid.DoorKindOf(edge)));
            };
        }

        /// <summary>A piece or door is coming down: where it stood (its bounds) and what it was made of, just before it goes.</summary>
        public event System.Action<Bounds, Material> Fell;

        private static MaterialPropertyBlock _scorch;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Blackens what a blast hurt, the more the less health it has left (full health: as made).</summary>
        public static void Scorch(MeshRenderer renderer, float health01)
        {
            if (renderer == null) return;
            var materials = renderer.sharedMaterials;
            if (health01 >= 0.999f)
            {
                for (int i = 0; i < materials.Length; i++) renderer.SetPropertyBlock(null, i);
                return;
            }
            if (_scorch == null) _scorch = new MaterialPropertyBlock();
            float shade = Mathf.Lerp(0.4f, 0.92f, Mathf.Clamp01(health01));
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !materials[i].HasProperty(BaseColorId)) continue;
                var c = materials[i].GetColor(BaseColorId);
                _scorch.Clear();
                _scorch.SetColor(BaseColorId, new Color(c.r * shade, c.g * shade * 0.97f, c.b * shade * 0.94f, c.a));
                renderer.SetPropertyBlock(_scorch, i);
            }
        }

        /// <summary>The solid shape of a piece, for the placing ghost.</summary>
        public Mesh MeshFor(PieceType type) => _solid[(int)type];

        public int Count => _pieces.Count;

        public bool TryGet(PieceRef piece, out GameObject view) => _pieces.TryGetValue(piece, out view);

        public static Vector3 EdgeMid(Edge edge) => edge.Axis == EdgeAxis.AlongX
            ? new Vector3((edge.X + 0.5f) * S, 0f, edge.Z * S)
            : new Vector3(edge.X * S, 0f, (edge.Z + 0.5f) * S);

        public static Quaternion EdgeRotation(Edge edge) =>
            edge.Axis == EdgeAxis.AlongZ ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

        /// <summary>How high the ground storey's stairs climb: from the foundation's top to the top of the roof above.</summary>
        public const float GroundRise = BuildingGrid.WallHeight + BuildingGrid.FloorThickness;

        /// <summary>Which way stairs facing this side (0 north, 1 east, 2 south, 3 west) are turned.</summary>
        public static Quaternion StairsRotation(int facing) => Quaternion.Euler(0f, facing * 90f, 0f);

        /// <summary>How much the stairs' meshes are squashed on a storey (whose floor-to-floor rise is a wall's height above the ground storey).</summary>
        public static float StairsSquash(int level) => level == 0 ? 1f : BuildingGrid.WallHeight / GroundRise;

        /// <summary>Where a placed piece stands (its origin: the floor under it, the middle of its cell or edge).</summary>
        public Vector3 PositionOf(PieceRef piece)
        {
            switch (piece.Type)
            {
                case PieceType.Foundation:
                    _grid.TryGetFoundationTop(piece.Cell, out float top);
                    return new Vector3((piece.Cell.X + 0.5f) * S, top, (piece.Cell.Z + 0.5f) * S);
                case PieceType.Roof:
                    _grid.TryGetStoreyBase(piece.Cell, out float storey);
                    return new Vector3((piece.Cell.X + 0.5f) * S, storey + BuildingGrid.WallHeight, (piece.Cell.Z + 0.5f) * S);
                case PieceType.Stairs:
                    _grid.TryGetFloorHeight(piece.Cell, out float floor);
                    return new Vector3((piece.Cell.X + 0.5f) * S, floor, (piece.Cell.Z + 0.5f) * S);
                default:
                    var mid = EdgeMid(piece.Edge);
                    mid.y = _grid.EdgeBaseHeight(piece.Edge);
                    return mid;
            }
        }

        /// <summary>Where a door's hinge goes in a doorway, and which way the closed door faces.</summary>
        public void DoorPose(Edge edge, out Vector3 hinge, out Quaternion rotation)
        {
            rotation = EdgeRotation(edge);
            var basePoint = EdgeMid(edge);
            basePoint.y = _grid.EdgeBaseHeight(edge) + 0.01f;
            hinge = basePoint + rotation * new Vector3(-0.58f, 0f, 0f);
        }

        private void Spawn(PieceRef piece)
        {
            if (_pieces.ContainsKey(piece)) return;
            var go = new GameObject(piece.ToString());
            go.transform.SetParent(_root, false);
            var rotation = piece.IsEdge ? EdgeRotation(piece.Edge)
                : piece.Type == PieceType.Stairs ? StairsRotation(_grid.StairsFacing(piece.Cell)) : Quaternion.identity;
            go.transform.SetPositionAndRotation(PositionOf(piece), rotation);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            if (piece.Type == PieceType.Stairs)
            {
                go.transform.localScale = new Vector3(1f, StairsSquash(piece.Level), 1f);
                // Steps to walk up: the solid steps' own shape, whatever the tier looks like.
                go.AddComponent<MeshCollider>().sharedMesh = _stairsCollider;
            }
            else
                foreach (var box in _boxes[(int)piece.Type])
                {
                    var collider = go.AddComponent<BoxCollider>();
                    collider.center = box.center;
                    collider.size = box.size;
                }
            go.AddComponent<PieceTag>().Piece = piece;
            _pieces.Add(piece, go);
            _grid.TryGetTier(piece, out var tier);
            Skin(piece, tier);
        }

        /// <summary>Twig is sticks, wood is planks, stone is blocks, metal is sheets. The colliders never change.</summary>
        private void Skin(PieceRef piece, BuildingTier tier)
        {
            if (!_pieces.TryGetValue(piece, out var go)) return;
            int i = (int)piece.Type;
            go.GetComponent<MeshFilter>().sharedMesh = tier == BuildingTier.Twig ? _twig[i] : _solid[i];
            var renderer = go.GetComponent<MeshRenderer>();
            // A new tier is whole again: no scorch.
            renderer.SetPropertyBlock(null, 0);
            renderer.sharedMaterial = tier == BuildingTier.Twig ? _materials.Twig
                : tier == BuildingTier.Stone ? _materials.StoneWall
                : tier == BuildingTier.Metal ? _materials.SheetMetal : _materials.WoodWall;
            // Thin sticks cast thin, flickering shadows for little gain.
            renderer.shadowCastingMode = tier == BuildingTier.Twig && piece.Type != PieceType.Foundation && piece.Type != PieceType.Stairs
                ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }

        private void Despawn(PieceRef piece)
        {
            if (!_pieces.TryGetValue(piece, out var go)) return;
            _pieces.Remove(piece);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) Fell?.Invoke(renderer.bounds, renderer.sharedMaterial);
            Object.Destroy(go);
        }

        private void SpawnDoor(Edge edge)
        {
            if (_doors.ContainsKey(edge) || !_grid.TryGetEdgePiece(edge, out var doorway)) return;
            DoorPose(edge, out var hinge, out var rotation);
            var go = new GameObject("Door " + edge);
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(hinge, rotation);
            var kind = _grid.DoorKindOf(edge);
            go.AddComponent<MeshFilter>().sharedMesh = DoorMeshFor(kind);
            go.AddComponent<MeshRenderer>().sharedMaterials = DoorMaterialsFor(kind);
            var collider = go.AddComponent<BoxCollider>();
            collider.center = new Vector3(0.58f, 1.08f, 0f);
            collider.size = new Vector3(1.14f, 2.16f, 0.1f);
            var tag = go.AddComponent<PieceTag>();
            tag.Piece = doorway;
            tag.IsDoor = true;
            var view = go.AddComponent<DoorView>();
            view.Init(rotation);
            _doors.Add(edge, view);
        }

        private void DespawnDoor(Edge edge)
        {
            if (!_doors.TryGetValue(edge, out var door)) return;
            _doors.Remove(edge);
            var renderer = door.GetComponent<MeshRenderer>();
            if (renderer != null) Fell?.Invoke(renderer.bounds, renderer.sharedMaterial);
            Object.Destroy(door.gameObject);
        }

        private static Bounds Box(float cx, float cy, float cz, float sx, float sy, float sz) =>
            new Bounds(new Vector3(cx, cy, cz), new Vector3(sx, sy, sz));
    }

    /// <summary>Swings a door open, away from whoever opened it, or shut.</summary>
    public sealed class DoorView : MonoBehaviour
    {
        private Quaternion _closed;
        private Quaternion _target;

        public void Init(Quaternion closed)
        {
            _closed = closed;
            _target = closed;
        }

        public void SetOpen(bool open, Vector3 from)
        {
            if (!open)
            {
                _target = _closed;
                return;
            }
            // The door's front faces +Z of its closed pose: swing to the side away from the opener.
            var front = _closed * Vector3.forward;
            float side = Vector3.Dot(from - transform.position, front) >= 0f ? 1f : -1f;
            _target = _closed * Quaternion.Euler(0f, 95f * side, 0f);
        }

        private void Update()
        {
            if (transform.rotation == _target) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _target, 300f * Time.deltaTime);
        }
    }
}
