using System;
using Oxfall.Core.Building;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using Oxfall.Core.Items;
using Oxfall.Game.Controls;
using Oxfall.Game.Rendering;
using Oxfall.Game.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// A door in hand: the empty doorway closest to where the player looks shows a ghost door, and
    /// attack hangs the door there.
    /// </summary>
    public sealed class DoorPlacer : MonoBehaviour
    {
        public float Reach = 5f;
        /// <summary>How far off the line of sight a doorway may be and still be picked, in degrees.</summary>
        public float AimCone = 35f;

        private BuildingViews _views;
        private BuildingGrid _grid;
        private PlayerInput _input;
        private Inventory _inventory;
        private Transform _head;
        private Hud _hud;
        private MeshRenderer _ghost;
        private Edge _edge;
        private bool _valid;

        public bool Active { get; private set; }

        /// <summary>A door was hung.</summary>
        public event Action Placed;

        /// <summary>Where hanging a door goes (the shared rules alone, the server online).</summary>
        public GameActions Actions { get; set; }

        public void Init(BuildingViews views, PlayerInput input, Inventory inventory, Transform head, Hud hud, MaterialLibrary materials)
        {
            _views = views;
            _grid = views.Grid;
            _input = input;
            _inventory = inventory;
            _head = head;
            _hud = hud;

            var ghost = new GameObject("Door ghost");
            ghost.AddComponent<MeshFilter>().sharedMesh = views.DoorMesh;
            _ghost = ghost.AddComponent<MeshRenderer>();
            _ghost.sharedMaterial = materials.GhostOk;
            _ghost.shadowCastingMode = ShadowCastingMode.Off;
            _ghost.receiveShadows = false;
            ghost.SetActive(false);
        }

        public void SetActive(bool on)
        {
            if (on == Active) return;
            Active = on;
            if (!on)
            {
                _ghost.gameObject.SetActive(false);
                _hud.SetPlaceValid(false);
            }
            else _hud.ShowToast("Apunta a un marco de puerta y toca COLOCAR");
        }

        private void Update()
        {
            if (!Active) return;
            if (_input.AttackPressed) Place();
            Aim();
        }

        public void Place()
        {
            if (!Active) return;
            Aim();
            if (!_valid)
            {
                _hud.ShowToast("Apunta a un marco de puerta vacío (constrúyelo con el plano)");
                return;
            }
            var result = Actions.Build(BuildOp.Door(_edge));
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            _hud.ShowToast("Puerta colocada: tócala con ABRIR");
            Debug.Log($"[Oxfall] Door hung at {_edge}");
            Placed?.Invoke();
        }

        /// <summary>Picks the empty doorway within reach that is closest to the line of sight.</summary>
        private void Aim()
        {
            var eye = _head.position;
            var look = _head.forward;
            float best = Mathf.Cos(AimCone * Mathf.Deg2Rad);
            _valid = false;
            foreach (var pair in _grid.EdgePieces)
            {
                if (pair.Value != PieceType.Doorway || _grid.HasDoor(pair.Key)) continue;
                var centre = BuildingViews.EdgeMid(pair.Key);
                centre.y = _grid.EdgeBaseHeight(pair.Key) + 1.1f;
                var to = centre - eye;
                float distance = to.magnitude;
                if (distance > Reach || distance < 0.01f) continue;
                float dot = Vector3.Dot(to / distance, look);
                if (dot <= best) continue;
                best = dot;
                _edge = pair.Key;
                _valid = true;
            }

            _ghost.gameObject.SetActive(_valid);
            _hud.SetPlaceValid(_valid);
            if (!_valid) return;
            _views.DoorPose(_edge, out var hinge, out var rotation);
            _ghost.transform.SetPositionAndRotation(hinge, rotation);
        }
    }
}
