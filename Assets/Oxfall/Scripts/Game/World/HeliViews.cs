using System;
using System.Collections.Generic;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Game.Audio;
using Oxfall.Game.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>Marks a patrol helicopter's collider, for shots and arrows: which one, and whether this is a rotor (its weak spot).</summary>
    public sealed class HeliTag : MonoBehaviour
    {
        public int Id;
        public bool Rotor;
    }

    /// <summary>
    /// The patrol helicopters as everyone sees and hears them: the dark gunship easing from where it was
    /// to where it is told, nose dipping and banking, rotors a blur, the thud of its blades heard from
    /// across the island and a searchlight at night; black smoke trailing once it is badly hurt; the spin
    /// down when it is shot down, the blast where it hits, and the wreck burning for minutes after. Its
    /// gun's bursts (tracers, flashes, dust kicked up round its target) and its rockets streaking down
    /// on smoke trails to burst where they land are drawn from what it fired.
    /// </summary>
    public sealed class HeliViews
    {
        private sealed class View
        {
            public int Id;
            public GameObject Root;
            public Transform Main, Tail, Lamp;
            public MeshRenderer Body;
            public Light Spot;
            public AudioSource Rotor, Burning;
            public Vector3 Pos, Goal;
            public Quaternion Rot, GoalRot;
            public HeliSnap Snap;
            public float Spin = 1f, NextPuff;
            public bool Wrecked;
            public List<Fire> Fires;
            public float SeenAt;
        }

        private sealed class Rocket
        {
            public Transform T;
            public Vector3 From, To;
            public float Start, End, NextPuff;
        }

        private static Mesh _body, _main, _tail, _rocket, _flame;

        private readonly MaterialLibrary _materials;
        private readonly SoundBank _bank;
        private readonly Explosions _effects;
        private readonly Transform _parent;
        private readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        private readonly List<Rocket> _rockets = new List<Rocket>();
        private readonly List<int> _gone = new List<int>();
        private int _blastIds = int.MinValue + 1;

        /// <summary>A helicopter showed up over the island (not one already there when this machine joined), or went down.</summary>
        public event Action<HeliSnap> Arrived, Downed;

        /// <summary>Where a player is (their key), for the searchlight to follow; null if unknown.</summary>
        public Func<int, Vector3?> Locate { get; set; }

        /// <summary>Night: the searchlight is on.</summary>
        public bool Night { get; set; }

        public HeliViews(MaterialLibrary materials, SoundBank bank, Explosions effects)
        {
            _materials = materials;
            _bank = bank;
            _effects = effects;
            _parent = new GameObject("Patrol helicopters").transform;
        }

        public int Count => _views.Count;

        public IEnumerable<HeliSnap> All
        {
            get
            {
                foreach (var v in _views.Values) yield return v.Snap;
            }
        }

        /// <summary>Where a helicopter is drawn now (for tests and the camera).</summary>
        public bool TryGetPosition(int id, out Vector3 at)
        {
            at = default;
            if (!_views.TryGetValue(id, out var v)) return false;
            at = v.Root.transform.position;
            return true;
        }

        /// <summary>
        /// What is known of the helicopters now (<paramref name="snap"/>: each update whole; those left out are
        /// gone). <paramref name="instant"/>: placed straight there (the game alone, every frame).
        /// </summary>
        public void Apply(IReadOnlyList<HeliSnap> snaps, bool instant, bool first = false)
        {
            float now = Time.time;
            foreach (var s in snaps)
            {
                if (!_views.TryGetValue(s.Id, out var v))
                {
                    v = Make(s);
                    _views[s.Id] = v;
                    if (!first && !s.Down) Arrived?.Invoke(s);
                }
                var was = v.Snap;
                v.Snap = s;
                v.SeenAt = now;
                v.Goal = new Vector3(s.X, s.Y, s.Z);
                v.GoalRot = Quaternion.Euler(s.Pitch, s.Yaw, -s.Roll);
                if (instant)
                {
                    v.Pos = v.Goal;
                    v.Rot = v.GoalRot;
                }
                if (s.State == HeliState.Crashing && was.State != HeliState.Crashing && was.Id == s.Id && !was.Down)
                {
                    Downed?.Invoke(s);
                    if (_bank != null) GameAudio.PlayAt(_bank.Explosion, v.Goal, 0.7f, 20f, 300f, 0.05f);
                }
                if (s.State == HeliState.Wreck && !v.Wrecked) Wreck(v, was.Id == s.Id && was.State == HeliState.Crashing);
            }
            _gone.Clear();
            foreach (var pair in _views)
            {
                bool listed = false;
                foreach (var s in snaps)
                    if (s.Id == pair.Key)
                    {
                        listed = true;
                        break;
                    }
                if (!listed) _gone.Add(pair.Key);
            }
            foreach (int id in _gone)
            {
                UnityEngine.Object.Destroy(_views[id].Root);
                _views.Remove(id);
            }
        }

        public void Clear()
        {
            foreach (var v in _views.Values) UnityEngine.Object.Destroy(v.Root);
            _views.Clear();
            foreach (var r in _rockets) UnityEngine.Object.Destroy(r.T.gameObject);
            _rockets.Clear();
        }

        /// <summary>Something a helicopter fired: a round's tracer, flash and strike, or a rocket on its way.</summary>
        public void Shot(HeliShot s)
        {
            var from = new Vector3(s.FromX, s.FromY, s.FromZ);
            var to = new Vector3(s.ToX, s.ToY, s.ToZ);
            switch (s.Kind)
            {
                case HeliShotKind.Bullet:
                {
                    if (_views.TryGetValue(s.Heli, out var v) && !v.Wrecked) from = v.Root.transform.TransformPoint(MeshFactory.HeliMuzzle);
                    var dir = (to - from).normalized;
                    if (_effects != null)
                    {
                        _effects.MuzzleFlash(from, dir, false);
                        _effects.Bullet(from, to);
                        _effects.Impact(to, s.Target != 0 ? -dir : Vector3.up, s.Target != 0);
                    }
                    if (_bank != null) GameAudio.PlayAt(_bank.HeliGun, from, 0.55f, 15f, 260f, 0.08f);
                    break;
                }
                case HeliShotKind.Rocket:
                {
                    _rocket ??= MeshFactory.HeliRocket();
                    var go = new GameObject("Rocket");
                    go.transform.SetParent(_parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = _rocket;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = new[] { _materials.GunMetal, _materials.WarnYellow };
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    float flight = Vector3.Distance(from, to) / PatrolHelis.RocketSpeed;
                    go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
                    _rockets.Add(new Rocket { T = go.transform, From = from, To = to, Start = Time.time, End = Time.time + flight });
                    _effects?.MuzzleFlash(from, (to - from).normalized, true);
                    if (_bank != null) GameAudio.PlayAt(_bank.RocketWhoosh, from, 0.8f, 15f, 260f, 0.06f);
                    break;
                }
            }
        }

        public void Tick(float dt, Vector3 camera)
        {
            float now = Time.time;
            foreach (var v in _views.Values)
            {
                if (!v.Wrecked)
                {
                    // Eases to where it should be (a tenth of a second behind online), never jumping.
                    float k = 1f - Mathf.Exp(-dt * 8f);
                    v.Pos = Vector3.Lerp(v.Pos, v.Goal, k);
                    v.Rot = Quaternion.Slerp(v.Rot, v.GoalRot, k);
                    v.Root.transform.SetPositionAndRotation(v.Pos, v.Rot);
                }
                // The rotors: a blur while it flies, slowing on the way down, still once wrecked.
                float spin = v.Wrecked ? 0f : v.Snap.State == HeliState.Crashing ? 0.55f : 1f;
                v.Spin = Mathf.MoveTowards(v.Spin, spin, dt * 0.4f);
                if (v.Spin > 0.001f)
                {
                    v.Main.Rotate(0f, 1300f * v.Spin * dt, 0f, Space.Self);
                    v.Tail.Rotate(2600f * v.Spin * dt, 0f, 0f, Space.Self);
                }
                if (v.Rotor != null) v.Rotor.pitch = Mathf.Lerp(0.7f, 1f, v.Spin);

                // Badly hurt, or going down: black smoke pours from the engine.
                bool smoke = v.Wrecked || v.Snap.Smoking || v.Snap.State == HeliState.Crashing;
                if (smoke && _effects != null && now >= v.NextPuff)
                {
                    v.NextPuff = now + (v.Wrecked ? 0.18f : 0.08f);
                    var engine = v.Root.transform.TransformPoint(new Vector3(0f, 1.3f, -1.5f));
                    var drift = v.Wrecked ? new Vector3(0.6f, 0f, 0.3f) : -(v.Goal - v.Pos) * 2f;
                    _effects.BurningSmoke(engine, drift, v.Wrecked ? 1.6f : 1.1f);
                }

                // The searchlight at night, on whoever it hunts or ahead and below.
                if (v.Spot != null)
                {
                    bool on = Night && !v.Wrecked && v.Snap.State != HeliState.Crashing;
                    if (v.Spot.enabled != on) v.Spot.enabled = on;
                    if (on)
                    {
                        Vector3? target = v.Snap.State == HeliState.Engage && Locate != null ? Locate(v.Snap.Target) : null;
                        var aim = target.HasValue ? (target.Value - v.Lamp.position).normalized
                            : v.Root.transform.TransformDirection(new Vector3(0f, -0.75f, 0.66f));
                        v.Lamp.rotation = Quaternion.Slerp(v.Lamp.rotation, Quaternion.LookRotation(aim), 1f - Mathf.Exp(-dt * 5f));
                    }
                }
            }

            for (int i = _rockets.Count - 1; i >= 0; i--)
            {
                var r = _rockets[i];
                float t = Mathf.Clamp01((now - r.Start) / Mathf.Max(0.01f, r.End - r.Start));
                var p = Vector3.Lerp(r.From, r.To, t);
                r.T.position = p;
                if (now >= r.NextPuff)
                {
                    r.NextPuff = now + 0.02f;
                    _effects?.RocketTrail(p - r.T.forward * 0.5f, (r.To - r.From).normalized * PatrolHelis.RocketSpeed);
                }
                if (t < 1f) continue;
                _effects?.Blast(_blastIds++, r.To);
                UnityEngine.Object.Destroy(r.T.gameObject);
                _rockets.RemoveAt(i);
            }
        }

        private View Make(HeliSnap s)
        {
            _body ??= MeshFactory.PatrolHeli();
            _main ??= MeshFactory.HeliMainRotor();
            _tail ??= MeshFactory.HeliTailRotor();
            var root = new GameObject("Patrol helicopter " + s.Id);
            root.transform.SetParent(_parent, false);
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = _body;
            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { _materials.HeliPaint, _materials.GunMetal, _materials.Glass, _materials.WarnYellow, _materials.Rubber };

            var main = new GameObject("Main rotor").transform;
            main.SetParent(root.transform, false);
            main.localPosition = MeshFactory.HeliRotorHub;
            main.gameObject.AddComponent<MeshFilter>().sharedMesh = _main;
            var mr = main.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { _materials.GunMetal, _materials.WarnYellow };
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var tail = new GameObject("Tail rotor").transform;
            tail.SetParent(root.transform, false);
            tail.localPosition = MeshFactory.HeliTailHub;
            tail.gameObject.AddComponent<MeshFilter>().sharedMesh = _tail;
            tail.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { _materials.GunMetal, _materials.WarnYellow };

            // What shots strike: the body and boom, and its weak spots, the main rotor's hub and the tail rotor.
            Collider(root.transform, "Hull", new Vector3(0f, -0.3f, 0.6f), new Vector3(2.3f, 2f, 7.2f), s.Id, false);
            Collider(root.transform, "Boom", new Vector3(0f, 0.2f, -5.8f), new Vector3(0.9f, 1f, 6f), s.Id, false);
            Collider(root.transform, "Wings", new Vector3(0f, -0.5f, 0.1f), new Vector3(5f, 0.6f, 1.6f), s.Id, false);
            Collider(root.transform, "Rotor hub", MeshFactory.HeliRotorHub + new Vector3(0f, -0.3f, 0f), new Vector3(1.4f, 0.9f, 1.4f), s.Id, true);
            Collider(root.transform, "Tail rotor", MeshFactory.HeliTailHub, new Vector3(0.5f, 2.3f, 2.3f), s.Id, true);

            // The searchlight under the nose.
            var lamp = new GameObject("Searchlight").transform;
            lamp.SetParent(root.transform, false);
            lamp.localPosition = new Vector3(0f, -1.0f, 3.6f);
            lamp.localRotation = Quaternion.Euler(45f, 0f, 0f);
            var spot = lamp.gameObject.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 110f;
            spot.spotAngle = 22f;
            spot.innerSpotAngle = 12f;
            spot.intensity = 9f;
            spot.color = new Color(1f, 0.97f, 0.9f);
            spot.shadows = LightShadows.None;
            spot.enabled = false;

            var v = new View
            {
                Id = s.Id, Root = root, Main = main, Tail = tail, Lamp = lamp, Body = renderer, Spot = spot,
                Pos = new Vector3(s.X, s.Y, s.Z), Rot = Quaternion.Euler(s.Pitch, s.Yaw, -s.Roll),
            };
            v.Goal = v.Pos;
            v.GoalRot = v.Rot;
            root.transform.SetPositionAndRotation(v.Pos, v.Rot);
            if (_bank != null)
            {
                v.Rotor = GameAudio.Attach(root, _bank.RotorLoop, 1f, 420f);
                v.Rotor.minDistance = 35f;
            }
            if (s.State == HeliState.Wreck) Wreck(v, false);
            return v;
        }

        private static void Collider(Transform parent, string name, Vector3 centre, Vector3 size, int id, bool rotor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
            box.isTrigger = true;
            var tag = go.AddComponent<HeliTag>();
            tag.Id = id;
            tag.Rotor = rotor;
        }

        /// <summary>Down for good: a blast where it hit (if it was seen falling), then a charred, burning hulk.</summary>
        private void Wreck(View v, bool fell)
        {
            v.Wrecked = true;
            var s = v.Snap;
            v.Pos = new Vector3(s.X, s.Y, s.Z);
            v.Rot = Quaternion.Euler(s.Pitch, s.Yaw, -s.Roll);
            // Settled on its belly, skids crushed.
            v.Root.transform.SetPositionAndRotation(v.Pos + Vector3.up * 1.1f, v.Rot);
            v.Body.sharedMaterials = new[] { _materials.Char, _materials.Char, _materials.Glass, _materials.Char, _materials.Rubber };
            // The rotor blades sheared off and the tail rotor gone.
            v.Main.localRotation = Quaternion.Euler(8f, 37f, -12f);
            v.Main.localScale = new Vector3(0.45f, 1f, 0.45f);
            v.Tail.gameObject.SetActive(false);
            foreach (var tag in v.Root.GetComponentsInChildren<HeliTag>()) tag.gameObject.SetActive(false);
            if (v.Rotor != null) v.Rotor.Stop();
            _flame ??= MeshFactory.Flame();
            v.Fires = new List<Fire>
            {
                Fire.Create(v.Root.transform, new Vector3(0f, 1.0f, -1.2f), 5f, 4, 18f, 3.5f, _flame, _materials.Flame),
                Fire.Create(v.Root.transform, new Vector3(0.6f, 0.2f, 1.6f), 3.5f, 3, 1f, 0f, _flame, _materials.Flame),
                Fire.Create(v.Root.transform, new Vector3(-0.3f, 0.5f, -4.5f), 3f, 3, 1f, 0f, _flame, _materials.Flame),
            };
            // One light is enough for the whole blaze.
            v.Fires[1].Light.enabled = false;
            v.Fires[2].Light.enabled = false;
            if (_bank != null)
            {
                v.Burning = GameAudio.Attach(v.Root, _bank.FireLoop, 0.9f, 60f);
                v.Burning.minDistance = 6f;
            }
            if (!fell) return;
            _effects?.Blast(_blastIds++, v.Pos + Vector3.up * 0.8f);
            _effects?.Blast(_blastIds++, v.Pos + v.Root.transform.forward * 2.5f + Vector3.up * 1.5f);
        }
    }
}
