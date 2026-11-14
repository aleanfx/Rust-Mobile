using System;
using System.Collections.Generic;

namespace Oxfall.Core.World
{
    /// <summary>
    /// The island's ground as pure functions of a seed: height, land and sea, forests and ponds.
    /// It is plain C# (its own noise, no engine), so the game and the server compute the same
    /// island from the same seed, and the weekly wipe is just a new seed.
    /// </summary>
    public sealed class IslandGround
    {
        /// <summary>Side of the square of land that is simulated, in metres. The sea goes on beyond it.</summary>
        public const float WorldSize = 480f;
        public const float HalfSize = WorldSize * 0.5f;

        /// <summary>Rough radius of the island itself.</summary>
        public const float IslandRadius = 175f;

        public const float WaterLevel = 0f;
        public const float SeaFloor = -9f;
        public const float BeachHeight = 1.0f;

        private readonly float _shoreX, _shoreZ, _hillX, _hillZ, _detailX, _detailZ, _forestX, _forestZ;
        private readonly List<Pond> _ponds = new List<Pond>();

        public int Seed { get; }

        /// <summary>Offsets for the ground colour noise (only the look uses them).</summary>
        public float ColourX { get; }
        public float ColourZ { get; }

        /// <summary>A pool of fresh water: drinkable, unlike the sea.</summary>
        public readonly struct Pond
        {
            public readonly float X, Z, Radius, Level;

            public Pond(float x, float z, float radius, float level)
            {
                X = x;
                Z = z;
                Radius = radius;
                Level = level;
            }

            public float DistanceTo(float x, float z) => MathF.Sqrt((x - X) * (x - X) + (z - Z) * (z - Z));
        }

        public IReadOnlyList<Pond> Ponds => _ponds;

        public IslandGround(int seed)
        {
            Seed = seed;
            var random = new Random(seed);
            NextOffset(random, out _shoreX, out _shoreZ);
            NextOffset(random, out _hillX, out _hillZ);
            NextOffset(random, out _detailX, out _detailZ);
            NextOffset(random, out float cx, out float cz);
            ColourX = cx;
            ColourZ = cz;
            NextOffset(random, out _forestX, out _forestZ);
            PlacePonds(new Random(seed * 13 + 5));
        }

        private static void NextOffset(Random random, out float x, out float z)
        {
            x = (float)random.NextDouble() * 5000f + 100f;
            z = (float)random.NextDouble() * 5000f + 100f;
        }

        /// <summary>
        /// Two or three ponds on fairly flat ground inland, well apart. The water sits a little below
        /// the lowest ground around each, so it never spills over its rim.
        /// </summary>
        private void PlacePonds(Random random)
        {
            for (int attempt = 0; attempt < 400 && _ponds.Count < 3; attempt++)
            {
                float angle = (float)random.NextDouble() * MathF.PI * 2f;
                float distance = 30f + (float)random.NextDouble() * 90f;
                float cx = MathF.Cos(angle) * distance, cz = MathF.Sin(angle) * distance;
                float radius = 6f + (float)random.NextDouble() * 4f;
                float h = BaseHeight(cx, cz);
                if (h < 3f || h > 13f) continue;

                bool apart = true;
                foreach (var other in _ponds)
                    if (other.DistanceTo(cx, cz) < 70f) apart = false;
                if (!apart) continue;

                // The ground around the rim must be fairly level.
                float lowest = float.MaxValue, highest = float.MinValue;
                for (int i = 0; i < 16; i++)
                {
                    float a = i * MathF.PI / 8f;
                    float rim = BaseHeight(cx + MathF.Cos(a) * (radius + 2f), cz + MathF.Sin(a) * (radius + 2f));
                    lowest = MathF.Min(lowest, rim);
                    highest = MathF.Max(highest, rim);
                }
                if (highest - lowest > 2.2f) continue;
                _ponds.Add(new Pond(cx, cz, radius, lowest - 0.3f));
            }
        }

        /// <summary>1 in a pond, fading to 0 a few metres up its bank: mud, no grass, no trees.</summary>
        public float PondBank(float x, float z)
        {
            float bank = 0f;
            foreach (var p in _ponds)
                bank = MathF.Max(bank, 1f - InverseLerp(p.Radius, p.Radius + 3f, p.DistanceTo(x, z)));
            return bank;
        }

        /// <summary>The fresh-water pond at a point, if the point is over one.</summary>
        public bool PondAt(float x, float z, out Pond pond)
        {
            foreach (var p in _ponds)
            {
                if ((x - p.X) * (x - p.X) + (z - p.Z) * (z - p.Z) <= p.Radius * p.Radius)
                {
                    pond = p;
                    return true;
                }
            }
            pond = default;
            return false;
        }

        /// <summary>Height of the ground at a world position, in metres: the land, with the ponds dug in.</summary>
        public float Height(float x, float z)
        {
            float h = BaseHeight(x, z);
            foreach (var pond in _ponds)
            {
                float d = pond.DistanceTo(x, z);
                if (d > pond.Radius + 12f) continue;
                float t = d / pond.Radius;
                // A bowl under the water, its rim a little under the surface, then a bank rising out
                // of the water over a metre and a half and on gently to meet the land.
                float dug = t < 1f ? pond.Level - 0.3f - 1.4f * (1f - t * t)
                    : d < pond.Radius + 1.5f ? pond.Level - 0.3f + (d - pond.Radius) * 0.55f
                    : pond.Level + 0.525f + (d - pond.Radius - 1.5f) * 0.35f;
                h = MathF.Min(h, dug);
            }
            return h;
        }

        /// <summary>Height of the land before the ponds are dug.</summary>
        private float BaseHeight(float x, float z)
        {
            float land = LandMask(x, z);

            // Rolling hills, with a mountain towards the middle.
            float hills = Fbm(x * 0.0065f + _hillX, z * 0.0065f + _hillZ, 4);
            float detail = Fbm(x * 0.03f + _detailX, z * 0.03f + _detailZ, 2);
            float centre = 1f - Clamp01(MathF.Sqrt(x * x + z * z) / IslandRadius);

            float inland = 1.6f
                           + MathF.Pow(MathF.Max(0f, hills), 1.6f) * 34f * Lerp(0.45f, 1f, centre)
                           + (detail - 0.5f) * 2.2f;

            // The sea floor rises to a flat beach, and the hills start a little further in.
            float shore = SmoothStep(Clamp01(land / 0.3f));
            float inlandBlend = SmoothStep(InverseLerp(0.35f, 0.7f, land));
            float onLand = Lerp(BeachHeight, inland, inlandBlend);

            return Lerp(SeaFloor, onLand, shore);
        }

        /// <summary>Surface normal from the height function (unit length, y up).</summary>
        public void Normal(float x, float z, out float nx, out float ny, out float nz)
        {
            const float e = 0.75f;
            float hl = Height(x - e, z), hr = Height(x + e, z), hd = Height(x, z - e), hu = Height(x, z + e);
            nx = hl - hr;
            ny = 2f * e;
            nz = hd - hu;
            float length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= length;
            ny /= length;
            nz /= length;
        }

        /// <summary>1 on level ground, less on slopes (the up part of the normal).</summary>
        public float Flatness(float x, float z)
        {
            Normal(x, z, out _, out float ny, out _);
            return ny;
        }

        /// <summary>
        /// 0 in the sea, rising through the shore to 1 well inland. The shore is pushed in and out by
        /// noise so the coast is not a circle.
        /// </summary>
        public float LandMask(float x, float z)
        {
            float r = MathF.Sqrt(x * x + z * z) / IslandRadius;
            float wobble = Fbm(x * 0.008f + _shoreX, z * 0.008f + _shoreZ, 3) - 0.5f;
            r += wobble * 0.4f;
            return Clamp01((1.05f - r) / 0.5f);
        }

        /// <summary>0..1, how much a spot wants trees. Forests come in patches.</summary>
        public float Forest(float x, float z) => Fbm(x * 0.012f + _forestX, z * 0.012f + _forestZ, 3);

        // ---- Noise and small maths, the same on every machine.

        /// <summary>Fractal noise in roughly 0..1.</summary>
        public static float Fbm(float x, float z, int octaves)
        {
            float sum = 0f, amplitude = 0.5f, frequency = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(x * frequency, z * frequency) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.03f;
            }
            return sum / norm;
        }

        private static readonly byte[] Perm = BuildPermutation();

        private static byte[] BuildPermutation()
        {
            // A fixed shuffle: the noise is part of the island's definition and never changes.
            var p = new byte[512];
            var order = new byte[256];
            for (int i = 0; i < 256; i++) order[i] = (byte)i;
            uint state = 2463534242u;
            for (int i = 255; i > 0; i--)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                int j = (int)(state % (uint)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
            for (int i = 0; i < 512; i++) p[i] = order[i & 255];
            return p;
        }

        /// <summary>Gradient noise in roughly 0..1, smooth, repeating every 256 units.</summary>
        public static float Noise(float x, float z)
        {
            int xi = (int)MathF.Floor(x), zi = (int)MathF.Floor(z);
            float xf = x - xi, zf = z - zi;
            xi &= 255;
            zi &= 255;
            float u = Fade(xf), v = Fade(zf);
            int aa = Perm[Perm[xi] + zi], ab = Perm[Perm[xi] + zi + 1];
            int ba = Perm[Perm[xi + 1] + zi], bb = Perm[Perm[xi + 1] + zi + 1];
            float x1 = Lerp(Grad(aa, xf, zf), Grad(ba, xf - 1f, zf), u);
            float x2 = Lerp(Grad(ab, xf, zf - 1f), Grad(bb, xf - 1f, zf - 1f), u);
            return (Lerp(x1, x2, v) + 1f) * 0.5f;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        private static float Grad(int hash, float x, float z)
        {
            switch (hash & 7)
            {
                case 0: return x + z;
                case 1: return -x + z;
                case 2: return x - z;
                case 3: return -x - z;
                case 4: return x;
                case 5: return -x;
                case 6: return z;
                default: return -z;
            }
        }

        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => a == b ? 0f : Clamp01((v - a) / (b - a));
        private static float SmoothStep(float t) => t * t * (3f - 2f * t);
    }
}
