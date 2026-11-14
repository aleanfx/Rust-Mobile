using System.Collections.Generic;
using Oxfall.Core.Net;
using Oxfall.Game.Rendering;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Net
{
    /// <summary>Marks a sleeper's body, so a blow or an arrow that lands on it knows whose it is.</summary>
    public sealed class SleeperTag : MonoBehaviour
    {
        public int Owner;
        public string Name;
    }

    /// <summary>
    /// Survivors asleep on the island (their players away): each lies on its back where it was left,
    /// breathing slowly, until its player comes back or someone kills it. As the server lists them.
    /// </summary>
    public sealed class SleeperViews
    {
        private const float DrawDistance = 150f;

        private sealed class View
        {
            public Transform Root;
            public Transform Figure;
            public SleeperTag Tag;
            public float Phase;
            public float Flinch;
        }

        private static Mesh _body, _leg, _arm;
        private readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        private readonly HashSet<int> _listed = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();
        private readonly MaterialLibrary _materials;
        private readonly Transform _parent;

        public SleeperViews(MaterialLibrary materials)
        {
            _materials = materials;
            _parent = new GameObject("Sleepers").transform;
        }

        public int Count => _views.Count;

        /// <summary>The whole list as the server sent it: new sleepers lie down, the ones not in it go.</summary>
        public void Apply(IReadOnlyList<SleeperSnap> sleepers, int me)
        {
            _listed.Clear();
            foreach (var s in sleepers)
            {
                if (s.Owner == me) continue;
                _listed.Add(s.Owner);
                if (!_views.TryGetValue(s.Owner, out var view)) _views[s.Owner] = view = Create(s.Owner);
                view.Root.SetPositionAndRotation(new Vector3(s.X, s.Y, s.Z), Quaternion.Euler(0f, s.Yaw, 0f));
                view.Tag.Name = s.Name;
                view.Root.name = "Sleeper " + s.Owner + " " + s.Name;
            }
            _gone.Clear();
            foreach (var owner in _views.Keys)
                if (!_listed.Contains(owner)) _gone.Add(owner);
            foreach (var owner in _gone) Remove(owner);
        }

        public void Remove(int owner)
        {
            if (!_views.TryGetValue(owner, out var view)) return;
            Object.Destroy(view.Root.gameObject);
            _views.Remove(owner);
        }

        public void Clear()
        {
            foreach (var view in _views.Values) Object.Destroy(view.Root.gameObject);
            _views.Clear();
        }

        /// <summary>A blow landed on this body: it jolts.</summary>
        public void Flinch(int owner)
        {
            if (_views.TryGetValue(owner, out var view)) view.Flinch = 1f;
        }

        public bool TryGet(int owner, out Vector3 position, out string name)
        {
            if (_views.TryGetValue(owner, out var view))
            {
                position = view.Root.position;
                name = view.Tag.Name;
                return true;
            }
            position = default;
            name = null;
            return false;
        }

        /// <summary>The nearest sleeper to <paramref name="from"/> (owner 0 if there are none).</summary>
        public int Nearest(Vector3 from, out Vector3 position)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            position = default;
            foreach (var pair in _views)
            {
                float d = (pair.Value.Root.position - from).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = pair.Key;
                    position = pair.Value.Root.position;
                }
            }
            return best;
        }

        /// <summary>Where each body lies and whose it is, for name tags.</summary>
        public IEnumerable<(Vector3 Position, string Name)> All()
        {
            foreach (var view in _views.Values)
                if (view.Root.gameObject.activeSelf) yield return (view.Root.position, view.Tag.Name);
        }

        /// <summary>Slow breathing, a jolt when struck; too far away they are not drawn.</summary>
        public void Tick(float dt, Vector3 eye)
        {
            foreach (var view in _views.Values)
            {
                bool near = (view.Root.position - eye).sqrMagnitude < DrawDistance * DrawDistance;
                if (view.Root.gameObject.activeSelf != near) view.Root.gameObject.SetActive(near);
                if (!near) continue;
                view.Phase += dt * 1.5f;
                view.Flinch = Mathf.MoveTowards(view.Flinch, 0f, dt * 5f);
                // On its back (head away from where it faced, face up); the chest rises and falls.
                float breath = Mathf.Sin(view.Phase) * 0.5f + 0.5f;
                view.Figure.localScale = new Vector3(1f, 1f, 1f + breath * 0.06f);
                view.Figure.localRotation = Quaternion.Euler(-90f + view.Flinch * 10f, 0f, view.Flinch * 6f);
            }
        }

        private View Create(int owner)
        {
            _body ??= MeshFactory.SurvivorBody();
            _leg ??= MeshFactory.SurvivorLeg();
            _arm ??= MeshFactory.SurvivorArm();
            var m = _materials;
            var root = new GameObject("Sleeper " + owner).transform;
            root.SetParent(_parent, false);
            var figure = new GameObject("Figure").transform;
            figure.SetParent(root, false);
            // Lying down the figure's height runs along -Z: shifted so the body is centred where it lies.
            figure.localPosition = new Vector3(0f, 0.13f, 0.86f);
            figure.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            Part(figure, Vector3.zero, Quaternion.identity, _body, true, m.Shirt, m.Skin);
            Part(figure, new Vector3(-0.09f, 0.92f, 0f), Quaternion.Euler(0f, 0f, 3f), _leg, false, m.Trousers, m.Char);
            Part(figure, new Vector3(0.09f, 0.92f, 0f), Quaternion.Euler(0f, 0f, -3f), _leg, false, m.Trousers, m.Char);
            // Arms down at the sides, hands resting a little out from the hips.
            Part(figure, new Vector3(-0.23f, 1.4f, 0f), Quaternion.Euler(0f, 0f, -9f), _arm, false, m.Shirt, m.Skin);
            Part(figure, new Vector3(0.23f, 1.4f, 0f), Quaternion.Euler(0f, 0f, 9f), _arm, false, m.Shirt, m.Skin);

            var box = root.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.16f, 0f);
            box.size = new Vector3(0.64f, 0.34f, 1.86f);
            var tag = root.gameObject.AddComponent<SleeperTag>();
            tag.Owner = owner;
            return new View { Root = root, Figure = figure, Tag = tag, Phase = owner * 1.7f };
        }

        private static void Part(Transform parent, Vector3 at, Quaternion rotation, Mesh mesh, bool shadows, params Material[] materials)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = rotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
    }
}
