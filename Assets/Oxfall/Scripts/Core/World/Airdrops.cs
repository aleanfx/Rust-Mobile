using System;
using System.Collections.Generic;

namespace Oxfall.Core.World
{
    /// <summary>
    /// One supply drop: a cargo plane crossing the island in a straight line, dropping a crate over
    /// one spot, which comes down under a parachute. Times are on the clock of whoever runs it
    /// (the server's, or the game's alone).
    /// </summary>
    public readonly struct Airdrop
    {
        public readonly int Id;
        /// <summary>Where the plane comes in and goes out (across the ground).</summary>
        public readonly float FromX, FromZ, ToX, ToZ;
        /// <summary>Where the crate comes down, and the ground there.</summary>
        public readonly float DropX, DropZ, GroundY;
        /// <summary>When the plane set off.</summary>
        public readonly float Start;

        public Airdrop(int id, float fromX, float fromZ, float toX, float toZ, float dropX, float dropZ, float groundY, float start)
        {
            Id = id;
            FromX = fromX;
            FromZ = fromZ;
            ToX = toX;
            ToZ = toZ;
            DropX = dropX;
            DropZ = dropZ;
            GroundY = groundY;
            Start = start;
        }

        /// <summary>When it lets the crate go (over the drop spot).</summary>
        public float DropTime => Start + Airdrops.Approach / Airdrops.PlaneSpeed;

        /// <summary>When the crate touches the ground.</summary>
        public float LandTime => DropTime + (Airdrops.Height - GroundY) / Airdrops.FallSpeed;

        /// <summary>When the plane is gone over the far side.</summary>
        public float EndTime => Start + 2f * Airdrops.Approach / Airdrops.PlaneSpeed;

        /// <summary>Where the plane is at a time (across the ground; it flies at <see cref="Airdrops.Height"/>).</summary>
        public void PlaneAt(float time, out float x, out float z)
        {
            float t = (time - Start) * Airdrops.PlaneSpeed / (2f * Airdrops.Approach);
            x = FromX + (ToX - FromX) * t;
            z = FromZ + (ToZ - FromZ) * t;
        }

        /// <summary>How high the crate is at a time, once dropped (the plane's height before that, the ground after landing).</summary>
        public float CrateHeight(float time) =>
            time <= DropTime ? Airdrops.Height : Math.Max(GroundY, Airdrops.Height - (time - DropTime) * Airdrops.FallSpeed);

        /// <summary>The plane's heading, degrees (0 along +Z).</summary>
        public float Yaw => (float)(Math.Atan2(ToX - FromX, ToZ - FromZ) * 180.0 / Math.PI);
    }

    /// <summary>
    /// When and where supplies are dropped, as on the PC game: now and then a plane crosses the island
    /// and a crate of good things comes down somewhere open, for whoever gets there first. The first
    /// some minutes into a fresh island, then every so often, never on water, ruins or a steep slope.
    /// </summary>
    public sealed class Airdrops
    {
        /// <summary>How high the plane flies.</summary>
        public const float Height = 120f;
        /// <summary>The plane's speed, m/s.</summary>
        public const float PlaneSpeed = 50f;
        /// <summary>How fast the crate comes down under its parachute, m/s.</summary>
        public const float FallSpeed = 4.5f;
        /// <summary>How far before the drop spot the plane comes in (and after it goes out).</summary>
        public const float Approach = 520f;
        /// <summary>The first drop after this long; then one every <see cref="Every"/> give or take <see cref="Jitter"/>.</summary>
        public const float FirstAfter = 900f, Every = 2700f, Jitter = 600f;

        private readonly IslandGround _ground;
        private readonly Random _random;
        private readonly List<Airdrop> _flying = new List<Airdrop>();
        private float _next;
        private int _nextId = 1;

        /// <summary>Spots it will not drop onto (trunks, rocks, buildings): set by whoever owns those.</summary>
        public Func<float, float, bool> Blocked { get; set; }

        public Airdrops(IslandGround ground, int seed, float now = 0f)
        {
            _ground = ground;
            _random = new Random(seed ^ 0x5a17d40b);
            _next = now + FirstAfter;
        }

        /// <summary>Planes in the air or crates still coming down.</summary>
        public IReadOnlyList<Airdrop> Flying => _flying;

        /// <summary>When the next plane sets off.</summary>
        public float Next => _next;

        /// <summary>
        /// Time moves on to <paramref name="now"/>: a plane may set off (added to <paramref name="started"/>),
        /// and crates that touched the ground are added to <paramref name="landed"/> (and forgotten here).
        /// </summary>
        public void Tick(float now, List<Airdrop> started, List<Airdrop> landed)
        {
            if (now >= _next)
            {
                _next = now + Every + ((float)_random.NextDouble() * 2f - 1f) * Jitter;
                var drop = Launch(now);
                if (drop.HasValue) started?.Add(drop.Value);
            }
            for (int i = _flying.Count - 1; i >= 0; i--)
            {
                if (now < _flying[i].LandTime) continue;
                landed?.Add(_flying[i]);
                _flying.RemoveAt(i);
            }
        }

        /// <summary>A plane now, over a spot of its own choosing (or, given one, over that).</summary>
        public Airdrop? Launch(float now, float? atX = null, float? atZ = null)
        {
            float x, z;
            if (atX.HasValue && atZ.HasValue)
            {
                x = atX.Value;
                z = atZ.Value;
            }
            else if (!TryPickSpot(out x, out z)) return null;
            double heading = _random.NextDouble() * Math.PI * 2.0;
            float dx = (float)Math.Sin(heading), dz = (float)Math.Cos(heading);
            var drop = new Airdrop(_nextId++, x - dx * Approach, z - dz * Approach, x + dx * Approach, z + dz * Approach,
                x, z, _ground.Height(x, z), now);
            _flying.Add(drop);
            return drop;
        }

        /// <summary>Open, dry, gentle ground away from the ruins and the shore.</summary>
        public bool TryPickSpot(out float x, out float z)
        {
            float half = IslandGround.HalfSize * 0.7f;
            for (int i = 0; i < 200; i++)
            {
                x = ((float)_random.NextDouble() * 2f - 1f) * half;
                z = ((float)_random.NextDouble() * 2f - 1f) * half;
                if (Suitable(_ground, x, z) && (Blocked == null || !Blocked(x, z))) return true;
            }
            x = z = 0f;
            return false;
        }

        public static bool Suitable(IslandGround ground, float x, float z)
        {
            float h = ground.Height(x, z);
            if (h < IslandGround.BeachHeight + 2f || ground.PondAt(x, z, out _)) return false;
            if (Monuments.NoBuilding(ground, x, z)) return false;
            // Not on a steep slope: the crate would sit askew.
            float slope = Math.Max(Math.Abs(ground.Height(x + 2f, z) - ground.Height(x - 2f, z)), Math.Abs(ground.Height(x, z + 2f) - ground.Height(x, z - 2f)));
            return slope < 1.6f;
        }
    }
}
