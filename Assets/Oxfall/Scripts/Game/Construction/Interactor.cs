using System;
using Oxfall.Core.Building;
using Oxfall.Core.Gathering;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using Oxfall.Game.UI;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// Whatever is in hand: looking up close at something that can be used shows the use button.
    /// A door: ABRIR or CERRAR. A box or a furnace: ABRIR (its panel opens beside the inventory). A
    /// tool cupboard: ARMARIO (who may build here). A supply crate in the ruins: ABRIR (what it held
    /// goes into the inventory). A workbench: MESA (the crafting panel). Behind a code lock the player does not know, the
    /// button says CÓDIGO and opens its keypad instead; on a lock the player knows, a second, smaller
    /// button (CANDADO) opens it to lock, unlock, change the code or take it off.
    /// </summary>
    public sealed class Interactor : MonoBehaviour
    {
        public float Reach = 3.2f;

        private enum Target
        {
            None,
            Door,
            Box,
            Cupboard,
            Crate,
            Workbench,
            Player,
            Boat,
            Crop,
        }

        private BuildingGrid _grid;
        private Settlement _settlement;
        private Transform _head;
        private Hud _hud;
        private Target _target;
        private Edge _door;
        private int _deployable;
        private ResourceNodeView _crate;
        private ushort _player;
        private int _boat;

        /// <summary>The empty boat in reach the player looks towards (0: none).</summary>
        public Func<int> BoatInSight { get; set; }

        /// <summary>Whether the player sits in a boat (the use button gets them out).</summary>
        public Func<bool> Rowing { get; set; }

        /// <summary>The player gets into a boat (its id).</summary>
        public event Action<int> Board;

        /// <summary>The player gets out of their boat.</summary>
        public event Action LeaveBoat;

        /// <summary>Whether this player could be asked into a team (online, not already a teammate, awake).</summary>
        public Func<ushort, bool> CanInvite { get; set; }

        /// <summary>The player asked another (by player id) into their team.</summary>
        public event Action<ushort> Invite;
        private bool _hasLock;
        private LockTarget _lock;
        private float _timer;

        /// <summary>A door opened or closed.</summary>
        public event Action DoorUsed;

        /// <summary>The player opens a box or furnace (its id).</summary>
        public event Action<int> OpenBox;

        /// <summary>The player opens a tool cupboard (its id).</summary>
        public event Action<int> OpenCupboard;

        /// <summary>The player opens a supply crate.</summary>
        public event Action<ResourceNodeView> OpenCrate;

        /// <summary>The player harvested a crop (what it was, how far along).</summary>
        public event Action<DeployableKind, CropStage> Harvested;

        /// <summary>The player taps MESA at a workbench.</summary>
        public event Action UseWorkbench;

        /// <summary>The player taps CÓDIGO on a lock they do not know.</summary>
        public event Action<LockTarget> EnterCode;

        /// <summary>The player taps CANDADO on a lock they know (or just put on).</summary>
        public event Action<LockTarget> ManageLock;

        /// <summary>Where opening goes (the shared rules alone, the server online).</summary>
        public GameActions Actions { get; set; }

        public bool LookingAtDoor => _target == Target.Door;

        /// <summary>The lock on what is in sight, if there is one.</summary>
        public bool LookingAtLock(out LockTarget target)
        {
            target = _lock;
            return _target != Target.None && _hasLock;
        }

        private int Me => Actions != null ? Actions.Owner : 0;

        /// <summary>True while a panel is open over the world: the buttons stay out of its way.</summary>
        public Func<bool> Busy;

        /// <summary>A lock in sight that keeps this player out.</summary>
        private bool Barred => _hasLock && !_settlement.CanPass(_lock, Me);

        /// <summary>The box, furnace or cupboard in sight (0: none).</summary>
        public int LookingAt => _target == Target.Box || _target == Target.Cupboard ? _deployable : 0;

        public void Init(Settlement settlement, Transform head, Hud hud)
        {
            _settlement = settlement;
            _grid = settlement.Grid;
            _head = head;
            _hud = hud;
            _grid.DoorToggled += (edge, open) => _timer = 0f;
            _grid.DoorRemoved += edge => _timer = 0f;
            settlement.DeployableRemoved += id => _timer = 0f;
            settlement.LockChanged += target => _timer = 0f;
        }

        private void Update()
        {
            if (_grid == null) return;
            // Ten looks a second are plenty for a button.
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.1f;
            if (Busy != null && Busy())
            {
                _hud.SetLockButton(false, false);
                _hud.SetUseButton(null);
                return;
            }
            if (Rowing != null && Rowing())
            {
                _hud.SetLockButton(false, false);
                _hud.SetUseButton("BAJAR", Icons.Boat);
                return;
            }
            Look();
            CodeLockState state = null;
            bool manage = _target != Target.None && _hasLock && _settlement.TryGetLock(_lock, out state) && state.CanManage(Me);
            _hud.SetLockButton(manage, manage && state.Locked);
            if (Barred)
            {
                _hud.SetUseButton("CÓDIGO", Icons.Keypad);
                return;
            }
            switch (_target)
            {
                case Target.Door:
                    _hud.SetUseButton(_grid.IsDoorOpen(_door) ? "CERRAR" : "ABRIR");
                    break;
                case Target.Box:
                    _settlement.TryGet(_deployable, out var d);
                    if (d.Kind == DeployableKind.Bag) _hud.SetUseButton("BOLSA", Icons.Bag);
                    else if (d.Kind == DeployableKind.SupplyDrop) _hud.SetUseButton("SUMINISTROS", Icons.Box);
                    else if (d.Kind == DeployableKind.Furnace) _hud.SetUseButton("HORNO", Icons.Flame);
                    else if (d.Kind == DeployableKind.Campfire) _hud.SetUseButton("FOGATA", Icons.Flame);
                    else if (d.Kind == DeployableKind.Recycler) _hud.SetUseButton("RECICLAR", Icons.Recycle);
                    else if (d.Kind == DeployableKind.ShotgunTrap) _hud.SetUseButton("TRAMPA", Icons.Box);
                    else _hud.SetUseButton("ABRIR", Icons.Box);
                    break;
                case Target.Cupboard:
                    _hud.SetUseButton("ARMARIO", Icons.Shield);
                    break;
                case Target.Crate:
                    _hud.SetUseButton("ABRIR", Icons.Box);
                    break;
                case Target.Workbench:
                    _hud.SetUseButton("MESA", Icons.Hammer);
                    break;
                case Target.Crop:
                {
                    var stage = _settlement.CropStageOf(_deployable);
                    if (Crops.Harvestable(stage)) _hud.SetUseButton(stage == CropStage.Ripe ? "COSECHAR" : "ARRANCAR", Icons.Hand);
                    else
                    {
                        _settlement.TryGet(_deployable, out var crop);
                        int percent = Mathf.FloorToInt(Crops.Growth01(crop.Kind, _settlement.CropAge(_deployable)) * 100f);
                        _hud.SetUseButton($"CRECE {percent}%", Icons.Hand);
                    }
                    break;
                }
                case Target.Player:
                    _hud.SetUseButton("INVITAR", Icons.Team);
                    break;
                case Target.Boat:
                    _hud.SetUseButton(_boat > 2000 ? "MONTAR" : "SUBIR", _boat > 2000 ? Icons.Hand : _boat > 1000 ? Icons.Copter : Icons.Boat);
                    break;
                default:
                    _hud.SetUseButton(null);
                    break;
            }
        }

        private void Look()
        {
            LookAtThings();
            if (_target != Target.None) return;
            // Boats float on the water, with nothing to hit: one close by and looked towards will do.
            _boat = BoatInSight != null ? BoatInSight() : 0;
            if (_boat != 0) _target = Target.Boat;
        }

        private void LookAtThings()
        {
            _target = Target.None;
            _hasLock = false;
            var ray = new Ray(_head.position, _head.forward);
            if (!Physics.SphereCast(ray, 0.15f, out var hit, Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            var piece = hit.collider.GetComponent<PieceTag>();
            if (piece != null && piece.Piece.IsEdge && _grid.HasDoor(piece.Piece.Edge))
            {
                _target = Target.Door;
                _door = piece.Piece.Edge;
                _lock = LockTarget.Door(_door);
                _hasLock = _settlement.HasLock(_lock);
                return;
            }
            var node = hit.collider.GetComponentInParent<ResourceNodeView>();
            if (node != null && node.Node.Type == ResourceNodeType.Crate && !node.Node.IsDepleted)
            {
                _target = Target.Crate;
                _crate = node;
                return;
            }
            var other = hit.collider.GetComponent<Net.PlayerTag>();
            if (other != null)
            {
                if (CanInvite != null && CanInvite(other.Id))
                {
                    _target = Target.Player;
                    _player = other.Id;
                }
                return;
            }
            var tag = hit.collider.GetComponent<DeployableTag>();
            if (tag == null || !_settlement.TryGet(tag.Id, out _)) return;
            _deployable = tag.Id;
            if (Crops.IsCrop(tag.Kind))
            {
                _target = Target.Crop;
                return;
            }
            if (tag.Kind == DeployableKind.ToolCupboard) _target = Target.Cupboard;
            else if (tag.Kind == DeployableKind.Workbench)
            {
                _target = Target.Workbench;
                return;
            }
            else if (_settlement.Container(tag.Id) != null) _target = Target.Box;
            _lock = LockTarget.On(tag.Id);
            _hasLock = _target != Target.None && _settlement.HasLock(_lock);
        }

        /// <summary>The lock button: the keypad of the lock in sight, to manage it.</summary>
        public void UseLock()
        {
            Look();
            if (_target == Target.None || !_hasLock) return;
            ManageLock?.Invoke(_lock);
        }

        /// <summary>Uses what is in sight, if anything.</summary>
        public void Use()
        {
            if (Rowing != null && Rowing())
            {
                LeaveBoat?.Invoke();
                _timer = 0f;
                return;
            }
            Look();
            if (Barred)
            {
                EnterCode?.Invoke(_lock);
                return;
            }
            switch (_target)
            {
                case Target.Door:
                    if (Actions.Build(BuildOp.ToggleDoor(_door, !_grid.IsDoorOpen(_door))) != ActionResult.Ok) return;
                    Debug.Log($"[Oxfall] Door at {_door} used");
                    DoorUsed?.Invoke();
                    break;
                case Target.Box:
                    OpenBox?.Invoke(_deployable);
                    break;
                case Target.Cupboard:
                    OpenCupboard?.Invoke(_deployable);
                    break;
                case Target.Crate:
                    OpenCrate?.Invoke(_crate);
                    _timer = 0f;
                    break;
                case Target.Workbench:
                    UseWorkbench?.Invoke();
                    break;
                case Target.Crop:
                {
                    var stage = _settlement.CropStageOf(_deployable);
                    if (!Crops.Harvestable(stage))
                    {
                        _hud.ShowToast($"{Crops.StageName(stage)}: aún no se puede cosechar. La lluvia la hace crecer más rápido");
                        break;
                    }
                    _settlement.TryGet(_deployable, out var crop);
                    var result = Actions.Build(BuildOp.Harvest(_deployable));
                    if (result != ActionResult.Ok)
                    {
                        _hud.ShowToast(GameActions.Explain(result));
                        break;
                    }
                    Debug.Log($"[Oxfall] Harvested {crop.Kind} {_deployable} ({stage})");
                    Harvested?.Invoke(crop.Kind, stage);
                    _timer = 0f;
                    break;
                }
                case Target.Player:
                    Invite?.Invoke(_player);
                    _timer = 0f;
                    break;
                case Target.Boat:
                    Board?.Invoke(_boat);
                    _timer = 0f;
                    break;
            }
        }
    }
}
