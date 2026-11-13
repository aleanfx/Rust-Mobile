using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Game.Controls;
using Oxfall.Game.Player;
using Oxfall.Game.Rendering;
using Oxfall.Game.UI;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// Building mode, while the building plan is in hand: shows a ghost of the chosen piece where
    /// it would go, green when it can be placed, and places it when attack is pressed. Where pieces may go and what they cost is decided by the Core's
    /// <see cref="BuildingGrid"/> and <see cref="BuildCosts"/>; this adds the terrain checks and the
    /// visuals.
    /// </summary>
    public sealed class BuildSystem : MonoBehaviour
    {
        private const float S = BuildingGrid.CellSize;

        /// <summary>How far ahead of the player the piece is aimed, in metres.</summary>
        public float Reach = 3.6f;

        private static readonly PieceType[] Order =
            { PieceType.Foundation, PieceType.Wall, PieceType.Doorway, PieceType.Roof };

        private BuildingGrid _grid;
        private PlayerInput _input;
        private Inventory _inventory;
        private IslandShape _shape;
        private PlayerController _player;
        private MaterialLibrary _materials;
        private Hud _hud;
        private Transform _pieces;

        private readonly Mesh[] _meshes = new Mesh[4];
        private readonly Bounds[][] _boxes = new Bounds[4][];
        private MeshFilter _ghostFilter;
        private MeshRenderer _ghostRenderer;

        private int _index;
        private bool _valid;
        private string _whyNot = "";
        private Cell _cell;
        private Edge _edge;
        private float _top;

        /// <summary>Raised after a piece is built.</summary>
        public event System.Action Placed;

        public bool Active { get; private set; }
        public PieceType Current => Order[_index];

        public void Init(BuildingGrid grid, PlayerInput input, Inventory inventory, IslandShape shape, PlayerController player,
            MaterialLibrary materials, Hud hud)
        {
            _grid = grid;
            _input = input;
            _inventory = inventory;
            _shape = shape;
            _player = player;
            _materials = materials;
            _hud = hud;
            _pieces = new GameObject("Buildings").transform;

            // Each piece is a few boxes: the same list makes its mesh and its colliders.
            _boxes[(int)PieceType.Foundation] = new[] { Box(0f, -1.25f, 0f, S, 2.5f, S) };
            _boxes[(int)PieceType.Wall] = new[] { Box(0f, 1.5f, 0f, S, BuildingGrid.WallHeight, 0.2f) };
            _boxes[(int)PieceType.Doorway] = new[]
            {
                Box(-1.05f, 1.5f, 0f, 0.9f, BuildingGrid.WallHeight, 0.2f),
                Box(1.05f, 1.5f, 0f, 0.9f, BuildingGrid.WallHeight, 0.2f),
                Box(0f, 2.6f, 0f, 1.2f, 0.8f, 0.2f),
            };
            _boxes[(int)PieceType.Roof] = new[] { Box(0f, 0.1f, 0f, S + 0.2f, 0.2f, S + 0.2f) };
            for (int i = 0; i < _meshes.Length; i++)
                _meshes[i] = MeshFactory.Boxes(((PieceType)i).ToString(), _boxes[i]);

            var ghost = new GameObject("Build ghost");
            _ghostFilter = ghost.AddComponent<MeshFilter>();
            _ghostRenderer = ghost.AddComponent<MeshRenderer>();
            _ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _ghostRenderer.receiveShadows = false;
            ghost.SetActive(false);
        }

        public static string Name(PieceType type)
        {
            switch (type)
            {
                case PieceType.Foundation: return "Cimiento";
                case PieceType.Wall: return "Pared";
                case PieceType.Doorway: return "Marco de puerta";
                default: return "Techo";
            }
        }

        /// <summary>Enters or leaves building mode: on while the building plan is in hand.</summary>
        public void SetActive(bool on)
        {
            if (on == Active) return;
            Active = on;
            _ghostFilter.gameObject.SetActive(Active);
            _hud.SetBuildMode(Active, Name(Current));
            if (Active) _hud.ShowToast("Modo construcción: apunta y toca COLOCAR");
        }

        public void CyclePiece()
        {
            _index = (_index + 1) % Order.Length;
            _hud.SetBuildMode(Active, Name(Current));
        }

        public void Place()
        {
            if (!Active) return;
            Aim();
            if (!_valid)
            {
                _hud.ShowToast(_whyNot);
                return;
            }
            if (!BuildCosts.CanAfford(_inventory, Current))
            {
                _hud.ShowToast("Faltan materiales: cada pieza cuesta 50 de Madera");
                return;
            }

            PlaceResult result;
            switch (Current)
            {
                case PieceType.Foundation: result = _grid.PlaceFoundation(_cell, _top); break;
                case PieceType.Roof: result = _grid.PlaceRoof(_cell); break;
                default: result = _grid.PlaceEdge(_edge, Current); break;
            }
            if (result != PlaceResult.Ok)
            {
                _hud.ShowToast(Explain(result));
                return;
            }
            BuildCosts.TryPay(_inventory, Current);

            var piece = new GameObject(Current.ToString());
            piece.transform.SetParent(_pieces, false);
            piece.transform.SetPositionAndRotation(_ghostFilter.transform.position, _ghostFilter.transform.rotation);
            piece.AddComponent<MeshFilter>().sharedMesh = _meshes[(int)Current];
            piece.AddComponent<MeshRenderer>().sharedMaterial = _materials.Planks;
            foreach (var box in _boxes[(int)Current])
            {
                var collider = piece.AddComponent<BoxCollider>();
                collider.center = box.center;
                collider.size = box.size;
            }

            // A floor built under the player's feet lifts them onto it instead of trapping them.
            if (Current == PieceType.Foundation && BuildingGrid.CellAt(_player.transform.position.x,
                    _player.transform.position.z).Equals(_cell))
                _player.LiftTo(_top + 0.05f);

            Debug.Log($"[Oxfall] Built {Current} at {piece.transform.position}");
            Placed?.Invoke();
        }

        private void Update()
        {
            if (!Active) return;
            if (_input.AttackPressed)
            {
                Place();
                if (!Active) return;
            }
            Aim();
            _ghostRenderer.sharedMaterial = _valid ? _materials.GhostOk : _materials.GhostBad;
            _hud.SetPlaceValid(_valid);
        }

        /// <summary>Works out which cell or edge is aimed at, whether the piece fits, and moves the ghost there.</summary>
        private void Aim()
        {
            var head = _player.Head;
            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = _player.transform.forward;
            var point = _player.transform.position + forward.normalized * Reach;

            _cell = BuildingGrid.CellAt(point.x, point.z);
            _valid = true;
            _whyNot = "";
            var position = Vector3.zero;
            var rotation = Quaternion.identity;

            switch (Current)
            {
                case PieceType.Foundation:
                    AimFoundation();
                    position = new Vector3((_cell.X + 0.5f) * S, _top, (_cell.Z + 0.5f) * S);
                    break;

                case PieceType.Roof:
                    Check(_grid.CanPlaceRoof(_cell));
                    _grid.TryGetFoundationTop(_cell, out float floor);
                    if (!_grid.HasFoundation(_cell)) floor = _shape.Height(point.x, point.z);
                    position = new Vector3((_cell.X + 0.5f) * S, floor + BuildingGrid.WallHeight, (_cell.Z + 0.5f) * S);
                    break;

                default:
                    _edge = NearestEdge(_cell, point);
                    Check(_grid.CanPlaceEdge(_edge));
                    var mid = EdgeMid(_edge);
                    float baseHeight = _valid ? _grid.EdgeBaseHeight(_edge) : _shape.Height(mid.x, mid.z);
                    position = new Vector3(mid.x, baseHeight, mid.z);
                    if (_edge.Axis == EdgeAxis.AlongZ) rotation = Quaternion.Euler(0f, 90f, 0f);
                    break;
            }

            _ghostFilter.sharedMesh = _meshes[(int)Current];
            _ghostFilter.transform.SetPositionAndRotation(position, rotation);
        }

        private void AimFoundation()
        {
            float x0 = _cell.X * S, z0 = _cell.Z * S;
            float a = _shape.Height(x0, z0), b = _shape.Height(x0 + S, z0);
            float c = _shape.Height(x0, z0 + S), d = _shape.Height(x0 + S, z0 + S);
            float e = _shape.Height(x0 + S * 0.5f, z0 + S * 0.5f);
            float highest = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, Mathf.Max(d, e)));
            float lowest = Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, Mathf.Min(d, e)));

            // Level with a neighbouring floor if there is one, so rooms come out flat.
            _top = highest + 0.25f;
            if (_grid.TryGetFoundationTop(new Cell(_cell.X + 1, _cell.Z), out float t)
                || _grid.TryGetFoundationTop(new Cell(_cell.X - 1, _cell.Z), out t)
                || _grid.TryGetFoundationTop(new Cell(_cell.X, _cell.Z + 1), out t)
                || _grid.TryGetFoundationTop(new Cell(_cell.X, _cell.Z - 1), out t))
                _top = t;

            Check(_grid.CanPlaceFoundation(_cell));
            if (!_valid) return;
            if (lowest < 0.4f) Fail("No se puede construir en el agua");
            else if (_top - lowest > 2.4f) Fail("El terreno es demasiado inclinado");
            else if (_top < highest - 0.4f) Fail("El terreno tapa el cimiento");
        }

        private void Check(PlaceResult result)
        {
            if (result != PlaceResult.Ok) Fail(Explain(result));
        }

        private void Fail(string why)
        {
            _valid = false;
            _whyNot = why;
        }

        private static string Explain(PlaceResult result)
        {
            switch (result)
            {
                case PlaceResult.Occupied: return "Ya hay una pieza ahí";
                case PlaceResult.NeedsFoundation: return "Primero pon un cimiento";
                case PlaceResult.NeedsWall: return "El techo necesita al menos una pared";
                case PlaceResult.NotEnoughResources: return "Faltan materiales";
                default: return "No se puede construir ahí";
            }
        }

        private static Edge NearestEdge(Cell cell, Vector3 point)
        {
            Edge best = cell.South;
            float bestDistance = float.MaxValue;
            foreach (var edge in cell.Edges)
            {
                var mid = EdgeMid(edge);
                float d = (mid.x - point.x) * (mid.x - point.x) + (mid.z - point.z) * (mid.z - point.z);
                if (d < bestDistance) { bestDistance = d; best = edge; }
            }
            return best;
        }

        private static Vector3 EdgeMid(Edge edge) => edge.Axis == EdgeAxis.AlongX
            ? new Vector3((edge.X + 0.5f) * S, 0f, edge.Z * S)
            : new Vector3(edge.X * S, 0f, (edge.Z + 0.5f) * S);

        private static Bounds Box(float cx, float cy, float cz, float sx, float sy, float sz) =>
            new Bounds(new Vector3(cx, cy, cz), new Vector3(sx, sy, sz));
    }
}
