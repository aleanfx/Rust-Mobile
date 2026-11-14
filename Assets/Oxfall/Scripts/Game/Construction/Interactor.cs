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
    /// tool cupboard: ARMARIO (who may build here).
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
        private float _timer;

        /// <summary>A door opened or closed.</summary>
        public event Action DoorUsed;

        /// <summary>The player opens a box or furnace (its id).</summary>
        public event Action<int> OpenBox;

        /// <summary>The player opens a tool cupboard (its id).</summary>
        public event Action<int> OpenCupboard;

        /// <summary>Where opening goes (the shared rules alone, the server online).</summary>
        public GameActions Actions { get; set; }

        public bool LookingAtDoor => _target == Target.Door;

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
        }

        private void Update()
        {
            if (_grid == null) return;
            // Ten looks a second are plenty for a button.
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.1f;
            Look();
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
            var ray = new Ray(_head.position, _head.forward);
            if (!Physics.SphereCast(ray, 0.15f, out var hit, Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            var piece = hit.collider.GetComponent<PieceTag>();
            if (piece != null && piece.Piece.IsEdge && _grid.HasDoor(piece.Piece.Edge))
            {
                _target = Target.Door;
                _door = piece.Piece.Edge;
                return;
            }
            var tag = hit.collider.GetComponent<DeployableTag>();
            if (tag == null || !_settlement.TryGet(tag.Id, out _)) return;
            _deployable = tag.Id;
            if (tag.Kind == DeployableKind.ToolCupboard) _target = Target.Cupboard;
            else if (_settlement.Container(tag.Id) != null) _target = Target.Box;
        }

        /// <summary>Uses what is in sight, if anything.</summary>
        public void Use()
        {
            Look();
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
