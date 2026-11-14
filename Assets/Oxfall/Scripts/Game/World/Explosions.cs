using System.Collections.Generic;
using Oxfall.Core.Rules;
using Oxfall.Game.Audio;
using Oxfall.Game.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>
    /// Satchel charges as they are seen and heard: the throw's arc, the charge stuck to a wall with its
    /// fuse spitting sparks, and the blast: a flash, a fireball, a column of smoke, embers, dust and
    /// rubble, a boom that carries across the island and a jolt of the camera for those close by.
    /// When a piece of a base comes down to a blast, its rubble flies, made of what it was made of.
    /// </summary>
    public sealed class Explosions : MonoBehaviour
    {
        private const int MaxPuffs = 120;
        private const int MaxChunks = 90;

        private sealed class ChargeView
        {
            public GameObject Root;
            public Transform Spark;
            public MeshRenderer SparkRenderer;
            public Light Light;
            public float ShowAt;
            public float GoesOffAt;
        }

        private sealed class Arc
        {
            public GameObject Go;
            public Vector3 From, To;
            public Quaternion Landed;
            public float Start, End;
            public bool Claimed;
        }

        private sealed class Puff
        {
            public Transform T;
            public MeshRenderer R;
            public Vector3 Velocity;
            public float Age, Life, Delay, Size0, Size1, Drag, Rise, Gravity, Seed;
            public Color C0, C1;
            public bool Fire;
        }

        private sealed class Chunk
        {
            public Transform T;
            public MeshRenderer R;
            public Vector3 Velocity, Spin;
            public float Age, Life, Size;
            public bool Resting;
        }

        private readonly Dictionary<int, ChargeView> _charges = new Dictionary<int, ChargeView>();
        private readonly List<Arc> _arcs = new List<Arc>();
        private readonly List<Puff> _puffs = new List<Puff>();
        private readonly Stack<Puff> _freePuffs = new Stack<Puff>();
        private readonly List<Chunk> _chunks = new List<Chunk>();
        private readonly Stack<Chunk> _freeChunks = new Stack<Chunk>();
        private readonly List<(Light Light, float Age, float Peak)> _flashes = new List<(Light, float, float)>();
        private readonly List<(Vector3 At, float Time)> _recent = new List<(Vector3, float)>();
        private readonly List<int> _scratch = new List<int>();
        private MaterialPropertyBlock _block;

        private Mesh _satchel;
        private Material[] _satchelMaterials;
        private Material _fireball, _smoke, _char;
        private bool _puffsReal;
        private Mesh _card;
        private Mesh[] _rubble;
        private SoundBank _bank;
        private Transform _camera;
        private System.Func<float, float, float> _ground;
        private float _shake;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");

        /// <summary>How many charges are stuck somewhere now, fizzing.</summary>
        public int Live => _charges.Count;

        /// <summary>Seconds since the last blast anywhere (a large number if none yet).</summary>
        public float SinceLastBlast => _recent.Count > 0 ? Time.time - _recent[_recent.Count - 1].Time : 1e6f;

        public static Explosions Create(MaterialLibrary materials, SoundBank bank, Transform camera, System.Func<float, float, float> ground)
        {
            var go = new GameObject("Explosions");
            var e = go.AddComponent<Explosions>();
            e._satchel = MeshFactory.SatchelCharge();
            e._satchelMaterials = new[] { materials.Satchel, materials.Rope, materials.Metal, materials.Char };
            e._fireball = materials.Fireball;
            e._smoke = materials.Smoke;
            e._char = materials.Char;
            e._puffsReal = materials.HasPuffs;
            e._card = MeshFactory.PuffCard();
            e._rubble = new[] { MeshFactory.Rubble(3), MeshFactory.Rubble(8), MeshFactory.Rubble(15) };
            e._bank = bank;
            e._camera = camera;
            e._ground = ground;
            e._block = new MaterialPropertyBlock();
            return e;
        }

        // ---- The throw and the charge

        /// <summary>This player's throw: the charge flies from the hand to where it will stick.</summary>
        public void Throw(Vector3 from, Vector3 to, Vector3 normal)
        {
            var go = NewSatchel("Thrown charge");
            go.transform.position = from;
            float distance = Vector3.Distance(from, to);
            _arcs.Add(new Arc
            {
                Go = go,
                From = from,
                To = to,
                Landed = Facing(normal),
                Start = Time.time,
                End = Time.time + Mathf.Lerp(0.18f, 0.42f, Mathf.Clamp01(distance / Explosives.ThrowReach)),
            });
        }

        /// <summary>
        /// A charge stuck somewhere, its fuse lit, <paramref name="secondsLeft"/> to go. The thrower's
        /// own appears where their throw lands, when it lands.
        /// </summary>
        public void Arm(LiveCharge c, float secondsLeft, bool mine)
        {
            if (_charges.ContainsKey(c.Id)) return;
            var root = NewSatchel("Charge " + c.Id);
            var normal = new Vector3(c.NX, c.NY, c.NZ);
            root.transform.SetPositionAndRotation(new Vector3(c.X, c.Y, c.Z) + normal * 0.01f, Facing(normal));
            var view = new ChargeView { Root = root, GoesOffAt = Time.time + secondsLeft, ShowAt = Time.time };
            if (mine)
            {
                // The oldest throw still flying (or landed and waiting) is this one.
                foreach (var arc in _arcs)
                {
                    if (arc.Claimed) continue;
                    arc.Claimed = true;
                    view.ShowAt = arc.End;
                    break;
                }
            }

            // The fuse: a spark at its end, a little orange light, and its hiss.
            var spark = new GameObject("Spark");
            spark.transform.SetParent(root.transform, false);
            spark.transform.localPosition = MeshFactory.SatchelFuseTip;
            spark.transform.localScale = Vector3.one * 0.09f;
            spark.AddComponent<MeshFilter>().sharedMesh = _card;
            var sparkRenderer = spark.AddComponent<MeshRenderer>();
            sparkRenderer.sharedMaterial = _fireball;
            sparkRenderer.shadowCastingMode = ShadowCastingMode.Off;
            sparkRenderer.receiveShadows = false;
            var lightObject = new GameObject("Fuse light");
            lightObject.transform.SetParent(spark.transform, false);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.range = 3f;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            view.Spark = spark.transform;
            view.SparkRenderer = sparkRenderer;
            view.Light = light;
            if (_bank != null) GameAudio.Attach(root, _bank.FuseLoop, 0.55f, 14f);
            root.SetActive(view.ShowAt <= Time.time);
            _charges[c.Id] = view;
        }

        // ---- The blast

        /// <summary>A charge goes off at <paramref name="at"/> (its own view, if any, goes with it).</summary>
        public void Blast(int id, Vector3 at)
        {
            if (_charges.TryGetValue(id, out var view))
            {
                Destroy(view.Root);
                _charges.Remove(id);
            }
            _recent.Add((at, Time.time));
            if (_recent.Count > 8) _recent.RemoveAt(0);

            // The flash: brief, bright, warm.
            var flash = new GameObject("Blast flash").AddComponent<Light>();
            flash.transform.position = at + Vector3.up * 0.5f;
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.72f, 0.42f);
            flash.range = 22f;
            flash.intensity = 9f;
            flash.shadows = LightShadows.None;
            _flashes.Add((flash, 0f, 9f));

            if (_puffsReal)
            {
                // The fireball: a few hot puffs that swell and redden as they go out.
                for (int i = 0; i < 8; i++)
                {
                    var dir = Random.onUnitSphere;
                    dir.y = Mathf.Abs(dir.y) * 0.8f + 0.2f;
                    Spawn(true, at + dir * Random.Range(0f, 0.4f), dir * Random.Range(2f, 4.5f), Random.Range(0.4f, 0.7f),
                        Random.Range(0.6f, 1f), Random.Range(2.2f, 3.4f),
                        new Color(1f, 0.82f, 0.5f, 1f), new Color(0.85f, 0.22f, 0.04f, 0f), 4f, 1.2f, 0f, 0f);
                }
                // The smoke: dark at first, paling and spreading as it climbs.
                for (int i = 0; i < 14; i++)
                {
                    var dir = Random.onUnitSphere;
                    dir.y = Mathf.Abs(dir.y);
                    Spawn(false, at + dir * Random.Range(0.1f, 0.8f), dir * Random.Range(1.2f, 3.2f) + Vector3.up * Random.Range(0.6f, 1.6f),
                        Random.Range(3.5f, 6.5f), Random.Range(1.2f, 1.8f), Random.Range(4f, 6.5f),
                        new Color(0.13f, 0.12f, 0.11f, 0.9f), new Color(0.38f, 0.37f, 0.35f, 0f), 1.4f, 0.55f, 0f, Random.Range(0f, 0.15f));
                }
                // Dust kicked up along the ground.
                for (int i = 0; i < 7; i++)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a));
                    Spawn(false, at + dir * 0.5f + Vector3.down * 0.4f, dir * Random.Range(3f, 5.5f), Random.Range(2f, 3.2f),
                        Random.Range(0.8f, 1.2f), Random.Range(2.5f, 3.8f),
                        new Color(0.42f, 0.37f, 0.3f, 0.7f), new Color(0.5f, 0.46f, 0.4f, 0f), 2.2f, 0.15f, 0f, 0.05f);
                }
                // Embers: tiny, fast, falling.
                for (int i = 0; i < 16; i++)
                {
                    var dir = Random.onUnitSphere;
                    dir.y = Mathf.Abs(dir.y) + 0.3f;
                    Spawn(true, at, dir.normalized * Random.Range(5f, 11f), Random.Range(0.6f, 1.3f), 0.09f, 0.04f,
                        new Color(1f, 0.75f, 0.35f, 1f), new Color(1f, 0.3f, 0.05f, 0f), 0.6f, 0f, 9f, 0f);
                }
            }
            // Black bits of the charge and whatever was round it.
            for (int i = 0; i < 8; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.4f;
                SpawnChunk(at, dir.normalized * Random.Range(4f, 9f), Random.Range(0.05f, 0.12f), _char);
            }

            if (_bank != null) GameAudio.PlayAt(_bank.Explosion, at, 1f, 8f, 160f, 0.06f);
            if (_camera != null)
            {
                float d = Vector3.Distance(_camera.position, at);
                _shake = Mathf.Max(_shake, Mathf.Clamp01(1f - d / 40f));
            }
        }

        /// <summary>
        /// A piece or a door of a base just came down (its bounds, what it was made of): if a blast went
        /// off beside it a moment ago, it goes in rubble and dust.
        /// </summary>
        public void Fell(Bounds bounds, Material material)
        {
            bool blasted = false;
            Vector3 from = bounds.center;
            foreach (var (at, time) in _recent)
            {
                if (Time.time - time > 1.5f || bounds.SqrDistance(at) > 3.5f * 3.5f) continue;
                blasted = true;
                from = at;
            }
            if (!blasted) return;
            var size = bounds.size;
            int count = Mathf.Clamp(Mathf.RoundToInt((size.x * size.y + size.y * size.z + size.x * size.z) * 1.2f), 8, 18);
            for (int i = 0; i < count; i++)
            {
                var p = bounds.center + Vector3.Scale(new Vector3(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f), size);
                var away = p - from;
                away.y = Mathf.Max(away.y, 0f);
                var velocity = (away.sqrMagnitude > 0.01f ? away.normalized : Random.onUnitSphere) * Random.Range(2.5f, 6.5f)
                               + Vector3.up * Random.Range(1.5f, 4f);
                SpawnChunk(p, velocity, Random.Range(0.12f, 0.32f), material);
            }
            if (!_puffsReal) return;
            for (int i = 0; i < 6; i++)
            {
                var p = bounds.center + Vector3.Scale(new Vector3(Random.value - 0.5f, Random.value * 0.6f - 0.5f, Random.value - 0.5f), size);
                Spawn(false, p, Random.onUnitSphere * 0.8f + Vector3.up * 0.4f, Random.Range(2.5f, 4f), Random.Range(1f, 1.5f),
                    Random.Range(2.6f, 3.6f), new Color(0.45f, 0.42f, 0.38f, 0.75f), new Color(0.52f, 0.5f, 0.46f, 0f), 1.6f, 0.2f, 0f, 0.05f);
            }
        }

        // ---- Each frame

        private void Update()
        {
            float dt = Time.deltaTime, now = Time.time;
            TickArcs(now);
            TickCharges(now);
            TickPuffs(dt);
            TickChunks(dt);
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var (light, age, peak) = _flashes[i];
                age += dt;
                if (age >= 0.7f || light == null)
                {
                    if (light != null) Destroy(light.gameObject);
                    _flashes.RemoveAt(i);
                    continue;
                }
                light.intensity = peak * Mathf.Exp(-age * 6f);
                _flashes[i] = (light, age, peak);
            }
        }

        private void LateUpdate()
        {
            if (_camera == null || _camera.parent == null) return;
            if (_shake <= 0.001f)
            {
                if (_shake > 0f)
                {
                    _shake = 0f;
                    _camera.localRotation = Quaternion.identity;
                    _camera.localPosition = Vector3.zero;
                }
                return;
            }
            float t = Time.time * 32f, s = _shake * _shake;
            _camera.localRotation = Quaternion.Euler((Mathf.PerlinNoise(t, 1.3f) - 0.5f) * 5f * s,
                (Mathf.PerlinNoise(2.7f, t) - 0.5f) * 5f * s, (Mathf.PerlinNoise(t, 7.1f) - 0.5f) * 3f * s);
            _camera.localPosition = new Vector3(Mathf.PerlinNoise(t, 4.4f) - 0.5f, Mathf.PerlinNoise(5.5f, t) - 0.5f, 0f) * 0.12f * s;
            _shake = Mathf.Max(0f, _shake - Time.deltaTime * 1.6f);
        }

        private void TickArcs(float now)
        {
            for (int i = _arcs.Count - 1; i >= 0; i--)
            {
                var arc = _arcs[i];
                float t = Mathf.Clamp01((now - arc.Start) / Mathf.Max(0.01f, arc.End - arc.Start));
                // A lob: a little higher in the middle the farther it goes, tumbling as it flies.
                var p = Vector3.Lerp(arc.From, arc.To, t);
                p.y += Mathf.Sin(t * Mathf.PI) * Mathf.Min(0.8f, Vector3.Distance(arc.From, arc.To) * 0.18f);
                arc.Go.transform.position = p;
                arc.Go.transform.rotation = t < 1f
                    ? Quaternion.Euler(t * 540f, t * 220f, 0f) * Quaternion.Slerp(Quaternion.identity, arc.Landed, t)
                    : arc.Landed;
                // Gone once its charge is shown where it landed; or, if the server never took it, a moment after.
                if ((arc.Claimed && now >= arc.End) || now > arc.End + 1.5f)
                {
                    Destroy(arc.Go);
                    _arcs.RemoveAt(i);
                }
            }
        }

        private void TickCharges(float now)
        {
            foreach (var pair in _charges)
            {
                var view = pair.Value;
                if (!view.Root.activeSelf && now >= view.ShowAt) view.Root.SetActive(true);
                if (!view.Root.activeSelf) continue;
                // Spitting faster as the end nears.
                float left = Mathf.Max(0f, view.GoesOffAt - now);
                float flicker = 0.6f + 0.4f * Mathf.PerlinNoise(now * (14f + 10f / (0.4f + left)), pair.Key);
                view.Spark.localScale = Vector3.one * (0.06f + 0.06f * flicker);
                view.Light.intensity = 0.6f + 1.4f * flicker;
                if (_puffsReal)
                {
                    _block.Clear();
                    _block.SetColor(ColorId, new Color(1f, 0.78f, 0.4f, 0.75f + 0.25f * flicker));
                    _block.SetFloat(SeedId, now * 7f);
                    view.SparkRenderer.SetPropertyBlock(_block);
                }
                // Now and then a spark leaps off.
                if (_puffsReal && Random.value < Time.deltaTime * 9f)
                    Spawn(true, view.Spark.position, Random.onUnitSphere * 1.6f + Vector3.up, 0.35f, 0.05f, 0.02f,
                        new Color(1f, 0.8f, 0.4f, 1f), new Color(1f, 0.35f, 0.05f, 0f), 0.4f, 0f, 6f, 0f);
            }
            // A charge whose blast never came (the connection dropped) is cleared a while after.
            _scratch.Clear();
            foreach (var pair in _charges)
                if (now > pair.Value.GoesOffAt + 4f) _scratch.Add(pair.Key);
            foreach (int id in _scratch)
            {
                Destroy(_charges[id].Root);
                _charges.Remove(id);
            }
        }

        private void TickPuffs(float dt)
        {
            for (int i = _puffs.Count - 1; i >= 0; i--)
            {
                var p = _puffs[i];
                if (p.Delay > 0f)
                {
                    p.Delay -= dt;
                    p.R.enabled = p.Delay <= 0f;
                    continue;
                }
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    p.R.enabled = false;
                    _puffs.RemoveAt(i);
                    _freePuffs.Push(p);
                    continue;
                }
                float t = p.Age / p.Life;
                p.Velocity *= Mathf.Exp(-p.Drag * dt);
                p.Velocity += Vector3.up * (p.Rise - p.Gravity) * dt;
                p.T.position += p.Velocity * dt;
                // Swells fast at first, slowly after.
                float grow = 1f - (1f - t) * (1f - t);
                p.T.localScale = Vector3.one * Mathf.Lerp(p.Size0, p.Size1, grow);
                var c = Color.Lerp(p.C0, p.C1, p.Fire ? t : t * t);
                // Smoke fades in over its first moments rather than popping in.
                if (!p.Fire) c.a *= Mathf.Clamp01(p.Age / 0.15f);
                _block.Clear();
                _block.SetColor(ColorId, c);
                _block.SetFloat(SeedId, p.Seed);
                p.R.SetPropertyBlock(_block);
            }
        }

        private void TickChunks(float dt)
        {
            for (int i = _chunks.Count - 1; i >= 0; i--)
            {
                var c = _chunks[i];
                c.Age += dt;
                if (c.Age >= c.Life)
                {
                    c.R.enabled = false;
                    _chunks.RemoveAt(i);
                    _freeChunks.Push(c);
                    continue;
                }
                if (!c.Resting)
                {
                    c.Velocity += Vector3.down * 14f * dt;
                    var p = c.T.position + c.Velocity * dt;
                    float floor = _ground != null ? _ground(p.x, p.z) + c.Size * 0.4f : float.NegativeInfinity;
                    if (p.y < floor)
                    {
                        p.y = floor;
                        // A bounce that loses most of its energy, then it lies still.
                        c.Velocity = new Vector3(c.Velocity.x * 0.45f, -c.Velocity.y * 0.3f, c.Velocity.z * 0.45f);
                        c.Spin *= 0.5f;
                        if (c.Velocity.sqrMagnitude < 1.2f) c.Resting = true;
                    }
                    c.T.position = p;
                    c.T.rotation *= Quaternion.Euler(c.Spin * dt);
                }
                // Sinks away at the end.
                float sink = Mathf.Clamp01((c.Age - (c.Life - 1.5f)) / 1.5f);
                if (sink > 0f) c.T.localScale = Vector3.one * c.Size * (1f - sink);
            }
        }

        // ---- Pools

        private void Spawn(bool fire, Vector3 at, Vector3 velocity, float life, float size0, float size1, Color c0, Color c1,
            float drag, float rise, float gravity, float delay)
        {
            Puff p;
            if (_freePuffs.Count > 0) p = _freePuffs.Pop();
            else if (_puffs.Count + _freePuffs.Count < MaxPuffs)
            {
                var go = new GameObject("Puff");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _card;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                p = new Puff { T = go.transform, R = r };
            }
            else return;
            p.R.sharedMaterial = fire ? _fireball : _smoke;
            p.Fire = fire;
            p.T.position = at;
            p.T.localScale = Vector3.one * size0;
            p.Velocity = velocity;
            p.Age = 0f;
            p.Life = life;
            p.Delay = delay;
            p.Size0 = size0;
            p.Size1 = size1;
            p.C0 = c0;
            p.C1 = c1;
            p.Drag = drag;
            p.Rise = rise;
            p.Gravity = gravity;
            p.Seed = Random.value * 50f;
            p.R.enabled = delay <= 0f;
            _block.Clear();
            _block.SetColor(ColorId, new Color(c0.r, c0.g, c0.b, fire ? c0.a : 0f));
            _block.SetFloat(SeedId, p.Seed);
            p.R.SetPropertyBlock(_block);
            _puffs.Add(p);
        }

        private void SpawnChunk(Vector3 at, Vector3 velocity, float size, Material material)
        {
            Chunk c;
            if (_freeChunks.Count > 0) c = _freeChunks.Pop();
            else if (_chunks.Count + _freeChunks.Count < MaxChunks)
            {
                var go = new GameObject("Rubble");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _rubble[(_chunks.Count + _freeChunks.Count) % _rubble.Length];
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                c = new Chunk { T = go.transform, R = r };
            }
            else
            {
                // The oldest piece of rubble makes way.
                c = _chunks[0];
                _chunks.RemoveAt(0);
            }
            c.R.sharedMaterial = material != null ? material : _char;
            c.R.enabled = true;
            c.T.SetPositionAndRotation(at, Random.rotation);
            c.T.localScale = Vector3.one * size;
            c.Size = size;
            c.Velocity = velocity;
            c.Spin = Random.insideUnitSphere * 720f;
            c.Age = 0f;
            c.Life = Random.Range(6f, 9f);
            c.Resting = false;
            _chunks.Add(c);
        }

        private GameObject NewSatchel(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = _satchel;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = _satchelMaterials;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A charge's back against a surface facing this way, its top as near up as that allows.</summary>
        private static Quaternion Facing(Vector3 normal)
        {
            if (normal.sqrMagnitude < 1e-4f) normal = Vector3.up;
            normal.Normalize();
            var up = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(normal, up);
        }
    }
}
