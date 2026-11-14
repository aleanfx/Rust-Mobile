using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Game.Controls;
using Oxfall.Game.Net;
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
    /// it would go, green when it can be placed, and places it when attack is pressed. Where pieces
    /// may go and what they cost is decided by the shared rules (<see cref="PlayerActions"/>), here
    /// alone or on the server online, through <see cref="GameActions"/>. <see cref="BuildingViews"/>
    /// draws what gets built.
    /// </summary>
    public sealed class BuildSystem : MonoBehaviour
    {
        private const float S = BuildingGrid.CellSize;

        /// <summary>How far ahead of the player the piece is aimed, in metres.</summary>
        public float Reach = 3.6f;

        private static readonly PieceType[] Order =
            { PieceType.Foundation, PieceType.Wall, PieceType.Doorway, PieceType.Roof };

        private BuildingGrid _grid;
        private BuildingViews _views;
        private PlayerInput _input;
        private Inventory _inventory;
        private IslandShape _shape;
        private PlayerController _player;
        private MaterialLibrary _materials;
        private Hud _hud;

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

        /// <summary>Where placing goes (the shared rules alone, the server online).</summary>
        public GameActions Actions { get; set; }

        /// <summary>The settlement the grid belongs to, for the floor-height rule shared with the server.</summary>
        public Settlement Settlement { get; set; }

        public bool Active { get; private set; }
        public PieceType Current => Order[_index];

        public void Init(BuildingViews views, PlayerInput input, Inventory inventory, IslandShape shape, PlayerController player,
            MaterialLibrary materials, Hud hud)
        {
            _views = views;
            _grid = views.Grid;
            _input = input;
            _inventory = inventory;
            _shape = shape;
            _player = player;
            _materials = materials;
            _hud = hud;

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
            if (Active) _hud.ShowToast("Apunta y toca COLOCAR. Luego mejóralo con el martillo");
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
                _hud.ShowToast($"Faltan materiales: cada pieza cuesta {BuildCosts.TwigWood} de Madera");
                return;
            }

            var op = Current == PieceType.Foundation ? BuildOp.Foundation(_cell, _top)
                : Current == PieceType.Roof ? BuildOp.Roof(_cell)
                : BuildOp.Edge(_edge, Current);
            var result = Actions.Build(op);
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }

            // A floor built under the player's feet lifts them onto it instead of trapping them.
            if (Current == PieceType.Foundation && BuildingGrid.CellAt(_player.transform.position.x,
                    _player.transform.position.z).Equals(_cell))
                _player.LiftTo(_top + 0.05f);

            Debug.Log($"[Oxfall] Built {Current} at {_ghostFilter.transform.position}");
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
                    var mid = BuildingViews.EdgeMid(_edge);
                    float baseHeight = _valid ? _grid.EdgeBaseHeight(_edge) : _shape.Height(mid.x, mid.z);
                    position = new Vector3(mid.x, baseHeight, mid.z);
                    rotation = BuildingViews.EdgeRotation(_edge);
                    break;
            }

            _ghostFilter.sharedMesh = _views.MeshFor(Current);
            _ghostFilter.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>The floor's height and whether the ground allows it: the same rule the server checks.</summary>
        private void AimFoundation()
        {
            var r = PlayerActions.FoundationTop(Settlement, _cell, out _top);
            if (r != ActionResult.Ok) Fail(GameActions.Explain(r));
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
                var mid = BuildingViews.EdgeMid(edge);
                float d = (mid.x - point.x) * (mid.x - point.x) + (mid.z - point.z) * (mid.z - point.z);
                if (d < bestDistance) { bestDistance = d; best = edge; }
            }
            return best;
        }

    }
}
