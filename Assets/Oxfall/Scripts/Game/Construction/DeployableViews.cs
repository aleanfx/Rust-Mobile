using System;
using System.Collections.Generic;
using Oxfall.Core.Rules;
using Oxfall.Game.Items;
using Oxfall.Game.Rendering;
using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game.Construction
{
    /// <summary>Marks the collider of something set down, so aiming at it finds which one it is.</summary>
    public sealed class DeployableTag : MonoBehaviour
    {
        public int Id;
        public DeployableKind Kind;
    }

    /// <summary>
    /// Draws what is set down in the settlement and nothing else: it listens to the Core
    /// <see cref="Settlement"/> and adds, lights and removes objects to match. A campfire always
    /// burns; a furnace burns while it is lit.
    /// </summary>
    public sealed class DeployableViews
    {
        private readonly Settlement _settlement;
        private readonly Dictionary<DeployableKind, ItemModel> _models = new Dictionary<DeployableKind, ItemModel>();
        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Fire> _fires = new Dictionary<int, Fire>();
        private readonly Mesh _flame;
        private readonly Material _flameMaterial;
        private readonly Transform _root;

        /// <summary>A fire started (a campfire set down, a furnace lit), for its crackle.</summary>
        public event Action<GameObject> FireStarted;

        /// <summary>Something set down is going (picked up, or destroyed): where it stood and what it was made of.</summary>
        public event Action<Bounds, Material> Fell;

        public int Count => _views.Count;

        public DeployableViews(Settlement settlement, IReadOnlyDictionary<string, ItemModel> models, MaterialLibrary materials)
        {
            _settlement = settlement;
            foreach (DeployableKind kind in Enum.GetValues(typeof(DeployableKind)))
            {
                string item = Deployables.ItemOf(kind);
                if (item != null && models.TryGetValue(item, out var model)) _models[kind] = model;
            }
            // A dead player's bag is made from no item, so it has a model of its own.
            _models[DeployableKind.Bag] = new ItemModel { Mesh = MeshFactory.LootBag(), Materials = new[] { materials.Cloth, materials.Rope } };
            _flame = MeshFactory.Flame();
            _flameMaterial = materials.Flame;
            _root = new GameObject("Deployables").transform;
            settlement.DeployableAdded += Show;
            settlement.DeployableRemoved += Hide;
            settlement.DeployableChanged += Changed;
            // Blackened by blasts, the more the closer it is to going.
            settlement.DeployableHealthChanged += (id, health) =>
            {
                if (!_views.TryGetValue(id, out var go) || !_settlement.TryGet(id, out var d)) return;
                BuildingViews.Scorch(go.GetComponent<MeshRenderer>(), health / Mathf.Max(1f, Deployables.MaxHealth(d.Kind)));
            };
        }

        public ItemModel ModelOf(DeployableKind kind) => _models.TryGetValue(kind, out var m) ? m : null;

        public bool TryGetView(int id, out GameObject view) => _views.TryGetValue(id, out view);

        private void Show(Deployable d)
        {
            if (_views.ContainsKey(d.Id) || !_models.TryGetValue(d.Kind, out var model)) return;
            var go = new GameObject(d.Kind.ToString());
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(new Vector3(d.X, d.Y, d.Z), Quaternion.Euler(0f, d.Yaw, 0f));
            go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = model.Materials;
            var collider = go.AddComponent<BoxCollider>();
            var bounds = model.Mesh.bounds;
            collider.center = bounds.center;
            // At least knee high, so it is easy to aim at (a sleeping bag lies flat).
            collider.size = new Vector3(bounds.size.x, Mathf.Max(bounds.size.y, 0.35f), bounds.size.z);
            var tag = go.AddComponent<DeployableTag>();
            tag.Id = d.Id;
            tag.Kind = d.Kind;
            _views.Add(d.Id, go);
            float max = Deployables.MaxHealth(d.Kind);
            if (max > 0f && _settlement.DeployableHealth(d.Id) < max)
                BuildingViews.Scorch(go.GetComponent<MeshRenderer>(), _settlement.DeployableHealth(d.Id) / max);
            if (d.Kind == DeployableKind.Campfire)
                Light(d.Id, go, new Vector3(0f, 0.04f, 0f), 1.7f, 5, 14f, 3.4f);
            else if (d.Kind == DeployableKind.Furnace && _settlement.IsLit(d.Id))
                Changed(d.Id);
        }

        private void Hide(int id)
        {
            if (!_views.TryGetValue(id, out var go)) return;
            _views.Remove(id);
            _fires.Remove(id);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) Fell?.Invoke(renderer.bounds, renderer.sharedMaterial);
            UnityEngine.Object.Destroy(go);
        }

        /// <summary>A furnace lit up or went out.</summary>
        private void Changed(int id)
        {
            if (!_views.TryGetValue(id, out var go) || !_settlement.TryGet(id, out var d) || d.Kind != DeployableKind.Furnace) return;
            bool lit = _settlement.IsLit(id);
            if (lit && !_fires.ContainsKey(id))
                Light(id, go, new Vector3(0f, 0.16f, 0.42f), 0.9f, 3, 9f, 2.6f);
            else if (!lit && _fires.TryGetValue(id, out var fire))
            {
                _fires.Remove(id);
                UnityEngine.Object.Destroy(fire.gameObject);
            }
        }

        private void Light(int id, GameObject go, Vector3 at, float scale, int flames, float range, float intensity)
        {
            var fire = Fire.Create(go.transform, at, scale, flames, range, intensity, _flame, _flameMaterial);
            _fires[id] = fire;
            FireStarted?.Invoke(fire.gameObject);
        }
    }
}
