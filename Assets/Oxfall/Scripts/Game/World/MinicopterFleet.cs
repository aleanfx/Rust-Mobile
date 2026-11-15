using System;
using System.Collections.Generic;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;
using Oxfall.Game.Rendering;
using UnityEngine;

namespace Oxfall.Game.World
{
    /// <summary>
    /// The minicopters on screen. Alone, this keeps them (they drop when nobody flies them, a wreck
    /// is rebuilt in time); online, it shows where the server says they are and eases between
    /// updates. The one this player flies is moved here, by the shared rules, and sent to the server.
    /// </summary>
    public sealed class MinicopterFleet
    {
        private sealed class View
        {
            public Minicopter Copter;
            public GameObject Root;
            public Transform Rotor;
            public MeshRenderer Body;
            public AudioSource Engine;
            public Vector3 Target;
            public float TargetYaw;
            public float Spin;
            public bool ShownWrecked;
        }

        private readonly IslandGround _ground;
        private readonly Transform _parent;
        private readonly Mesh _body, _rotor;
        private readonly Material[] _bodyMaterials, _rotorMaterials, _wreckMaterials;
        private readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        private float _clock;

        /// <summary>The copter this player flies (null: none).</summary>
        public Minicopter Flying { get; private set; }

        /// <summary>Online: the server says this player now flies this copter, or no longer does (null).</summary>
        public event Action<Minicopter> FlyingChanged;

        /// <summary>The engine's sound; once set, every copter has one.</summary>
        public AudioClip EngineClip { get; set; }

        public MinicopterFleet(IslandGround ground, MaterialLibrary materials, bool alone)
        {
            _ground = ground;
            _parent = new GameObject("Minicopters").transform;
            _body = MeshFactory.Minicopter();
            _rotor = MeshFactory.MinicopterRotor();
            _bodyMaterials = new[] { materials.BarrelPaint[2], materials.GunMetal, materials.Rubber, materials.WarnYellow };
            _rotorMaterials = new[] { materials.GunMetal, materials.WarnYellow };
            _wreckMaterials = new[] { materials.Char, materials.Char, materials.Rubber, materials.Char };
            if (alone)
                foreach (var m in Oxfall.Core.Rules.Flying.Spawn(ground)) Add(m);
        }

        public int Count => _views.Count;

        public IEnumerable<Minicopter> All
        {
            get { foreach (var v in _views.Values) yield return v.Copter; }
        }

        private View Add(Minicopter m)
        {
            var go = new GameObject("Minicopter " + m.Id);
            go.transform.SetParent(_parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = _body;
            var body = go.AddComponent<MeshRenderer>();
            body.sharedMaterials = _bodyMaterials;
            var rotor = new GameObject("Rotor");
            rotor.transform.SetParent(go.transform, false);
            rotor.transform.localPosition = MeshFactory.MinicopterRotorHub;
            rotor.AddComponent<MeshFilter>().sharedMesh = _rotor;
            rotor.AddComponent<MeshRenderer>().sharedMaterials = _rotorMaterials;
            // Something to bump into on foot; nothing to stand inside.
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.9f, -0.2f);
            box.size = new Vector3(1.3f, 1.6f, 2.6f);
            var view = new View { Copter = m, Root = go, Rotor = rotor.transform, Body = body };
            view.Target = new Vector3(m.X, m.Y, m.Z);
            view.TargetYaw = m.Yaw;
            go.transform.SetPositionAndRotation(view.Target, Quaternion.Euler(0f, m.Yaw, 0f));
            _views[m.Id] = view;
            return view;
        }

        /// <summary>The copter nearest the player within reach that they look towards (null: none).</summary>
        public Minicopter Mountable(Vector3 feet, Vector3 lookForward)
        {
            if (Flying != null) return null;
            Minicopter best = null;
            float bestD = float.MaxValue;
            var flat = new Vector2(lookForward.x, lookForward.z);
            foreach (var v in _views.Values)
            {
                if (!Oxfall.Core.Rules.Flying.CanMount(v.Copter, feet.x, feet.y, feet.z)) continue;
                var to = new Vector2(v.Copter.X - feet.x, v.Copter.Z - feet.z);
                float d = to.magnitude;
                if (d > 1.6f && flat.sqrMagnitude > 1e-4f && Vector2.Angle(flat, to) > 55f) continue;
                if (d < bestD) { bestD = d; best = v.Copter; }
            }
            return best;
        }

        private const ushort MeAlone = 1;

        /// <summary>Alone: gets in at once, pouring this many cans of fuel in; how many went in comes back.</summary>
        public bool MountAlone(Minicopter m, int cans, out int poured)
        {
            poured = 0;
            if (m == null || m.Driver != 0 || m.Wrecked) return false;
            m.Driver = MeAlone;
            poured = Mathf.Min(cans, Oxfall.Core.Rules.Flying.TankSpace(m));
            if (poured > 0) m.Fuel += poured;
            Flying = m;
            return true;
        }

        /// <summary>Alone: gets out beside it; where the pilot goes comes back.</summary>
        public Vector3 Leave()
        {
            var m = Flying;
            if (m == null) return Vector3.zero;
            m.Driver = 0;
            Flying = null;
            Oxfall.Core.Rules.Flying.Landing(m, _ground, out float x, out float y, out float z);
            return new Vector3(x, y, z);
        }

        /// <summary>Stops flying here without asking anyone (online, the server already let go).</summary>
        public void Drop() => Flying = null;

        /// <summary>Flies one frame; the seat's position comes back, and how hard it hit the ground (0: it did not).</summary>
        public Vector3 Fly(Vector2 stick, bool climb, float lookYaw, float lookPitch, float dt, out float crash)
        {
            crash = 0f;
            var m = Flying;
            if (m == null) return Vector3.zero;
            crash = Oxfall.Core.Rules.Flying.Step(m, stick.y, stick.x, climb, lookYaw, lookPitch, dt, _ground);
            if (_views.TryGetValue(m.Id, out var v))
            {
                v.Target = new Vector3(m.X, m.Y, m.Z);
                v.TargetYaw = m.Yaw;
                Place(v, v.Target, m.Yaw);
            }
            Oxfall.Core.Rules.Flying.Seat(m, out float sx, out float sy, out float sz);
            return new Vector3(sx, sy, sz);
        }

        /// <summary>Online: the copters as the server sees them.</summary>
        public void Apply(IReadOnlyList<MinicopterSnap> copters, ushort me)
        {
            foreach (var snap in copters)
            {
                if (!_views.TryGetValue(snap.Id, out var v)) v = Add(new Minicopter(snap.Id, snap.X, snap.Y, snap.Z, snap.Yaw));
                var m = v.Copter;
                m.Driver = snap.Driver;
                m.Fuel = snap.Fuel;
                m.Health = snap.Health;
                bool mine = me != 0 && snap.Driver == me;
                if (mine && Flying != m)
                {
                    m.X = snap.X; m.Y = snap.Y; m.Z = snap.Z; m.Yaw = snap.Yaw;
                    m.Vx = m.Vy = m.Vz = 0f;
                    Flying = m;
                    FlyingChanged?.Invoke(m);
                }
                else if (!mine && Flying == m)
                {
                    Flying = null;
                    FlyingChanged?.Invoke(null);
                }
                if (mine)
                {
                    // Flown here; only pulled back if the server would not believe where it went.
                    float dx = snap.X - m.X, dy = snap.Y - m.Y, dz = snap.Z - m.Z;
                    if (dx * dx + dy * dy + dz * dz > 8f * 8f) { m.X = snap.X; m.Y = snap.Y; m.Z = snap.Z; m.Vx = m.Vy = m.Vz = 0f; }
                    continue;
                }
                m.X = snap.X; m.Y = snap.Y; m.Z = snap.Z; m.Yaw = snap.Yaw;
                v.Target = new Vector3(snap.X, snap.Y, snap.Z);
                v.TargetYaw = snap.Yaw;
            }
        }

        /// <summary>
        /// Alone, copters nobody flies drop and wrecks are rebuilt in time; every copter eases to its
        /// place, its rotor turns while it runs and its engine sounds.
        /// </summary>
        public void Tick(float dt, bool alone)
        {
            _clock += dt;
            foreach (var v in _views.Values)
            {
                var m = v.Copter;
                if (alone && m != Flying)
                {
                    if (m.Wrecked)
                    {
                        if (m.RebuiltAt <= 0f) m.RebuiltAt = _clock + Oxfall.Core.Rules.Flying.RebuildSeconds;
                        else if (_clock >= m.RebuiltAt) m.Rebuild();
                    }
                    else if (m.Y > Oxfall.Core.Rules.Flying.Floor(_ground, m.X, m.Z) + 0.01f || m.Vy < 0f)
                        Oxfall.Core.Rules.Flying.Step(m, 0f, 0f, false, m.Yaw, 0f, dt, _ground);
                    v.Target = new Vector3(m.X, m.Y, m.Z);
                    v.TargetYaw = m.Yaw;
                }
                if (m != Flying)
                {
                    var t = v.Root.transform;
                    var pos = Vector3.Lerp(t.position, v.Target, Mathf.Min(1f, dt * 8f));
                    if ((pos - v.Target).sqrMagnitude > 400f) pos = v.Target;
                    Place(v, pos, Mathf.LerpAngle(t.eulerAngles.y, v.TargetYaw, Mathf.Min(1f, dt * 6f)));
                }
                // The rotor spins up while it runs and winds down after.
                float want = m.Running ? 1f : 0f;
                v.Spin = Mathf.MoveTowards(v.Spin, want, dt * (m.Running ? 0.5f : 0.25f));
                v.Rotor.localRotation = Quaternion.Euler(0f, (v.Rotor.localEulerAngles.y + v.Spin * 1500f * dt) % 360f, 0f);
                if (v.ShownWrecked != m.Wrecked)
                {
                    v.ShownWrecked = m.Wrecked;
                    v.Body.sharedMaterials = m.Wrecked ? _wreckMaterials : _bodyMaterials;
                    v.Rotor.gameObject.SetActive(!m.Wrecked);
                }
                Sound(v, dt);
            }
        }

        private void Place(View v, Vector3 at, float yaw)
        {
            var m = v.Copter;
            // It leans the way it flies (nose down going forward), and a wreck lies on its side.
            float rad = yaw * Mathf.Deg2Rad;
            float forward = m.Vx * Mathf.Sin(rad) + m.Vz * Mathf.Cos(rad);
            float side = m.Vx * Mathf.Cos(rad) - m.Vz * Mathf.Sin(rad);
            float pitch = m.Wrecked ? 8f : Mathf.Clamp(forward * 0.6f, -10f, 14f);
            float roll = m.Wrecked ? 28f : Mathf.Clamp(-side * 1.2f, -12f, 12f);
            v.Root.transform.SetPositionAndRotation(at, Quaternion.Euler(pitch, yaw, roll));
        }

        private void Sound(View v, float dt)
        {
            if (EngineClip == null) return;
            if (v.Engine == null) v.Engine = Audio.GameAudio.Attach(v.Root, EngineClip, 0f, 110f);
            float speed = Mathf.Sqrt(v.Copter.Vx * v.Copter.Vx + v.Copter.Vz * v.Copter.Vz);
            float share = Mathf.Clamp01(speed / Oxfall.Core.Rules.Flying.MaxSpeed);
            v.Engine.volume = Mathf.MoveTowards(v.Engine.volume, v.Spin * (0.45f + 0.35f * share), dt * 1.5f);
            v.Engine.pitch = 0.6f + 0.9f * v.Spin + 0.35f * share;
        }

        /// <summary>For tests: the nearest copter to a point, or null.</summary>
        public Minicopter Nearest(Vector3 at)
        {
            Minicopter best = null;
            float bestD = float.MaxValue;
            foreach (var v in _views.Values)
            {
                float d = (v.Copter.X - at.x) * (v.Copter.X - at.x) + (v.Copter.Z - at.z) * (v.Copter.Z - at.z);
                if (d < bestD) { bestD = d; best = v.Copter; }
            }
            return best;
        }
    }
}
