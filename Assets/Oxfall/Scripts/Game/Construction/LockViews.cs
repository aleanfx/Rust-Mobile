using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Rules;
using Oxfall.Game.Rendering;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// Draws the code locks the settlement holds: on both faces of a door (it swings with it), on the
    /// front of a box, on the right-hand door of a tool cupboard. The light on the keypad is red while
    /// locked and green while open, as in the PC game.
    /// </summary>
    public sealed class LockViews
    {
        // Where a lock sits on what it locks, in that thing's own space (its back on the surface).
        private static readonly Vector3 OnDoor = new Vector3(0.86f, 1.3f, 0.036f);
        private static readonly Vector3 OnBox = new Vector3(0.15f, 0.26f, 0.281f);
        private static readonly Vector3 OnCupboard = new Vector3(0.15f, 0.98f, 0.236f);

        private readonly Settlement _settlement;
        private readonly BuildingViews _buildings;
        private readonly DeployableViews _deployables;
        private readonly Material[] _locked;
        private readonly Material[] _open;
        private readonly Dictionary<LockTarget, List<MeshRenderer>> _views = new Dictionary<LockTarget, List<MeshRenderer>>();

        public Mesh Mesh { get; }

        public int Count => _views.Count;

        public LockViews(Settlement settlement, BuildingViews buildings, DeployableViews deployables, MaterialLibrary materials)
        {
            _settlement = settlement;
            _buildings = buildings;
            _deployables = deployables;
            Mesh = MeshFactory.CodeLock();
            _locked = new[] { materials.LockBody, materials.LockKeys, materials.LedRed };
            _open = new[] { materials.LockBody, materials.LockKeys, materials.LedGreen };
            settlement.LockChanged += Refresh;
            // A door or box drawn again (the whole island arriving from the server) gets its lock back.
            settlement.Grid.DoorPlaced += edge => Refresh(LockTarget.Door(edge));
            settlement.DeployableAdded += d => Refresh(LockTarget.On(d.Id));
        }

        /// <summary>Where a lock on this goes, in the world (its front face): false if it is not drawn.</summary>
        public bool TryPose(LockTarget target, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (!TryAnchor(target, out var anchor, out var local)) return false;
            position = anchor.TransformPoint(local);
            rotation = anchor.rotation;
            return true;
        }

        /// <summary>What a collider belongs to, if a lock can go on it (a hanging door, a box, a tool cupboard).</summary>
        public static bool TryTargetOf(Collider collider, Settlement settlement, out LockTarget target)
        {
            target = default;
            if (collider == null) return false;
            var piece = collider.GetComponent<PieceTag>();
            if (piece != null && piece.Piece.IsEdge && settlement.Grid.HasDoor(piece.Piece.Edge))
            {
                target = LockTarget.Door(piece.Piece.Edge);
                return true;
            }
            var tag = collider.GetComponent<DeployableTag>();
            if (tag == null || !CodeLocks.Lockable(tag.Kind) || !settlement.TryGet(tag.Id, out _)) return false;
            target = LockTarget.On(tag.Id);
            return true;
        }

        private bool TryAnchor(LockTarget target, out Transform anchor, out Vector3 local)
        {
            local = default;
            anchor = null;
            if (target.IsDoor)
            {
                if (!_buildings.TryGetDoor(target.Edge, out anchor)) return false;
                local = OnDoor;
                return true;
            }
            if (!_deployables.TryGetView(target.Id, out var view) || !_settlement.TryGet(target.Id, out var d)) return false;
            anchor = view.transform;
            local = d.Kind == DeployableKind.ToolCupboard ? OnCupboard : OnBox;
            return true;
        }

        private void Refresh(LockTarget target)
        {
            if (!_settlement.TryGetLock(target, out var state))
            {
                Clear(target);
                return;
            }
            if (!_views.TryGetValue(target, out var renderers) || renderers.Count == 0 || renderers[0] == null)
            {
                Clear(target);
                if (!TryAnchor(target, out var anchor, out var local)) return;
                renderers = new List<MeshRenderer> { Make(anchor, local, false) };
                // A door has the keypad on both faces.
                if (target.IsDoor) renderers.Add(Make(anchor, new Vector3(local.x, local.y, -local.z), true));
                _views[target] = renderers;
            }
            var materials = state.Locked ? _locked : _open;
            foreach (var r in renderers)
                if (r != null && r.sharedMaterials[2] != materials[2]) r.sharedMaterials = materials;
        }

        private MeshRenderer Make(Transform anchor, Vector3 local, bool back)
        {
            var go = new GameObject("Code lock");
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = local;
            go.transform.localRotation = back ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = _open;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        private void Clear(LockTarget target)
        {
            if (!_views.TryGetValue(target, out var renderers)) return;
            _views.Remove(target);
            foreach (var r in renderers)
                if (r != null) Object.Destroy(r.gameObject);
        }
    }
}
