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
    /// The rowboats on screen. Alone, this keeps the boats (they drift to a stop when nobody rows);
    /// online, it shows where the server says they are and moves them smoothly between updates.
    /// The boat this player rows is moved here, by the shared rules, and sent to the server.
    /// </summary>
    public sealed class BoatFleet
    {
        private sealed class View
        {
            public Boat Boat;
            public GameObject Root;
            public AudioSource Engine;
            public Vector3 Target;
            public float TargetYaw;
        }

        private readonly IslandGround _ground;
        private readonly Transform _parent;
        private readonly Mesh _rowMesh, _motorMesh;
        private readonly Material[] _materials, _motorMaterials;
        private readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        private float _bob;

        /// <summary>The boat this player rows (null: none).</summary>
        public Boat Rowing { get; private set; }

        /// <summary>Online: the server says this player now rows this boat, or no longer does (null).</summary>
        public event Action<Boat> RowingChanged;

        /// <summary>
        /// The boats' floor relative to the water line (negative: above it). Kept a little above, or
        /// the sea's surface would show through inside the hull.
        /// </summary>
        public const float FloorDepth = -0.08f;

        /// <summary>The motor's sound; set once the game's sounds are made, then every motorboat has one.</summary>
        public AudioClip EngineClip { get; set; }

        public BoatFleet(IslandGround ground, MaterialLibrary materials, bool alone)
        {
            _ground = ground;
            _parent = new GameObject("Boats").transform;
            _rowMesh = MeshFactory.Rowboat();
            _motorMesh = MeshFactory.Rowboat(true);
            _materials = new[] { materials.WoodWall, materials.Planks, materials.Metal };
            _motorMaterials = new[] { materials.WoodWall, materials.Planks, materials.Metal, materials.BarrelPaint[1] };
            if (alone)
                foreach (var b in Boating.Spawn(ground)) Add(b);
        }

        public int Count => _views.Count;

        public IEnumerable<Boat> All
        {
            get { foreach (var v in _views.Values) yield return v.Boat; }
        }

        private View Add(Boat boat)
        {
            bool motor = boat.Kind == BoatKind.Motorboat;
            var go = new GameObject((motor ? "Motorboat " : "Rowboat ") + boat.Id);
            go.transform.SetParent(_parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = motor ? _motorMesh : _rowMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = motor ? _motorMaterials : _materials;
            var view = new View { Boat = boat, Root = go };
            view.Target = new Vector3(boat.X, IslandGround.WaterLevel - FloorDepth, boat.Z);
            view.TargetYaw = boat.Yaw;
            go.transform.SetPositionAndRotation(view.Target, Quaternion.Euler(0f, boat.Yaw, 0f));
            _views[boat.Id] = view;
            return view;
        }

        /// <summary>The empty boat nearest the player within reach that they look towards (null: none).</summary>
        public Boat Boardable(Vector3 feet, Vector3 lookForward)
        {
            if (Rowing != null) return null;
            Boat best = null;
            float bestD = float.MaxValue;
            var flat = new Vector2(lookForward.x, lookForward.z);
            foreach (var v in _views.Values)
            {
                if (!Boating.CanBoard(v.Boat, feet.x, feet.y, feet.z)) continue;
                var to = new Vector2(v.Boat.X - feet.x, v.Boat.Z - feet.z);
                float d = to.magnitude;
                // Standing right by it, any look will do; further, roughly towards it.
                if (d > 1.6f && flat.sqrMagnitude > 1e-4f && Vector2.Angle(flat, to) > 50f) continue;
                if (d < bestD) { bestD = d; best = v.Boat; }
            }
            return best;
        }

        /// <summary>Alone: gets in at once. Online the server is asked, and says so with the boats it sends.</summary>
        public bool BoardAlone(Boat boat)
        {
            if (boat == null || boat.Driver != 0) return false;
            boat.Driver = _meAlone;
            Rowing = boat;
            return true;
        }

        private const ushort _meAlone = 1;

        /// <summary>Alone: gets out. The landing spot is where the player goes.</summary>
        public Vector3 Leave()
        {
            var boat = Rowing;
            if (boat == null) return Vector3.zero;
            boat.Driver = 0;
            Rowing = null;
            Boating.Landing(boat, _ground, out float x, out float y, out float z);
            return new Vector3(x, y, z);
        }

        /// <summary>Stops rowing here without asking anyone (online, the server already let go).</summary>
        public void Drop() => Rowing = null;

        /// <summary>Rows one frame; the seat's position and the boat's turn this frame come back.</summary>
        public Vector3 Row(float throttle, float steer, float dt, out float turned)
        {
            turned = 0f;
            if (Rowing == null) return Vector3.zero;
            float before = Rowing.Yaw;
            Boating.Step(Rowing, throttle, steer, dt, _ground);
            turned = Mathf.DeltaAngle(before, Rowing.Yaw);
            if (_views.TryGetValue(Rowing.Id, out var v))
            {
                v.Target = new Vector3(Rowing.X, IslandGround.WaterLevel - FloorDepth, Rowing.Z);
                v.TargetYaw = Rowing.Yaw;
                v.Root.transform.SetPositionAndRotation(v.Target + Vector3.up * Bob(Rowing.Id), Quaternion.Euler(Pitch(Rowing), Rowing.Yaw, Roll(Rowing.Id)));
            }
            Boating.Seat(Rowing, out float sx, out float sy, out float sz);
            return new Vector3(sx, sy, sz);
        }

        /// <summary>Online: the boats as the server sees them.</summary>
        public void Apply(IReadOnlyList<BoatSnap> boats, ushort me)
        {
            foreach (var snap in boats)
            {
                if (!_views.TryGetValue(snap.Id, out var v) || v.Boat.Kind != snap.Kind)
                {
                    if (v != null) UnityEngine.Object.Destroy(v.Root);
                    v = Add(new Boat(snap.Id, snap.X, snap.Z, snap.Yaw, snap.Kind));
                }
                var b = v.Boat;
                b.Driver = snap.Driver;
                // The server keeps the tank; between its updates the driver's motor burns it here too.
                b.Fuel = snap.Fuel;
                bool mine = me != 0 && snap.Driver == me;
                if (mine && Rowing != b)
                {
                    // The server let this player in: rowing starts from where it says the boat is.
                    b.X = snap.X; b.Z = snap.Z; b.Yaw = snap.Yaw; b.Speed = 0f;
                    Rowing = b;
                    RowingChanged?.Invoke(b);
                }
                else if (!mine && Rowing == b)
                {
                    Rowing = null;
                    RowingChanged?.Invoke(null);
                }
                if (mine)
                {
                    // Rowed here; only pulled back if the server would not believe where it went.
                    float dx = snap.X - b.X, dz = snap.Z - b.Z;
                    if (dx * dx + dz * dz > 6f * 6f) { b.X = snap.X; b.Z = snap.Z; b.Yaw = snap.Yaw; b.Speed = 0f; }
                    continue;
                }
                b.X = snap.X; b.Z = snap.Z; b.Yaw = snap.Yaw; b.Speed = snap.Speed;
                v.Target = new Vector3(snap.X, IslandGround.WaterLevel - FloorDepth, snap.Z);
                v.TargetYaw = snap.Yaw;
            }
        }

        /// <summary>Alone, boats nobody rows drift to a stop; every boat bobs on the swell and eases to its place.</summary>
        public void Tick(float dt, bool alone)
        {
            _bob += dt;
            foreach (var v in _views.Values)
            {
                Sound(v, dt);
                if (v.Boat == Rowing) continue;
                if (alone && v.Boat.Speed != 0f)
                {
                    Boating.Step(v.Boat, 0f, 0f, dt, _ground);
                    v.Target = new Vector3(v.Boat.X, IslandGround.WaterLevel - FloorDepth, v.Boat.Z);
                    v.TargetYaw = v.Boat.Yaw;
                }
                else if (!alone)
                {
                    // Between updates, carried on at the speed it had.
                    float rad = v.TargetYaw * Mathf.Deg2Rad;
                    v.Target += new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * v.Boat.Speed * dt;
                }
                var t = v.Root.transform;
                var flat = new Vector3(t.position.x, v.Target.y, t.position.z);
                var pos = Vector3.Lerp(flat, v.Target, Mathf.Min(1f, dt * 8f));
                if ((pos - v.Target).sqrMagnitude > 100f) pos = v.Target;
                float yaw = Mathf.LerpAngle(t.eulerAngles.y, v.TargetYaw, Mathf.Min(1f, dt * 6f));
                t.SetPositionAndRotation(pos + Vector3.up * Bob(v.Boat.Id), Quaternion.Euler(Pitch(v.Boat), yaw, Roll(v.Boat.Id)));
            }
        }

        /// <summary>A motorboat's engine: idling while someone sits at the tiller, louder and higher with speed, quiet dry or empty.</summary>
        private void Sound(View v, float dt)
        {
            if (v.Boat.Kind != BoatKind.Motorboat || EngineClip == null) return;
            if (v.Engine == null) v.Engine = Audio.GameAudio.Attach(v.Root, EngineClip, 0f, 70f);
            bool running = v.Boat.Driver != 0 && v.Boat.Fuel > 0f;
            float share = Mathf.Clamp01(Mathf.Abs(v.Boat.Speed) / Boating.MotorSpeed);
            float volume = running ? 0.35f + 0.4f * share : 0f;
            v.Engine.volume = Mathf.MoveTowards(v.Engine.volume, volume, dt * 1.5f);
            v.Engine.pitch = Mathf.Lerp(v.Engine.pitch, 0.75f + 0.75f * share, Mathf.Min(1f, dt * 3f));
        }

        private float Bob(int id) => Mathf.Sin(_bob * 1.3f + id * 1.7f) * 0.05f;
        private float Roll(int id) => Mathf.Sin(_bob * 0.9f + id * 2.3f) * 2.2f;
        // The bow lifts a little when it goes fast.
        private static float Pitch(Boat b) => -Mathf.Clamp(b.Speed, 0f, Boating.MotorSpeed) * 0.4f;

        /// <summary>Alone: pours fuel into the motorboat this player drives; how many cans went in.</summary>
        public int RefuelAlone(Boat boat, int cans)
        {
            if (boat == null) return 0;
            int pour = Mathf.Min(cans, Boating.TankSpace(boat));
            if (pour > 0) boat.Fuel += pour;
            return pour;
        }

        /// <summary>For tests: the nearest boat to a point, or null.</summary>
        public Boat Nearest(Vector3 at, BoatKind? kind = null)
        {
            Boat best = null;
            float bestD = float.MaxValue;
            foreach (var v in _views.Values)
            {
                if (kind.HasValue && v.Boat.Kind != kind.Value) continue;
                float d = (v.Boat.X - at.x) * (v.Boat.X - at.x) + (v.Boat.Z - at.z) * (v.Boat.Z - at.z);
                if (d < bestD) { bestD = d; best = v.Boat; }
            }
            return best;
        }
    }
}
