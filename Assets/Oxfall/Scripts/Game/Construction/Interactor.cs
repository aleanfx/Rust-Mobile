using System;
using Oxfall.Core.Building;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using Oxfall.Game.UI;
using UnityEngine;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// Whatever is in hand: looking up close at something that can be used shows the use button.
    /// A door: ABRIR or CERRAR. A box or a furnace: ABRIR (its panel opens beside the inventory). A
    /// tool cupboard: ARMARIO (who may build here). Behind a code lock the player does not know, the
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
        }

        private BuildingGrid _grid;
        private Settlement _settlement;
        private Transform _head;
        private Hud _hud;
        private Target _target;
        private Edge _door;
        private int _deployable;
        private bool _hasLock;
        private LockTarget _lock;
        private float _timer;

        /// <summary>A door opened or closed.</summary>
        public event Action DoorUsed;

        /// <summary>The player opens a box or furnace (its id).</summary>
        public event Action<int> OpenBox;

        /// <summary>The player opens a tool cupboard (its id).</summary>
        public event Action<int> OpenCupboard;

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
                    bool furnace = _settlement.TryGet(_deployable, out var d) && d.Kind == DeployableKind.Furnace;
                    _hud.SetUseButton(furnace ? "HORNO" : "ABRIR", furnace ? Icons.Flame : Icons.Box);
                    break;
                case Target.Cupboard:
                    _hud.SetUseButton("ARMARIO", Icons.Shield);
                    break;
                default:
                    _hud.SetUseButton(null);
                    break;
            }
        }

        private void Look()
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
            var tag = hit.collider.GetComponent<DeployableTag>();
            if (tag == null || !_settlement.TryGet(tag.Id, out _)) return;
            _deployable = tag.Id;
            if (tag.Kind == DeployableKind.ToolCupboard) _target = Target.Cupboard;
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
            }
        }
    }
}
