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
    /// The horses on screen. Alone, this keeps them (they graze when nobody rides them); online, it
    /// shows where the server says they are and eases between updates. The one this player rides
    /// is moved here, by the shared rules, and sent to the server.
    /// </summary>
    public sealed class HorseHerd
    {
        private sealed class View
        {
            public Horse Horse;
            public GameObject Root;
            public Transform Figure, Neck;
            public Transform[] Legs;
            public Collider Solid;
            public Vector3 Target;
            public float TargetYaw;
            public float Phase;
            public float Gait;
        }

        private readonly IslandGround _ground;
        private readonly Transform _parent;
        private readonly Mesh _body, _leg;
        private readonly Material[] _materials;
        private readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        private readonly System.Random _random = new System.Random(5);

        /// <summary>The horse this player rides (null: none).</summary>
        public Horse Riding { get; private set; }

        /// <summary>Online: the server says this player now rides this horse, or no longer does (null).</summary>
        public event Action<Horse> RidingChanged;

        /// <summary>A hoof hit the ground, at a pace (0..1) and where: for its clop.</summary>
        public event Action<Vector3, float> Hoof;

        public HorseHerd(IslandGround ground, MaterialLibrary materials, bool alone, Func<float, float, bool> blocked, int seed)
        {
            _ground = ground;
            _parent = new GameObject("Horses").transform;
            _body = MeshFactory.HorseBody();
            _leg = MeshFactory.HorseLeg();
            _materials = new[] { materials.HorseCoat, materials.HorseMane, materials.Saddle };
            if (alone)
                foreach (var h in Oxfall.Core.Rules.Riding.Spawn(ground, seed, blocked)) Add(h);
        }

        public int Count => _views.Count;

        public IEnumerable<Horse> All
        {
            get { foreach (var v in _views.Values) yield return v.Horse; }
        }

        private View Add(Horse h)
        {
            var go = new GameObject("Horse " + h.Id);
            go.transform.SetParent(_parent, false);
            var figure = new GameObject("Figure").transform;
            figure.SetParent(go.transform, false);
            figure.gameObject.AddComponent<MeshFilter>().sharedMesh = _body;
            figure.gameObject.AddComponent<MeshRenderer>().sharedMaterials = _materials;
            var legs = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                var hip = new GameObject("Leg " + i).transform;
                hip.SetParent(figure, false);
                hip.localPosition = MeshFactory.HorseHips[i];
                var leg = new GameObject("Leg");
                leg.transform.SetParent(hip, false);
                leg.AddComponent<MeshFilter>().sharedMesh = _leg;
                var r = leg.AddComponent<MeshRenderer>();
                r.sharedMaterials = _materials;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                legs[i] = hip;
            }
            // Something to bump into on foot; the rider passes through their own.
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.25f, 0f);
            box.size = new Vector3(0.6f, 0.8f, 1.9f);
            var view = new View { Horse = h, Root = go, Figure = figure, Legs = legs, Solid = box };
            view.Target = new Vector3(h.X, h.Y, h.Z);
            view.TargetYaw = h.Yaw;
            go.transform.SetPositionAndRotation(view.Target, Quaternion.Euler(0f, h.Yaw, 0f));
            _views[h.Id] = view;
            return view;
        }

        /// <summary>The horse nearest the player within reach that they look towards (null: none).</summary>
        public Horse Mountable(Vector3 feet, Vector3 lookForward)
        {
            if (Riding != null) return null;
            Horse best = null;
            float bestD = float.MaxValue;
            var flat = new Vector2(lookForward.x, lookForward.z);
            foreach (var v in _views.Values)
            {
                if (!Oxfall.Core.Rules.Riding.CanMount(v.Horse, feet.x, feet.y, feet.z)) continue;
                var to = new Vector2(v.Horse.X - feet.x, v.Horse.Z - feet.z);
                float d = to.magnitude;
                if (d > 1.4f && flat.sqrMagnitude > 1e-4f && Vector2.Angle(flat, to) > 60f) continue;
                if (d < bestD) { bestD = d; best = v.Horse; }
            }
            return best;
        }

        private const ushort MeAlone = 1;

        /// <summary>Alone: gets on at once, feeding it from this many berries; how many it ate comes back.</summary>
        public bool MountAlone(Horse h, int berries, out int ate)
        {
            ate = 0;
            if (h == null || h.Driver != 0) return false;
            h.Driver = MeAlone;
            h.Speed = 0f;
            ate = Oxfall.Core.Rules.Riding.Feed(h, berries);
            Riding = h;
            Solid(h, false);
            return true;
        }

        private void Solid(Horse h, bool on)
        {
            if (h != null && _views.TryGetValue(h.Id, out var v) && v.Solid != null) v.Solid.enabled = on;
        }

        /// <summary>Alone: gets off beside it; where the rider goes comes back.</summary>
        public Vector3 Leave()
        {
            var h = Riding;
            if (h == null) return Vector3.zero;
            h.Driver = 0;
            h.Speed = 0f;
            Riding = null;
            Solid(h, true);
            Oxfall.Core.Rules.Riding.Landing(h, _ground, out float x, out float y, out float z);
            return new Vector3(x, y, z);
        }

        /// <summary>Stops riding here without asking anyone (online, the server already let go).</summary>
        public void Drop()
        {
            Solid(Riding, true);
            Riding = null;
        }

        /// <summary>Rides one frame; the saddle's position comes back.</summary>
        public Vector3 Ride(Vector2 stick, bool sprint, float lookYaw, float dt)
        {
            var h = Riding;
            if (h == null) return Vector3.zero;
            float ox = h.X, oy = h.Y, oz = h.Z;
            Oxfall.Core.Rules.Riding.Step(h, stick.y, stick.x, sprint, lookYaw, dt, _ground);
            _views.TryGetValue(h.Id, out var v);
            Block(h, ox, oy, oz);
            if (v != null)
            {
                v.Target = new Vector3(h.X, h.Y, h.Z);
                v.TargetYaw = h.Yaw;
                v.Root.transform.SetPositionAndRotation(v.Target, Quaternion.Euler(0f, h.Yaw, 0f));
            }
            Oxfall.Core.Rules.Riding.Seat(h, out float sx, out float sy, out float sz);
            return new Vector3(sx, sy, sz);
        }

        /// <summary>Walls, rocks and trees stop it: if the way it just went runs into something solid, it stays.</summary>
        private void Block(Horse h, float ox, float oy, float oz)
        {
            var from = new Vector3(ox, oy + 1.2f, oz);
            var to = new Vector3(h.X, h.Y + 1.2f, h.Z);
            var way = to - from;
            float length = way.magnitude;
            if (length < 1e-4f) return;
            bool hit = Physics.SphereCast(from, 0.45f, way / length, out var info, length + 0.6f, ~0, QueryTriggerInteraction.Ignore)
                && info.collider.GetComponentInParent<Player.PlayerController>() == null
                && info.point.y > Oxfall.Core.Rules.Riding.Floor(_ground, info.point.x, info.point.z) + 0.4f;
            if (!hit) return;
            h.X = ox;
            h.Y = oy;
            h.Z = oz;
            h.Speed = 0f;
        }

        /// <summary>Online: the horses as the server sees them.</summary>
        public void Apply(IReadOnlyList<HorseSnap> horses, ushort me)
        {
            foreach (var snap in horses)
            {
                if (!_views.TryGetValue(snap.Id, out var v)) v = Add(new Horse(snap.Id, snap.X, snap.Y, snap.Z, snap.Yaw));
                var h = v.Horse;
                h.Driver = snap.Driver;
                h.Stamina = snap.Stamina;
                h.Food = snap.Food;
                bool mine = me != 0 && snap.Driver == me;
                if (mine && Riding != h)
                {
                    h.X = snap.X; h.Y = snap.Y; h.Z = snap.Z; h.Yaw = snap.Yaw;
                    h.Speed = 0f;
                    Riding = h;
                    Solid(h, false);
                    RidingChanged?.Invoke(h);
                }
                else if (!mine && Riding == h)
                {
                    Solid(h, true);
                    Riding = null;
                    RidingChanged?.Invoke(null);
                }
                if (mine)
                {
                    float dx = snap.X - h.X, dz = snap.Z - h.Z;
                    if (dx * dx + dz * dz > 8f * 8f) { h.X = snap.X; h.Y = snap.Y; h.Z = snap.Z; h.Speed = 0f; }
                    continue;
                }
                h.X = snap.X; h.Y = snap.Y; h.Z = snap.Z; h.Yaw = snap.Yaw; h.Speed = snap.Speed;
                v.Target = new Vector3(snap.X, snap.Y, snap.Z);
                v.TargetYaw = snap.Yaw;
            }
        }

        /// <summary>Alone, horses nobody rides graze; every horse eases to its place and its legs move with its pace.</summary>
        public void Tick(float dt, bool alone)
        {
            foreach (var v in _views.Values)
            {
                var h = v.Horse;
                if (alone && h != Riding)
                {
                    Oxfall.Core.Rules.Riding.Graze(h, dt, (float)_random.NextDouble() / Mathf.Max(0.001f, dt * 20f), _ground);
                    v.Target = new Vector3(h.X, h.Y, h.Z);
                    v.TargetYaw = h.Yaw;
                }
                var t = v.Root.transform;
                if (h != Riding)
                {
                    var pos = Vector3.Lerp(t.position, v.Target, Mathf.Min(1f, dt * 8f));
                    if ((pos - v.Target).sqrMagnitude > 400f) pos = v.Target;
                    t.SetPositionAndRotation(pos, Quaternion.Euler(0f, Mathf.LerpAngle(t.eulerAngles.y, v.TargetYaw, Mathf.Min(1f, dt * 6f)), 0f));
                }
                Animate(v, dt);
            }
        }

        /// <summary>Walk, trot or gallop: legs swing by pace, the body rocks, the head dips to graze when it stands.</summary>
        private void Animate(View v, float dt)
        {
            var h = v.Horse;
            v.Gait = Mathf.Lerp(v.Gait, Mathf.Abs(h.Speed), 1f - Mathf.Exp(-6f * dt));
            float stride = v.Gait > Oxfall.Core.Rules.Riding.TrotSpeed + 1f ? 3.2f : 2.2f;
            float before = v.Phase;
            v.Phase += v.Gait / stride * Mathf.PI * dt;
            float amount = Mathf.Clamp01(v.Gait / 2f);
            float swing = Mathf.Lerp(0f, v.Gait > Oxfall.Core.Rules.Riding.TrotSpeed + 1f ? 42f : 28f, amount);
            float s = Mathf.Sin(v.Phase);
            bool gallop = v.Gait > Oxfall.Core.Rules.Riding.TrotSpeed + 1f;
            // A trot moves the diagonal pairs together; a gallop the front pair, then the back.
            float s2 = gallop ? Mathf.Sin(v.Phase - 0.9f) : -s;
            v.Legs[0].localRotation = Quaternion.Euler(s * swing, 0f, 0f);
            v.Legs[3].localRotation = Quaternion.Euler((gallop ? s2 : s) * swing, 0f, 0f);
            v.Legs[1].localRotation = Quaternion.Euler((gallop ? s * 0.8f : -s) * swing, 0f, 0f);
            v.Legs[2].localRotation = Quaternion.Euler((gallop ? s2 * 0.8f : -s) * swing, 0f, 0f);
            float rock = gallop ? Mathf.Sin(v.Phase * 2f) * 3f * amount : 0f;
            float bob = Mathf.Abs(Mathf.Cos(v.Phase)) * 0.05f * amount;
            v.Figure.localPosition = new Vector3(0f, bob, 0f);
            v.Figure.localRotation = Quaternion.Euler(rock, 0f, 0f);
            // A clop each time a front hoof comes down.
            if (amount > 0.1f && Mathf.Floor(before / Mathf.PI) != Mathf.Floor(v.Phase / Mathf.PI))
                Hoof?.Invoke(v.Root.transform.position, Mathf.Clamp01(v.Gait / Oxfall.Core.Rules.Riding.GallopSpeed));
        }

        /// <summary>For tests: the nearest horse to a point, or null.</summary>
        public Horse Nearest(Vector3 at)
        {
            Horse best = null;
            float bestD = float.MaxValue;
            foreach (var v in _views.Values)
            {
                float d = (v.Horse.X - at.x) * (v.Horse.X - at.x) + (v.Horse.Z - at.z) * (v.Horse.Z - at.z);
                if (d < bestD) { bestD = d; best = v.Horse; }
            }
            return best;
        }
    }
}
