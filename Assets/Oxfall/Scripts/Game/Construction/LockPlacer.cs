using System;
using Oxfall.Core.Rules;
using Oxfall.Game.Controls;
using Oxfall.Game.Net;
using Oxfall.Game.Rendering;
using Oxfall.Game.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// A code lock in hand: looking at a door, a box or a tool cupboard shows a ghost lock where it
    /// would go (green where it can, red where it cannot), and attack puts it on. Its keypad then
    /// opens to choose the code.
    /// </summary>
    public sealed class LockPlacer : MonoBehaviour
    {
        public float Reach = 3.4f;

        private Settlement _settlement;
        private LockViews _views;
        private PlayerInput _input;
        private Transform _head;
        private Hud _hud;
        private MeshRenderer _ghost;
        private Material[] _ghostOk;
        private Material[] _ghostBad;
        private LockTarget _target;
        private bool _aimed;
        private bool _valid;

        public bool Active { get; private set; }

        /// <summary>A lock was put on (alone it is there already; online, once the server says so).</summary>
        public event Action<LockTarget> Placed;

        public GameActions Actions { get; set; }

        public void Init(Settlement settlement, LockViews views, PlayerInput input, Transform head, Hud hud, MaterialLibrary materials)
        {
            _settlement = settlement;
            _views = views;
            _input = input;
            _head = head;
            _hud = hud;
            var ghost = new GameObject("Lock ghost");
            ghost.AddComponent<MeshFilter>().sharedMesh = views.Mesh;
            _ghost = ghost.AddComponent<MeshRenderer>();
            _ghostOk = new[] { materials.GhostOk, materials.GhostOk, materials.GhostOk };
            _ghostBad = new[] { materials.GhostBad, materials.GhostBad, materials.GhostBad };
            _ghost.sharedMaterials = _ghostOk;
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
            else _hud.ShowToast("Apunta a una puerta, una caja o un armario y toca COLOCAR");
        }

        private void Update()
        {
            if (!Active) return;
            if (_input.AttackPressed) Place();
            if (!Active) return;
            Aim();
        }

        public void Place()
        {
            if (!Active) return;
            Aim();
            if (!_aimed)
            {
                _hud.ShowToast("Apunta de cerca a una puerta, una caja o un armario");
                return;
            }
            if (_settlement.HasLock(_target))
            {
                _hud.ShowToast("Ya tiene candado");
                return;
            }
            var result = Actions.Build(BuildOp.PlaceLock(_target));
            if (result != ActionResult.Ok)
            {
                _hud.ShowToast(GameActions.Explain(result));
                return;
            }
            Debug.Log("[Oxfall] Lock put on " + _target);
            Placed?.Invoke(_target);
        }

        /// <summary>What is in sight up close, and whether a lock can go on it.</summary>
        private void Aim()
        {
            _aimed = false;
            var ray = new Ray(_head.position, _head.forward);
            if (Physics.SphereCast(ray, 0.15f, out var hit, Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                _aimed = LockViews.TryTargetOf(hit.collider, _settlement, out _target);
            if (!_aimed || !_views.TryPose(_target, out var position, out var rotation))
            {
                _aimed = false;
                _ghost.gameObject.SetActive(false);
                _hud.SetPlaceValid(false);
                return;
            }
            // A door shows the face toward the player.
            if (_target.IsDoor && Vector3.Dot(_head.position - position, rotation * Vector3.forward) < 0f)
            {
                position -= rotation * new Vector3(0f, 0f, 0.072f);
                rotation *= Quaternion.Euler(0f, 180f, 0f);
            }
            var at = PlayerActions.LockPoint(_settlement, _target);
            _valid = !_settlement.HasLock(_target) && _settlement.HasPrivilege(Actions.Owner, at.X, at.Z);
            _ghost.transform.SetPositionAndRotation(position, rotation);
            _ghost.sharedMaterials = _valid ? _ghostOk : _ghostBad;
            _ghost.gameObject.SetActive(true);
            _hud.SetPlaceValid(_valid);
        }
    }
}
