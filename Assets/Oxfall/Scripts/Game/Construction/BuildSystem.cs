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
            { PieceType.Foundation, PieceType.Wall, PieceType.Doorway, PieceType.Window, PieceType.Roof, PieceType.Stairs };

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
        private int _facing;

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
                case PieceType.Window: return "Ventana";
                case PieceType.Stairs: return "Escalera";
                default: return "Techo / piso";
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
                _hud.ShowToast($"Faltan materiales: {Name(Current)} cuesta {BuildCosts.For(Current)[0].Amount} de Madera");
                return;
            }

            var op = Current == PieceType.Foundation ? BuildOp.Foundation(_cell, _top)
                : Current == PieceType.Roof ? BuildOp.Roof(_cell)
                : Current == PieceType.Stairs ? BuildOp.Stairs(_cell, _facing)
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

            Debug.Log($"[Oxfall] Built {Current} at {_ghostFilter.transform.position} ({op})");
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

        /// <summary>
        /// The storey aimed at above a square: the one the player stands on when looking ahead, the
        /// one above when looking well up, the one below when looking well down.
        /// </summary>
        public static int AimLevel(BuildingGrid grid, Cell column, float feet, float lookUp)
        {
            float y = feet + 1.0f + (lookUp > 0f ? lookUp * 4f : lookUp * 2.4f);
            return grid.LevelAt(column, y);
        }

        /// <summary>Which side of a square faces whoever looks along <paramref name="forward"/> (0 north, 1 east, 2 south, 3 west): stairs open towards the builder.</summary>
        public static int FacingTowards(Vector3 forward)
        {
            if (Mathf.Abs(forward.x) > Mathf.Abs(forward.z)) return forward.x > 0f ? 3 : 1;
            return forward.z > 0f ? 2 : 0;
        }

        /// <summary>Works out which cell or edge (and storey) is aimed at, whether the piece fits, and moves the ghost there.</summary>
        private void Aim()
        {
            var head = _player.Head;
            float lookUp = head.forward.y;
            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = _player.transform.forward;
            forward.Normalize();
            var feet = _player.transform.position;
            var point = feet + forward * Reach;

            var column = BuildingGrid.CellAt(point.x, point.z);
            var standing = BuildingGrid.CellAt(feet.x, feet.z);
            // Your own storey: by the square you stand on if it is built, else by the one aimed at.
            int own = _grid.HasFoundation(standing) ? _grid.LevelAt(standing, feet.y) : _grid.LevelAt(column, feet.y);
            int aimed = AimLevel(_grid, column, feet.y, lookUp);
            // A roof is the ceiling over you, or the floor ahead when looking down; stairs start on your floor.
            int level = Current == PieceType.Roof || Current == PieceType.Stairs ? Mathf.Min(aimed, own) : aimed;
            _cell = new Cell(column.X, column.Z, Current == PieceType.Foundation ? 0 : level);
            _valid = true;
            _whyNot = "";
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            var scale = Vector3.one;

            switch (Current)
            {
                case PieceType.Foundation:
                    AimFoundation();
                    position = new Vector3((_cell.X + 0.5f) * S, _top, (_cell.Z + 0.5f) * S);
                    break;

                case PieceType.Roof:
                {
                    Check(_grid.CanPlaceRoof(_cell));
                    if (!_grid.TryGetStoreyBase(_cell, out float storey)) storey = _shape.Height(point.x, point.z);
                    position = new Vector3((_cell.X + 0.5f) * S, storey + BuildingGrid.WallHeight, (_cell.Z + 0.5f) * S);
                    break;
                }

                case PieceType.Stairs:
                {
                    Check(_grid.CanPlaceStairs(_cell));
                    if (!_grid.TryGetFloorHeight(_cell, out float floor)) floor = _shape.Height(point.x, point.z);
                    _facing = FacingTowards(forward);
                    position = new Vector3((_cell.X + 0.5f) * S, floor, (_cell.Z + 0.5f) * S);
                    rotation = BuildingViews.StairsRotation(_facing);
                    scale = new Vector3(1f, BuildingViews.StairsSquash(_cell.Level), 1f);
                    break;
                }

                default:
                    _edge = NearestEdge(_cell, point);
                    Check(_grid.CanPlaceEdge(_edge));
                    var mid = BuildingViews.EdgeMid(_edge);
                    float baseHeight = _valid || _grid.HasFoundation(_edge.CellA) || _grid.HasFoundation(_edge.CellB)
                        ? _grid.EdgeBaseHeight(_edge) : _shape.Height(mid.x, mid.z);
                    position = new Vector3(mid.x, baseHeight, mid.z);
                    rotation = BuildingViews.EdgeRotation(_edge);
                    break;
            }

            _ghostFilter.sharedMesh = _views.MeshFor(Current);
            _ghostFilter.transform.SetPositionAndRotation(position, rotation);
            _ghostFilter.transform.localScale = scale;
            // The piece button says which storey is aimed at (only re-written when that changes).
            int shown = Current == PieceType.Foundation ? 0 : _cell.Level;
            if (shown != _shownLevel || Current != _shownPiece)
            {
                _shownLevel = shown;
                _shownPiece = Current;
                _hud.SetBuildMode(Active, shown > 0 ? $"{Name(Current)} · piso {shown + 1}" : Name(Current));
            }
        }

        private int _shownLevel = -1;
        private PieceType _shownPiece;

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
                case PlaceResult.NeedsFloor: return "Necesita piso debajo: pon un techo en el piso de abajo";
                case PlaceResult.TooHigh: return $"Máximo {BuildingGrid.MaxLevel + 1} pisos";
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
