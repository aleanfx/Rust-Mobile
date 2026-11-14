using System;
using System.Collections.Generic;
using Oxfall.Core.Wildlife;
using Oxfall.Game.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>Marks an animal's collider, so a blow that lands on it knows which animal it hit.</summary>
    public sealed class AnimalTag : MonoBehaviour
    {
        public int Id;
        public AnimalKind Kind;
    }

    /// <summary>
    /// Draws the animals from what is known of them (alone: the game's own; online: the server's few
    /// updates a second). Each walks on between updates the way it faces, eases to where it should be,
    /// follows the slope of the ground, swings its legs in a trot that quickens with its speed, flinches
    /// when hit and falls on its side when it dies.
    /// </summary>
    public sealed class AnimalViews
    {
        private sealed class View
        {
            public int Id;
            public AnimalKind Kind;
            public GameObject Root;
            public Transform Figure;
            public Transform[] Legs;
            public BoxCollider Collider;
            public Renderer[] Renderers;
            public bool Shown = true;

            // Where it should be (walked on between updates) and where it is drawn.
            public float TargetX, TargetZ, TargetYaw, Speed;
            public float X, Z, Yaw;
            public AnimalState State;
            public float Health = 1f;

            public float Phase;
            public float Gait;
            public float Pitch;
            public float Fallen;
            public float Flinch;
            public bool Seen;
            public float CalledAt = -10f;
        }

        /// <summary>An animal calls out (a wolf or boar starting a charge, a deer bolting, any of them hurt): its kind, where.</summary>
        public event Action<AnimalKind, Vector3> Called;

        private const float DrawDistance = 150f;

        private readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        private readonly Dictionary<AnimalKind, (Mesh Body, Mesh Leg, Vector3[] Hips, Material[] Materials)> _rigs =
            new Dictionary<AnimalKind, (Mesh, Mesh, Vector3[], Material[])>();
        private readonly Func<float, float, float> _height;
        private readonly Transform _parent;
        private readonly List<int> _gone = new List<int>();

        public AnimalViews(MaterialLibrary materials, Func<float, float, float> height)
        {
            _height = height;
            _parent = new GameObject("Animals").transform;
            foreach (AnimalKind kind in new[] { AnimalKind.Boar, AnimalKind.Wolf, AnimalKind.Deer })
            {
                var coat = kind == AnimalKind.Boar ? materials.BoarHide : kind == AnimalKind.Wolf ? materials.WolfFur : materials.DeerHide;
                _rigs[kind] = (MeshFactory.AnimalBody(kind), MeshFactory.AnimalLeg(kind), MeshFactory.AnimalHips(kind),
                    new[] { coat, materials.Hoof, materials.Horn });
            }
        }

        public int Count => _views.Count;

        public IEnumerable<int> Ids => _views.Keys;

        public bool TryGet(int id, out AnimalKind kind, out Vector3 position, out AnimalState state)
        {
            if (_views.TryGetValue(id, out var v))
            {
                kind = v.Kind;
                position = v.Root.transform.position;
                state = v.State;
                return true;
            }
            kind = default;
            position = default;
            state = default;
            return false;
        }

        /// <summary>What is known of the animals now: new ones appear, missing ones go, the rest are steered to their new places.</summary>
        public void Apply(IReadOnlyList<AnimalSnap> animals)
        {
            foreach (var v in _views.Values) v.Seen = false;
            foreach (var a in animals)
            {
                if (!_views.TryGetValue(a.Id, out var v))
                {
                    v = Make(a);
                    _views.Add(a.Id, v);
                }
                v.Seen = true;
                v.TargetX = a.X;
                v.TargetZ = a.Z;
                v.TargetYaw = a.Yaw;
                v.Speed = a.State == AnimalState.Dead ? 0f : a.Speed;
                bool hurt = a.Health < v.Health - 0.001f;
                if (hurt && a.State != AnimalState.Dead) v.Flinch = 1f;
                bool alarm = a.State != v.State && (a.State == AnimalState.Chase || (a.State == AnimalState.Flee && a.Kind == AnimalKind.Deer));
                if ((alarm || hurt) && Time.time - v.CalledAt > 3f)
                {
                    v.CalledAt = Time.time;
                    Called?.Invoke(a.Kind, v.Root.transform.position + Vector3.up * 0.6f);
                }
                v.Health = a.Health;
                if (v.State != a.State && a.State == AnimalState.Dead) SetDead(v, true);
                else if (v.State == AnimalState.Dead && a.State != AnimalState.Dead) SetDead(v, false);
                v.State = a.State;
            }
            _gone.Clear();
            foreach (var v in _views.Values)
                if (!v.Seen) _gone.Add(v.Id);
            foreach (int id in _gone)
            {
                UnityEngine.Object.Destroy(_views[id].Root);
                _views.Remove(id);
            }
        }

        /// <summary>A blow landed on it: a start.</summary>
        public void Flinch(int id)
        {
            if (_views.TryGetValue(id, out var v)) v.Flinch = 1f;
        }

        public void Clear()
        {
            foreach (var v in _views.Values) UnityEngine.Object.Destroy(v.Root);
            _views.Clear();
        }

        /// <summary>Moves and animates every animal; <paramref name="eye"/> is where the camera is, to skip far ones.</summary>
        public void Tick(float dt, Vector3 eye)
        {
            if (dt <= 0f) return;
            float ease = 1f - Mathf.Exp(-8f * dt);
            float turn = 1f - Mathf.Exp(-10f * dt);
            foreach (var v in _views.Values)
            {
                // Walk the target on the way it faces until the next update says otherwise.
                if (v.Speed > 0.01f)
                {
                    float yaw = v.TargetYaw * Mathf.Deg2Rad;
                    v.TargetX += Mathf.Sin(yaw) * v.Speed * dt;
                    v.TargetZ += Mathf.Cos(yaw) * v.Speed * dt;
                }
                float px = v.X, pz = v.Z;
                float jump = (v.TargetX - v.X) * (v.TargetX - v.X) + (v.TargetZ - v.Z) * (v.TargetZ - v.Z);
                if (jump > 100f)
                {
                    v.X = v.TargetX;
                    v.Z = v.TargetZ;
                }
                else
                {
                    v.X += (v.TargetX - v.X) * ease;
                    v.Z += (v.TargetZ - v.Z) * ease;
                }
                v.Yaw = Mathf.LerpAngle(v.Yaw, v.TargetYaw, turn);

                bool show = (new Vector2(v.X - eye.x, v.Z - eye.z)).sqrMagnitude < DrawDistance * DrawDistance;
                if (show != v.Shown)
                {
                    v.Shown = show;
                    foreach (var r in v.Renderers) r.enabled = show;
                }

                float moved = Mathf.Sqrt((v.X - px) * (v.X - px) + (v.Z - pz) * (v.Z - pz)) / dt;
                v.Gait = Mathf.Lerp(v.Gait, Mathf.Min(moved, 9f), 1f - Mathf.Exp(-6f * dt));
                Place(v, dt);
            }
        }

        private void Place(View v, float dt)
        {
            float half = Animals.HalfSize(v.Kind).Z;
            float yawRad = v.Yaw * Mathf.Deg2Rad;
            float fx = Mathf.Sin(yawRad), fz = Mathf.Cos(yawRad);
            float y = _height(v.X, v.Z);
            // Nose up a slope, down a dip.
            float front = _height(v.X + fx * half, v.Z + fz * half), back = _height(v.X - fx * half, v.Z - fz * half);
            float pitch = v.Fallen > 0f ? 0f : Mathf.Clamp(Mathf.Atan2(back - front, half * 2f) * Mathf.Rad2Deg, -25f, 25f);
            v.Pitch = Mathf.Lerp(v.Pitch, pitch, 1f - Mathf.Exp(-8f * dt));
            v.Root.transform.SetPositionAndRotation(new Vector3(v.X, y, v.Z), Quaternion.Euler(v.Pitch, v.Yaw, 0f));

            // A trot: the diagonal pairs together, quicker and longer the faster it goes.
            float stride = v.Kind == AnimalKind.Boar ? 0.75f : v.Kind == AnimalKind.Wolf ? 1.1f : 1.4f;
            v.Phase += v.Gait / stride * Mathf.PI * dt;
            float amount = Mathf.Clamp01(v.Gait / 2.5f);
            float swing = Mathf.Lerp(0f, v.Gait > 3.5f ? 38f : 26f, amount);
            float s = Mathf.Sin(v.Phase);
            if (v.Fallen > 0f)
            {
                v.Fallen = Mathf.Min(1f, v.Fallen + dt * 2.2f);
                swing = 0f;
                s = 0f;
            }
            v.Legs[0].localRotation = Quaternion.Euler(s * swing, 0f, 0f);
            v.Legs[3].localRotation = Quaternion.Euler(s * swing, 0f, 0f);
            v.Legs[1].localRotation = Quaternion.Euler(-s * swing, 0f, 0f);
            v.Legs[2].localRotation = Quaternion.Euler(-s * swing, 0f, 0f);

            // Falls onto its side when dead; a start back and up when hit.
            var size = Animals.HalfSize(v.Kind);
            float bob = Mathf.Abs(Mathf.Cos(v.Phase)) * 0.035f * amount;
            v.Flinch = Mathf.Max(0f, v.Flinch - dt * 4f);
            float fall = Mathf.SmoothStep(0f, 1f, v.Fallen);
            float centre = size.Y;
            v.Figure.localPosition = new Vector3(centre * fall, bob * (1f - fall) + size.X * fall * 0.9f, -v.Flinch * 0.12f);
            v.Figure.localRotation = Quaternion.Euler(-v.Flinch * 14f, 0f, 90f * fall);
        }

        private void SetDead(View v, bool dead)
        {
            var size = Animals.HalfSize(v.Kind);
            if (dead)
            {
                v.Fallen = 0.01f;
                v.Flinch = 1f;
                v.Collider.center = new Vector3(size.Y * 0.4f, size.X + 0.05f, 0f);
                v.Collider.size = new Vector3(size.Y * 2f, size.X * 2f + 0.1f, size.Z * 2f);
            }
            else
            {
                v.Fallen = 0f;
                v.Collider.center = new Vector3(0f, size.Y, 0f);
                v.Collider.size = new Vector3(size.X * 2f, size.Y * 2f, size.Z * 2f);
            }
        }

        private View Make(AnimalSnap a)
        {
            var rig = _rigs[a.Kind];
            var root = new GameObject(Animals.Name(a.Kind) + " " + a.Id);
            root.transform.SetParent(_parent, false);
            var figure = new GameObject("Figure").transform;
            figure.SetParent(root.transform, false);
            var renderers = new List<Renderer> { Part(figure, "Body", rig.Body, rig.Materials, Vector3.zero, true) };
            var legs = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var hip = new GameObject("Leg " + i).transform;
                hip.SetParent(figure, false);
                hip.localPosition = rig.Hips[i];
                legs[i] = hip;
                // Thin legs: their shadows are not worth the draw on a phone.
                renderers.Add(Part(hip, "Leg", rig.Leg, rig.Materials, Vector3.zero, false));
            }
            var collider = root.AddComponent<BoxCollider>();
            var tag = root.AddComponent<AnimalTag>();
            tag.Id = a.Id;
            tag.Kind = a.Kind;
            var v = new View
            {
                Id = a.Id,
                Kind = a.Kind,
                Root = root,
                Figure = figure,
                Legs = legs,
                Collider = collider,
                Renderers = renderers.ToArray(),
                X = a.X,
                Z = a.Z,
                Yaw = a.Yaw,
                TargetX = a.X,
                TargetZ = a.Z,
                TargetYaw = a.Yaw,
                State = a.State,
                Health = a.Health,
                Phase = a.Id * 1.7f,
            };
            SetDead(v, a.State == AnimalState.Dead);
            if (a.State == AnimalState.Dead) v.Fallen = 1f;
            Place(v, 0.02f);
            return v;
        }

        private static Renderer Part(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 at, bool shadow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return r;
        }
    }
}
