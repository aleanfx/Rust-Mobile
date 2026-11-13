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
        private readonly Mesh[] _solid = new Mesh[4];
        private readonly Mesh[] _twig = new Mesh[4];
        private readonly Bounds[][] _boxes = new Bounds[4][];
        private readonly Dictionary<PieceRef, GameObject> _pieces = new Dictionary<PieceRef, GameObject>();
        private readonly Dictionary<Edge, DoorView> _doors = new Dictionary<Edge, DoorView>();

        public BuildingGrid Grid => _grid;
        public Mesh DoorMesh { get; }

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
            _boxes[(int)PieceType.Roof] = new[] { Box(0f, 0.1f, 0f, S + 0.2f, 0.2f, S + 0.2f) };
            for (int i = 0; i < _solid.Length; i++)
                _solid[i] = MeshFactory.Boxes(((PieceType)i).ToString(), _boxes[i]);
            // Twig: a solid block of sticks for floors, see-through frames for the rest.
            _twig[(int)PieceType.Foundation] = _solid[(int)PieceType.Foundation];
            _twig[(int)PieceType.Wall] = MeshFactory.TwigWall(false);
            _twig[(int)PieceType.Doorway] = MeshFactory.TwigWall(true);
            _twig[(int)PieceType.Roof] = MeshFactory.TwigRoof();
            DoorMesh = MeshFactory.Door();

            grid.CellPieceAdded += (type, cell) => Spawn(type == PieceType.Roof ? PieceRef.Roof(cell) : PieceRef.Foundation(cell));
            grid.EdgePieceAdded += (type, edge) => Spawn(PieceRef.OnEdge(edge, type));
            grid.PieceRemoved += (piece, tier) => Despawn(piece);
            grid.PieceUpgraded += (piece, tier) => Skin(piece, tier);
            grid.DoorPlaced += SpawnDoor;
            grid.DoorRemoved += DespawnDoor;
            grid.DoorToggled += (edge, open) =>
            {
                if (_doors.TryGetValue(edge, out var door)) door.SetOpen(open, Viewer != null ? Viewer.position : door.transform.position);
            };
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

        /// <summary>Where a placed piece stands (its origin: the floor under it, the middle of its cell or edge).</summary>
        public Vector3 PositionOf(PieceRef piece)
        {
            switch (piece.Type)
            {
                case PieceType.Foundation:
                    _grid.TryGetFoundationTop(piece.Cell, out float top);
                    return new Vector3((piece.Cell.X + 0.5f) * S, top, (piece.Cell.Z + 0.5f) * S);
                case PieceType.Roof:
                    _grid.TryGetFoundationTop(piece.Cell, out float floor);
                    return new Vector3((piece.Cell.X + 0.5f) * S, floor + BuildingGrid.WallHeight, (piece.Cell.Z + 0.5f) * S);
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
            go.transform.SetPositionAndRotation(PositionOf(piece), piece.IsEdge ? EdgeRotation(piece.Edge) : Quaternion.identity);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
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

        /// <summary>Twig is sticks, wood is planks, stone is blocks. The colliders never change.</summary>
        private void Skin(PieceRef piece, BuildingTier tier)
        {
            if (!_pieces.TryGetValue(piece, out var go)) return;
            int i = (int)piece.Type;
            go.GetComponent<MeshFilter>().sharedMesh = tier == BuildingTier.Twig ? _twig[i] : _solid[i];
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = tier == BuildingTier.Twig ? _materials.Twig
                : tier == BuildingTier.Stone ? _materials.StoneWall : _materials.Planks;
            // Thin sticks cast thin, flickering shadows for little gain.
            renderer.shadowCastingMode = tier == BuildingTier.Twig && piece.Type != PieceType.Foundation
                ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }

        private void Despawn(PieceRef piece)
        {
            if (!_pieces.TryGetValue(piece, out var go)) return;
            _pieces.Remove(piece);
            Object.Destroy(go);
        }

        private void SpawnDoor(Edge edge)
        {
            if (_doors.ContainsKey(edge) || !_grid.TryGetEdgePiece(edge, out var doorway)) return;
            DoorPose(edge, out var hinge, out var rotation);
            var go = new GameObject("Door " + edge);
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(hinge, rotation);
            go.AddComponent<MeshFilter>().sharedMesh = DoorMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = _materials.Door;
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
