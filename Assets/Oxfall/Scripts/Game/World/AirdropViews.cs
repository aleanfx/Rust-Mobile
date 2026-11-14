using System;
using System.Collections.Generic;
using Oxfall.Core.World;
using Oxfall.Game.Audio;
using Oxfall.Game.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>
    /// Supply drops as everyone sees them: the cargo plane droning across the sky, the crate falling
    /// from it and swinging under an orange and white parachute, and, once it is down, red smoke
    /// rising from it for a few minutes so it can be found. The crate itself, once landed, is a
    /// deployable like any other (drawn by DeployableViews); this only draws it on its way down.
    /// Times are on this machine's clock (Time.time).
    /// </summary>
    public sealed class AirdropViews
    {
        /// <summary>How long a landed crate smokes.</summary>
        public const float SmokeSeconds = 240f;

        private sealed class View
        {
            public Airdrop Drop;
            public Transform Plane;
            public Transform Falling;
            public Transform Chute;
            public float SmokeUntil;
            public float NextPuff;
            public bool Landed;
        }

        private static Mesh _plane, _chute, _crate;

        private readonly MaterialLibrary _materials;
        private readonly SoundBank _bank;
        private readonly Explosions _explosions;
        private readonly Transform _parent;
        private readonly List<View> _views = new List<View>();

        /// <summary>A plane set off (for the HUD to say so).</summary>
        public event Action<Airdrop> Started;

        public AirdropViews(MaterialLibrary materials, SoundBank bank, Explosions explosions)
        {
            _materials = materials;
            _bank = bank;
            _explosions = explosions;
            _parent = new GameObject("Supply drops").transform;
        }

        public int Count => _views.Count;

        /// <summary>A plane on its way (already some way along if it set off a while ago).</summary>
        public void Add(Airdrop drop)
        {
            foreach (var v in _views)
                if (v.Drop.Id == drop.Id) return;
            _plane ??= MeshFactory.CargoPlane();
            _chute ??= MeshFactory.Parachute();
            _crate ??= MeshFactory.AirdropCrate();
            var view = new View { Drop = drop, SmokeUntil = drop.LandTime + SmokeSeconds };

            if (Time.time < drop.EndTime)
            {
                var plane = new GameObject("Cargo plane " + drop.Id);
                plane.transform.SetParent(_parent, false);
                plane.AddComponent<MeshFilter>().sharedMesh = _plane;
                var r = plane.AddComponent<MeshRenderer>();
                r.sharedMaterials = new[] { _materials.PlanePaint, _materials.Steel };
                r.shadowCastingMode = ShadowCastingMode.Off;
                // Its drone, heard across the island.
                if (_bank != null) GameAudio.Attach(plane, _bank.PlaneLoop, 1f, 700f).minDistance = 60f;
                view.Plane = plane.transform;
            }

            if (Time.time < drop.LandTime)
            {
                var falling = new GameObject("Supply crate falling " + drop.Id).transform;
                falling.SetParent(_parent, false);
                Part(falling, "Crate", _crate, _materials.DropPaint, _materials.Steel, _materials.ChuteOrange, _materials.WhitePaint);
                view.Chute = Part(falling, "Parachute", _chute, _materials.ChuteOrange, _materials.ChuteWhite, _materials.Rope);
                // Its lines meet on the crate's lid.
                view.Chute.localPosition = new Vector3(0f, 1.12f, 0f);
                falling.gameObject.SetActive(false);
                view.Falling = falling;
            }
            else view.Landed = true;
            _views.Add(view);
            Place(view, Time.time);
            Started?.Invoke(drop);
            Debug.Log($"[Oxfall] Supply plane {drop.Id}: crate down at ({drop.DropX:0}, {drop.DropZ:0}) in {drop.LandTime - Time.time:0} s");
        }

        public void Clear()
        {
            foreach (var v in _views) Remove(v);
            _views.Clear();
        }

        /// <summary>Where the nearest drop is now: its plane until it lets go, then the crate coming down, then the crate on the ground (for tests).</summary>
        public bool TryNearest(Vector3 from, out Vector3 at)
        {
            at = default;
            float best = float.MaxValue;
            foreach (var v in _views)
            {
                // The crate where it is now if it is on its way down; the plane, if it has yet to drop it.
                var p = v.Falling != null && v.Falling.gameObject.activeSelf ? v.Falling.position
                    : !v.Landed && v.Plane != null ? v.Plane.position
                    : new Vector3(v.Drop.DropX, v.Drop.GroundY, v.Drop.DropZ);
                float d = (p - from).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    at = p;
                }
            }
            return best < float.MaxValue;
        }

        public void Tick(float dt)
        {
            float now = Time.time;
            for (int i = _views.Count - 1; i >= 0; i--)
            {
                var v = _views[i];
                Place(v, now);
                if (v.Landed && now >= v.NextPuff && now < v.SmokeUntil)
                {
                    v.NextPuff = now + 0.3f;
                    var wind = new Vector3(0.7f, 0f, 0.35f);
                    _explosions?.SignalSmoke(new Vector3(v.Drop.DropX, v.Drop.GroundY + 1.2f, v.Drop.DropZ), wind);
                }
                if (now >= v.SmokeUntil && now >= v.Drop.EndTime)
                {
                    Remove(v);
                    _views.RemoveAt(i);
                }
            }
        }

        private void Place(View v, float now)
        {
            var d = v.Drop;
            if (v.Plane != null)
            {
                if (now >= d.EndTime)
                {
                    UnityEngine.Object.Destroy(v.Plane.gameObject);
                    v.Plane = null;
                }
                else
                {
                    d.PlaneAt(now, out float x, out float z);
                    // A gentle bank and bob: it is flying, not sliding.
                    float roll = Mathf.Sin(now * 0.3f + d.Id) * 3f;
                    v.Plane.SetPositionAndRotation(new Vector3(x, Airdrops.Height + Mathf.Sin(now * 0.5f) * 0.6f, z),
                        Quaternion.Euler(0f, d.Yaw, roll));
                }
            }
            if (v.Falling == null) return;
            if (now >= d.LandTime)
            {
                // Down: the deployable takes over from here; the smoke starts.
                UnityEngine.Object.Destroy(v.Falling.gameObject);
                v.Falling = null;
                v.Landed = true;
                Debug.Log($"[Oxfall] Supply crate {d.Id} down at ({d.DropX:0}, {d.GroundY:0.0}, {d.DropZ:0})");
                return;
            }
            bool dropped = now >= d.DropTime;
            if (v.Falling.gameObject.activeSelf != dropped) v.Falling.gameObject.SetActive(dropped);
            if (!dropped) return;
            float y = d.CrateHeight(now);
            // The canopy opens a moment after it leaves the plane, and it sways as it comes down.
            float open = Mathf.Clamp01((now - d.DropTime) / 1.2f);
            v.Chute.localScale = new Vector3(Mathf.Lerp(0.15f, 1f, open), Mathf.Lerp(0.6f, 1f, open), Mathf.Lerp(0.15f, 1f, open));
            float sway = Mathf.Sin(now * 1.1f + d.Id) * 6f * open, swayZ = Mathf.Cos(now * 0.8f + d.Id) * 4f * open;
            v.Falling.SetPositionAndRotation(new Vector3(d.DropX, y, d.DropZ), Quaternion.Euler(swayZ, d.Yaw, sway));
        }

        private static void Remove(View v)
        {
            if (v.Plane != null) UnityEngine.Object.Destroy(v.Plane.gameObject);
            if (v.Falling != null) UnityEngine.Object.Destroy(v.Falling.gameObject);
        }

        private static Transform Part(Transform parent, string name, Mesh mesh, params Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
