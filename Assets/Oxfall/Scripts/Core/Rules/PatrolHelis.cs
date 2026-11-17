using System;
using System.Collections.Generic;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    /// <summary>What a patrol helicopter is doing.</summary>
    public enum HeliState : byte
    {
        /// <summary>Flying its round of the ruins, looking for anyone in the open.</summary>
        Patrol = 1,
        /// <summary>Circling someone it saw (or who shot at it), firing its gun and its rockets.</summary>
        Engage = 2,
        /// <summary>Lost them: hovering about where it last saw them, looking.</summary>
        Search = 3,
        /// <summary>Its time is up: flying off the island, minding no one.</summary>
        Leaving = 4,
        /// <summary>Shot down: spinning and falling, smoke pouring out.</summary>
        Crashing = 5,
        /// <summary>On the ground, burning; its crates lie round it.</summary>
        Wreck = 6,
    }

    public enum HeliShotKind : byte
    {
        /// <summary>A round of its gun: from the muzzle to where it struck (the target, if it hit someone).</summary>
        Bullet = 1,
        /// <summary>A rocket let go: from its pod to where it will burst.</summary>
        Rocket = 2,
        /// <summary>A rocket burst near someone: whom and how much it took off (before armour).</summary>
        Splash = 3,
    }

    /// <summary>One thing a helicopter fired, or what a rocket did when it burst.</summary>
    public readonly struct HeliShot
    {
        public readonly int Heli;
        public readonly HeliShotKind Kind;
        public readonly float FromX, FromY, FromZ, ToX, ToY, ToZ;
        /// <summary>Whom it hit (a player key), 0 for no one.</summary>
        public readonly int Target;
        /// <summary>What it took off (before armour); 0 for a miss.</summary>
        public readonly float Damage;

        public HeliShot(int heli, HeliShotKind kind, float fromX, float fromY, float fromZ, float toX, float toY, float toZ, int target, float damage)
        {
            Heli = heli;
            Kind = kind;
            FromX = fromX;
            FromY = fromY;
            FromZ = fromZ;
            ToX = toX;
            ToY = toY;
            ToZ = toZ;
            Target = target;
            Damage = damage;
        }
    }

    /// <summary>One patrol helicopter: where it is, which way it points, how it leans, how much it can take, what it is doing.</summary>
    public sealed class PatrolHeli
    {
        public int Id { get; internal set; }
        public float X { get; internal set; }
        public float Y { get; internal set; }
        public float Z { get; internal set; }
        /// <summary>Heading in degrees (0 along +Z).</summary>
        public float Yaw { get; internal set; }
        /// <summary>Nose down (positive) as it speeds up, degrees.</summary>
        public float Pitch { get; internal set; }
        /// <summary>Bank into a turn (positive: right side down), degrees.</summary>
        public float Roll { get; internal set; }
        public float Health { get; internal set; } = PatrolHelis.MaxHealth;
        public HeliState State { get; internal set; } = HeliState.Patrol;
        /// <summary>Whom it is after (a player key), 0 for no one.</summary>
        public int Target { get; internal set; }
        /// <summary>When it came in, and when its state last changed.</summary>
        public float Born { get; internal set; }
        public float Since { get; internal set; }

        /// <summary>Badly hurt: smoke trails from it.</summary>
        public bool Smoking => Health < PatrolHelis.MaxHealth * 0.5f;
        public bool Down => State == HeliState.Crashing || State == HeliState.Wreck;

        internal float Vx, Vy, Vz;
        internal readonly List<(float X, float Z)> Route = new List<(float, float)>();
        internal int Leg;
        internal float CircleUntil, Orbit;
        internal float NextScan, LastSeen, SeenX, SeenY, SeenZ;
        internal bool Sees;
        internal float BurstUntil, NextBurst, NextRound;
        internal float NextVolley, NextRocket;
        internal int RocketsLeft;
        internal int Aggro;
        internal float AggroUntil;
        internal float ExitX, ExitZ;
        internal float Spin;
    }

    /// <summary>
    /// The patrol helicopter, as on the PC game: now and then an armed helicopter comes in over the sea
    /// and makes a round of the ruins for a while. Anyone it sees out in the open it circles and
    /// shoots at, with bursts of its gun and now and then a volley of rockets; walls and roofs hide
    /// you from it. It can be shot down (its rotors are its weak spot): it spins down in smoke and
    /// burns where it falls, with crates of military loot round the wreck, too hot to open for a
    /// couple of minutes. Times are on the clock of whoever runs it (the server's, or the game's alone).
    /// </summary>
    public sealed class PatrolHelis
    {
        public const float MaxHealth = 2000f;
        /// <summary>A round that strikes a rotor counts this many times over.</summary>
        public const float RotorMultiplier = 3f;

        /// <summary>The first some way into a fresh island, then one every <see cref="Every"/> give or take <see cref="Jitter"/>.</summary>
        public const float FirstAfter = 1800f, Every = 7200f, Jitter = 1800f;
        /// <summary>How long it stays over the island before it goes.</summary>
        public const float Lifetime = 900f;

        /// <summary>How high it cruises over the ground, and how high over its target it circles.</summary>
        public const float CruiseHeight = 42f, EngageHeight = 24f, MinClearance = 16f;
        public const float PatrolSpeed = 20f, EngageSpeed = 13f, LeaveSpeed = 32f;
        /// <summary>How long it circles each ruin on its round, and how wide.</summary>
        public const float CircleSeconds = 14f, CircleRadius = 30f;
        /// <summary>How wide it circles someone it is after.</summary>
        public const float OrbitRadius = 34f;

        /// <summary>How far it sees someone in the open, and how far it keeps after someone it already saw.</summary>
        public const float SightRange = 95f, ChaseRange = 140f;
        /// <summary>Lost from sight this long: it goes to look where they were; that long looking: back to its round.</summary>
        public const float LoseSeconds = 6f, SearchSeconds = 12f;

        /// <summary>Its gun: rounds a second while firing, a burst's length and the pause after, how far, each round's damage.</summary>
        public const float RoundsPerSecond = 8f, BurstSeconds = 2.5f, BurstPause = 3.5f, GunRange = 120f, BulletDamage = 7f;
        /// <summary>Chance a round hits someone in the open: close, and at its range.</summary>
        public const float HitChanceNear = 0.32f, HitChanceFar = 0.07f;

        /// <summary>Rockets: so many in a volley this far apart, a volley this often, how fast they fly, how wide and hard they burst.</summary>
        public const int VolleyRockets = 4;
        public const float RocketGap = 0.3f, VolleyEvery = 18f, RocketSpeed = 70f, RocketRange = 110f, SplashRadius = 5f, SplashDamage = 55f;

        /// <summary>How long a wreck lies burning; how long its crates are too hot to open; how many it drops.</summary>
        public const float WreckSeconds = 600f, CrateBurnSeconds = 120f;
        public const int Crates = 3;

        private readonly IslandGround _ground;
        private readonly Random _random;
        private readonly List<PatrolHeli> _helis = new List<PatrolHeli>();
        private readonly List<(float At, int Heli, float X, float Y, float Z)> _rockets = new List<(float, int, float, float, float)>();
        private float _next;
        private int _nextId = 1;

        /// <summary>Whether a wall, a floor or a high wall stands between two points (none known: nothing does).</summary>
        public Func<float, float, float, float, float, float, bool> Shielded { get; set; }

        public PatrolHelis(IslandGround ground, int seed, float now = 0f)
        {
            _ground = ground;
            _random = new Random(seed ^ 0x4e11c0);
            _next = now + FirstAfter;
        }

        public IReadOnlyList<PatrolHeli> All => _helis;
        public int Count => _helis.Count;

        /// <summary>When the next one comes in (once none is about).</summary>
        public float Next => _next;

        /// <summary>Whether one is in the air now (wrecks do not count).</summary>
        public bool Flying
        {
            get
            {
                foreach (var h in _helis)
                    if (!h.Down) return true;
                return false;
            }
        }

        public bool TryGet(int id, out PatrolHeli heli)
        {
            foreach (var h in _helis)
                if (h.Id == id)
                {
                    heli = h;
                    return true;
                }
            heli = null;
            return false;
        }

        public void Clear()
        {
            _helis.Clear();
            _rockets.Clear();
        }

        /// <summary>
        /// Comes in now from over the sea: towards (<paramref name="atX"/>, <paramref name="atZ"/>) first if given
        /// (and from <paramref name="from"/> metres off it), then on round the ruins.
        /// </summary>
        public PatrolHeli Launch(float now, float? atX = null, float? atZ = null, float from = 0f)
        {
            double a = _random.NextDouble() * Math.PI * 2.0;
            float edge = IslandGround.HalfSize * 1.05f;
            var h = new PatrolHeli { Id = _nextId++, Born = now, Since = now };
            if (atX.HasValue && atZ.HasValue && from > 0f)
            {
                h.X = atX.Value + (float)Math.Sin(a) * from;
                h.Z = atZ.Value + (float)Math.Cos(a) * from;
            }
            else
            {
                h.X = (float)Math.Sin(a) * edge;
                h.Z = (float)Math.Cos(a) * edge;
            }
            h.Y = Cruise(h.X, h.Z);
            h.ExitX = -(float)Math.Sin(a) * IslandGround.HalfSize * 1.5f;
            h.ExitZ = -(float)Math.Cos(a) * IslandGround.HalfSize * 1.5f;
            if (atX.HasValue && atZ.HasValue) h.Route.Add((atX.Value, atZ.Value));
            var stops = new List<(float, float)>();
            foreach (var m in Monuments.Find(_ground)) stops.Add((m.X, m.Z));
            // A couple of open spots too, so it is not only ever over the ruins.
            for (int i = 0; i < 2; i++)
            {
                double b = _random.NextDouble() * Math.PI * 2.0, r = Math.Sqrt(_random.NextDouble()) * IslandGround.IslandRadius * 0.7;
                stops.Add(((float)(Math.Sin(b) * r), (float)(Math.Cos(b) * r)));
            }
            while (stops.Count > 0)
            {
                int i = _random.Next(stops.Count);
                h.Route.Add(stops[i]);
                stops.RemoveAt(i);
            }
            h.Yaw = Wrap(Heading(h.Route[0].X - h.X, h.Route[0].Z - h.Z));
            h.NextBurst = now;
            h.NextVolley = now + VolleyEvery * 0.5f;
            _helis.Add(h);
            return h;
        }

        /// <summary>
        /// Time moves on to <paramref name="now"/> (<paramref name="dt"/> since the last tick). A new one may
        /// come in (<paramref name="started"/>), each flies, looks for the <paramref name="players"/> and fires
        /// at them (<paramref name="shots"/>; rounds and splashes that hit say whom and how much), and one shot
        /// down that reaches the ground goes in <paramref name="crashed"/> (to set its crates down round it).
        /// </summary>
        public void Tick(float now, float dt, IReadOnlyList<Fauna.Prey> players, List<HeliShot> shots,
            List<PatrolHeli> started = null, List<PatrolHeli> crashed = null)
        {
            if (dt <= 0f) return;
            if (Flying || players == null || players.Count == 0) _next = Math.Max(_next, now + 60f);
            else if (now >= _next)
            {
                _next = now + Every + ((float)_random.NextDouble() * 2f - 1f) * Jitter;
                started?.Add(Launch(now));
            }

            for (int i = _rockets.Count - 1; i >= 0; i--)
            {
                var r = _rockets[i];
                if (now < r.At) continue;
                _rockets.RemoveAt(i);
                Burst(r.Heli, r.X, r.Y, r.Z, players, shots);
            }

            for (int i = _helis.Count - 1; i >= 0; i--)
            {
                var h = _helis[i];
                switch (h.State)
                {
                    case HeliState.Wreck:
                        if (now - h.Since >= WreckSeconds) _helis.RemoveAt(i);
                        continue;
                    case HeliState.Crashing:
                        Fall(h, now, dt, crashed);
                        continue;
                    case HeliState.Leaving:
                        Steer(h, h.ExitX, Cruise(h.X, h.Z) + 10f, h.ExitZ, LeaveSpeed, dt, faceX: null, faceZ: null);
                        if (h.X * h.X + h.Z * h.Z > IslandGround.HalfSize * IslandGround.HalfSize * 2.1f) _helis.RemoveAt(i);
                        continue;
                }
                if (now - h.Born >= Lifetime && (h.State != HeliState.Engage || now - h.Born >= Lifetime + 300f))
                {
                    Become(h, HeliState.Leaving, now);
                    h.Target = 0;
                    continue;
                }
                Look(h, now, players);
                Fly(h, now, dt, players);
                if (h.State == HeliState.Engage) Fire(h, now, players, shots);
            }
        }

        /// <summary>
        /// Someone's round (or arrow) struck it for <paramref name="damage"/>, on a rotor if <paramref name="rotor"/>:
        /// it turns on whoever fired, from wherever they are. True if that brought it down.
        /// </summary>
        public bool Hit(int id, float damage, bool rotor, int attacker, float ax, float ay, float az, float now)
        {
            if (!TryGet(id, out var h) || h.Down || damage <= 0f) return false;
            h.Health = Math.Max(0f, h.Health - damage * (rotor ? RotorMultiplier : 1f));
            if (h.Health <= 0f)
            {
                Become(h, HeliState.Crashing, now);
                h.Target = 0;
                // It lurches on the way it was going, and drops.
                float speed = (float)Math.Sqrt(h.Vx * h.Vx + h.Vz * h.Vz);
                if (speed < 10f)
                {
                    double yaw = h.Yaw * Math.PI / 180.0;
                    h.Vx = (float)Math.Sin(yaw) * 10f;
                    h.Vz = (float)Math.Cos(yaw) * 10f;
                }
                h.Vy = 2f;
                return true;
            }
            if (attacker != 0 && h.State != HeliState.Leaving)
            {
                h.Aggro = attacker;
                h.AggroUntil = now + 30f;
                if (h.Target != attacker || h.State != HeliState.Engage)
                {
                    h.Target = attacker;
                    Become(h, HeliState.Engage, now);
                }
                h.SeenX = ax;
                h.SeenY = ay;
                h.SeenZ = az;
                h.LastSeen = now;
            }
            return false;
        }

        /// <summary>Sets its health (tests): 0 brings it down.</summary>
        public void SetHealth(int id, float health, float now)
        {
            if (!TryGet(id, out var h) || h.Down) return;
            if (health <= 0f)
            {
                h.Health = 1f;
                Hit(id, 1f, false, 0, 0f, 0f, 0f, now);
                return;
            }
            h.Health = Math.Min(MaxHealth, health);
        }

        /// <summary>Where crate <paramref name="index"/> of a wreck's <see cref="Crates"/> lands: round it, a few metres out.</summary>
        public void CrateSpot(PatrolHeli wreck, int index, out float x, out float y, out float z, out float yaw)
        {
            double a = (wreck.Yaw + 60f + index * (360f / Crates)) * Math.PI / 180.0;
            float r = 4.5f + (index % 2) * 1.2f;
            x = wreck.X + (float)Math.Sin(a) * r;
            z = wreck.Z + (float)Math.Cos(a) * r;
            y = Math.Max(IslandGround.WaterLevel, _ground.Height(x, z));
            yaw = (float)(a * 180.0 / Math.PI) + 90f;
        }

        /// <summary>Chance one round hits someone in the open this far off.</summary>
        public static float HitChance(float distance)
        {
            float t = Math.Max(0f, Math.Min(1f, (distance - 30f) / (GunRange - 30f)));
            return HitChanceNear + (HitChanceFar - HitChanceNear) * t;
        }

        /// <summary>What a rocket's burst takes off someone this far from it.</summary>
        public static float Splash(float distance) => distance >= SplashRadius ? 0f : SplashDamage * (1f - distance / SplashRadius * 0.8f);

        // ---- Seeing

        private void Look(PatrolHeli h, float now, IReadOnlyList<Fauna.Prey> players)
        {
            if (now < h.NextScan) return;
            h.NextScan = now + 0.4f;
            h.Sees = false;
            if (h.Target != 0 && TryFind(players, h.Target, out var t))
            {
                float range = h.Target == h.Aggro && now < h.AggroUntil ? ChaseRange * 1.5f : ChaseRange;
                if (Distance(h, t) <= range && CanSee(h, t))
                {
                    h.Sees = true;
                    h.LastSeen = now;
                    h.SeenX = t.X;
                    h.SeenY = t.Y;
                    h.SeenZ = t.Z;
                    return;
                }
            }
            else if (h.Target != 0 && !TryFind(players, h.Target, out _))
            {
                // Dead or gone: free to look for someone else.
                h.Target = 0;
                if (h.State == HeliState.Engage) Become(h, HeliState.Search, now);
            }
            // Anyone else in sight: the nearest, if it is not already busy with someone it can see.
            float best = SightRange;
            int found = 0;
            foreach (var p in players)
            {
                float d = Distance(h, p);
                if (d >= best || !CanSee(h, p)) continue;
                best = d;
                found = p.Key;
                h.SeenX = p.X;
                h.SeenY = p.Y;
                h.SeenZ = p.Z;
            }
            if (found == 0) return;
            h.Target = found;
            h.Sees = true;
            h.LastSeen = now;
            if (h.State != HeliState.Engage) Become(h, HeliState.Engage, now);
        }

        private bool CanSee(PatrolHeli h, Fauna.Prey p) =>
            Shielded == null || !Shielded(h.X, h.Y - 1f, h.Z, p.X, p.Y + 1.2f, p.Z);

        // ---- Flying

        private void Fly(PatrolHeli h, float now, float dt, IReadOnlyList<Fauna.Prey> players)
        {
            switch (h.State)
            {
                case HeliState.Patrol:
                {
                    if (h.Route.Count == 0) h.Route.Add((0f, 0f));
                    var (sx, sz) = h.Route[h.Leg % h.Route.Count];
                    float dx = sx - h.X, dz = sz - h.Z;
                    if (h.CircleUntil > 0f)
                    {
                        // Circling the stop, looking down at it.
                        h.Orbit += dt * PatrolSpeed * 0.5f / CircleRadius;
                        float cx = sx + (float)Math.Sin(h.Orbit) * CircleRadius, cz = sz + (float)Math.Cos(h.Orbit) * CircleRadius;
                        Steer(h, cx, Cruise(cx, cz), cz, PatrolSpeed * 0.6f, dt, sx, sz);
                        if (now >= h.CircleUntil)
                        {
                            h.CircleUntil = 0f;
                            h.Leg++;
                        }
                    }
                    else
                    {
                        Steer(h, sx, Cruise(sx, sz), sz, PatrolSpeed, dt, null, null);
                        if (dx * dx + dz * dz < CircleRadius * CircleRadius)
                        {
                            h.CircleUntil = now + CircleSeconds;
                            h.Orbit = (float)Math.Atan2(h.X - sx, h.Z - sz);
                        }
                    }
                    break;
                }
                case HeliState.Engage:
                {
                    if (now - h.LastSeen > LoseSeconds)
                    {
                        Become(h, HeliState.Search, now);
                        break;
                    }
                    float tx = h.SeenX, ty = h.SeenY, tz = h.SeenZ;
                    if (TryFind(players, h.Target, out var t) && h.Sees)
                    {
                        tx = t.X;
                        ty = t.Y;
                        tz = t.Z;
                    }
                    h.Orbit += dt * EngageSpeed / OrbitRadius;
                    float ox = tx + (float)Math.Sin(h.Orbit) * OrbitRadius, oz = tz + (float)Math.Cos(h.Orbit) * OrbitRadius;
                    float oy = Math.Max(ty + EngageHeight, Math.Max(_ground.Height(ox, oz), IslandGround.WaterLevel) + MinClearance);
                    Steer(h, ox, oy, oz, EngageSpeed * 1.6f, dt, tx, tz);
                    break;
                }
                case HeliState.Search:
                {
                    float cx = h.SeenX + (float)Math.Sin(now * 0.4f) * 12f, cz = h.SeenZ + (float)Math.Cos(now * 0.4f) * 12f;
                    Steer(h, cx, Math.Max(h.SeenY + EngageHeight + 6f, Cruise(cx, cz) - 10f), cz, EngageSpeed, dt, h.SeenX, h.SeenZ);
                    if (now - h.Since > SearchSeconds)
                    {
                        h.Target = 0;
                        Become(h, HeliState.Patrol, now);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Flies towards a point at up to <paramref name="speed"/>: it eases its speed round, turns its nose
        /// along the way it goes (or at what it is watching), dips its nose as it speeds up and banks into turns.
        /// </summary>
        private void Steer(PatrolHeli h, float tx, float ty, float tz, float speed, float dt, float? faceX, float? faceZ)
        {
            float dx = tx - h.X, dz = tz - h.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            // Slowing as it nears the point, so it does not overshoot.
            float want = Math.Min(speed, d * 0.8f);
            float wx = d > 0.01f ? dx / d * want : 0f, wz = d > 0.01f ? dz / d * want : 0f;
            const float accel = 7f;
            float ax = wx - h.Vx, az = wz - h.Vz;
            float a = (float)Math.Sqrt(ax * ax + az * az), most = accel * dt;
            if (a > most)
            {
                ax *= most / a;
                az *= most / a;
            }
            h.Vx += ax;
            h.Vz += az;
            float vy = Math.Max(-6f, Math.Min(6f, (ty - h.Y) * 0.8f));
            h.Vy += Math.Max(-4f * dt, Math.Min(4f * dt, vy - h.Vy));
            h.X += h.Vx * dt;
            h.Z += h.Vz * dt;
            h.Y += h.Vy * dt;
            // Never into the ground or the sea.
            h.Y = Math.Max(h.Y, Math.Max(_ground.Height(h.X, h.Z), IslandGround.WaterLevel) + MinClearance * 0.6f);

            float v = (float)Math.Sqrt(h.Vx * h.Vx + h.Vz * h.Vz);
            float face = faceX.HasValue && faceZ.HasValue
                ? Heading(faceX.Value - h.X, faceZ.Value - h.Z)
                : v > 1f ? Heading(h.Vx, h.Vz) : h.Yaw;
            float turn = DeltaAngle(h.Yaw, face);
            float step = 80f * dt;
            h.Yaw = Wrap(h.Yaw + Math.Max(-step, Math.Min(step, turn)));
            // Leaning into the way it is speeding: forward speed dips the nose, sideways speed banks it.
            double yaw = h.Yaw * Math.PI / 180.0;
            float fwd = (float)(h.Vx * Math.Sin(yaw) + h.Vz * Math.Cos(yaw));
            float side = (float)(h.Vx * Math.Cos(yaw) - h.Vz * Math.Sin(yaw));
            h.Pitch += (Math.Max(-8f, Math.Min(14f, fwd * 0.55f)) - h.Pitch) * Math.Min(1f, dt * 2f);
            h.Roll += (Math.Max(-22f, Math.Min(22f, side * 1.1f + turn * 0.15f)) - h.Roll) * Math.Min(1f, dt * 2f);
        }

        private void Fall(PatrolHeli h, float now, float dt, List<PatrolHeli> crashed)
        {
            // Spinning ever faster as the tail rotor goes, dropping like a stone.
            h.Spin = Math.Min(360f, h.Spin + 220f * dt);
            h.Yaw = Wrap(h.Yaw + h.Spin * dt);
            h.Vy = Math.Max(-22f, h.Vy - 9f * dt);
            h.Vx *= 1f - Math.Min(1f, 0.25f * dt);
            h.Vz *= 1f - Math.Min(1f, 0.25f * dt);
            h.X += h.Vx * dt;
            h.Z += h.Vz * dt;
            h.Y += h.Vy * dt;
            h.Roll += (25f - h.Roll) * Math.Min(1f, dt);
            h.Pitch += (18f - h.Pitch) * Math.Min(1f, dt);
            float floor = Math.Max(_ground.Height(h.X, h.Z), IslandGround.WaterLevel);
            if (h.Y > floor + 0.6f && now - h.Since < 15f) return;
            h.Y = floor;
            h.Vx = h.Vy = h.Vz = 0f;
            h.Pitch = 6f;
            h.Roll = 14f;
            Become(h, HeliState.Wreck, now);
            crashed?.Add(h);
        }

        // ---- Firing

        private void Fire(PatrolHeli h, float now, IReadOnlyList<Fauna.Prey> players, List<HeliShot> shots)
        {
            if (!h.Sees || !TryFind(players, h.Target, out var t)) return;
            float distance = Distance(h, t);
            float aim = Math.Abs(DeltaAngle(h.Yaw, Heading(t.X - h.X, t.Z - h.Z)));
            Muzzle(h, out float mx, out float my, out float mz);

            // The gun: a burst, a pause, a burst.
            if (now >= h.NextBurst && distance <= GunRange && aim < 35f)
            {
                h.BurstUntil = now + BurstSeconds;
                h.NextBurst = now + BurstSeconds + BurstPause;
                h.NextRound = now;
            }
            while (now < h.BurstUntil && now >= h.NextRound)
            {
                h.NextRound += 1f / RoundsPerSecond;
                bool hit = _random.NextDouble() < HitChance(distance);
                if (hit)
                    shots?.Add(new HeliShot(h.Id, HeliShotKind.Bullet, mx, my, mz, t.X, t.Y + 1.1f, t.Z, t.Key, BulletDamage));
                else
                {
                    // Into the ground round them.
                    double a = _random.NextDouble() * Math.PI * 2.0;
                    float r = 1.2f + (float)_random.NextDouble() * 3.5f;
                    float x = t.X + (float)Math.Sin(a) * r, z = t.Z + (float)Math.Cos(a) * r;
                    shots?.Add(new HeliShot(h.Id, HeliShotKind.Bullet, mx, my, mz, x, Math.Max(_ground.Height(x, z), t.Y - 0.2f), z, 0, 0f));
                }
            }

            // Rockets: a volley now and then, a rocket at a time.
            if (h.RocketsLeft == 0 && now >= h.NextVolley && distance <= RocketRange && aim < 25f)
            {
                h.RocketsLeft = VolleyRockets;
                h.NextRocket = now;
                h.NextVolley = now + VolleyEvery;
            }
            while (h.RocketsLeft > 0 && now >= h.NextRocket)
            {
                h.RocketsLeft--;
                h.NextRocket += RocketGap;
                double a = _random.NextDouble() * Math.PI * 2.0;
                float r = (float)_random.NextDouble() * 4f;
                float x = t.X + (float)Math.Sin(a) * r, z = t.Z + (float)Math.Cos(a) * r;
                float y = Math.Max(_ground.Height(x, z), t.Y) + 0.2f;
                // From the pod on one side, then the other.
                double yaw = h.Yaw * Math.PI / 180.0;
                float sideways = h.RocketsLeft % 2 == 0 ? 1.6f : -1.6f;
                float fx = h.X + (float)Math.Cos(yaw) * sideways, fz = h.Z - (float)Math.Sin(yaw) * sideways, fy = h.Y - 0.9f;
                shots?.Add(new HeliShot(h.Id, HeliShotKind.Rocket, fx, fy, fz, x, y, z, 0, 0f));
                float flight = (float)Math.Sqrt((x - fx) * (x - fx) + (y - fy) * (y - fy) + (z - fz) * (z - fz)) / RocketSpeed;
                _rockets.Add((now + flight, h.Id, x, y, z));
            }
        }

        private void Burst(int heli, float x, float y, float z, IReadOnlyList<Fauna.Prey> players, List<HeliShot> shots)
        {
            if (players == null) return;
            foreach (var p in players)
            {
                float dx = p.X - x, dy = p.Y + 0.9f - y, dz = p.Z - z;
                float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                float damage = Splash(d);
                if (damage <= 0f) continue;
                if (Shielded != null && Shielded(x, y + 0.3f, z, p.X, p.Y + 0.9f, p.Z)) continue;
                shots?.Add(new HeliShot(heli, HeliShotKind.Splash, x, y, z, p.X, p.Y, p.Z, p.Key, damage));
            }
        }

        /// <summary>Where its gun is: under the nose.</summary>
        public static void Muzzle(PatrolHeli h, out float x, out float y, out float z)
        {
            double yaw = h.Yaw * Math.PI / 180.0;
            x = h.X + (float)Math.Sin(yaw) * 3.4f;
            z = h.Z + (float)Math.Cos(yaw) * 3.4f;
            y = h.Y - 1.1f;
        }

        // ---- Helpers

        private float Cruise(float x, float z) => Math.Max(_ground.Height(x, z), IslandGround.WaterLevel) + CruiseHeight;

        private static void Become(PatrolHeli h, HeliState state, float now)
        {
            h.State = state;
            h.Since = now;
        }

        private static bool TryFind(IReadOnlyList<Fauna.Prey> players, int key, out Fauna.Prey found)
        {
            if (players != null && key != 0)
                foreach (var p in players)
                    if (p.Key == key)
                    {
                        found = p;
                        return true;
                    }
            found = default;
            return false;
        }

        private static float Distance(PatrolHeli h, Fauna.Prey p)
        {
            float dx = h.X - p.X, dy = h.Y - p.Y, dz = h.Z - p.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static float Heading(float dx, float dz) => (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);

        private static float Wrap(float a)
        {
            a %= 360f;
            return a < 0f ? a + 360f : a;
        }

        private static float DeltaAngle(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }
    }
}
