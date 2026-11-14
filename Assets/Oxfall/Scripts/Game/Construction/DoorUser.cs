using Oxfall.Core.Building;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using Oxfall.Game.UI;
using UnityEngine;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// Whatever is in hand: looking at a door (or the doorway it hangs in) up close shows the use
    /// button, ABRIR or CERRAR, and tapping it swings the door.
    /// </summary>
    public sealed class DoorUser : MonoBehaviour
    {
        public float Reach = 3.2f;

        private BuildingGrid _grid;
        private Transform _head;
        private Hud _hud;
        private bool _hasDoor;
        private Edge _door;
        private float _timer;

        /// <summary>A door opened or closed.</summary>
        public event System.Action Used;

        /// <summary>Where opening goes (the shared rules alone, the server online).</summary>
        public GameActions Actions { get; set; }

        public void Init(BuildingGrid grid, Transform head, Hud hud)
        {
            _grid = grid;
            _head = head;
            _hud = hud;
            _grid.DoorToggled += (edge, open) => _timer = 0f;
            _grid.DoorRemoved += edge => _timer = 0f;
        }

        private void Update()
        {
            if (_grid == null) return;
            // Ten looks a second are plenty for a button.
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.1f;

            _hasDoor = false;
            var ray = new Ray(_head.position, _head.forward);
            if (Physics.SphereCast(ray, 0.15f, out var hit, Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var tag = hit.collider.GetComponent<PieceTag>();
                if (tag != null && tag.Piece.IsEdge && _grid.HasDoor(tag.Piece.Edge))
                {
                    _hasDoor = true;
                    _door = tag.Piece.Edge;
                }
            }
            _hud.SetUseButton(_hasDoor ? (_grid.IsDoorOpen(_door) ? "CERRAR" : "ABRIR") : null);
        }

        /// <summary>Opens or closes the door in sight, if there is one.</summary>
        public void Use()
        {
            if (!_hasDoor || Actions.Build(BuildOp.ToggleDoor(_door, !_grid.IsDoorOpen(_door))) != ActionResult.Ok) return;
            Debug.Log($"[Oxfall] Door at {_door} {(_grid.IsDoorOpen(_door) ? "opened" : "toggled")}");
            Used?.Invoke();
        }

        public bool LookingAtDoor => _hasDoor;
    }
}
