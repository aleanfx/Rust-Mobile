using UnityEngine;

namespace Oxfall.Game.World
{
    /// <summary>
    /// The island's height and colour as pure functions of a seed. Every client computes the same
    /// island from the same seed, so the server only has to send the seed (and the weekly wipe is a
    /// new seed).
    /// </summary>
    public sealed class IslandShape
    {
        /// <summary>Side of the square of land that is simulated, in metres. The sea goes on beyond it.</summary>
        public const float WorldSize = 480f;
        public const float HalfSize = WorldSize * 0.5f;

        /// <summary>Rough radius of the island itself.</summary>
        public const float IslandRadius = 175f;

        public const float WaterLevel = 0f;
        public const float SeaFloor = -9f;
        public const float BeachHeight = 1.0f;

        private readonly Vector2 _shoreOffset;
        private readonly Vector2 _hillOffset;
        private readonly Vector2 _detailOffset;
        private readonly Vector2 _colourOffset;
        private readonly Vector2 _forestOffset;

        public int Seed { get; }

        public IslandShape(int seed)
        {
            Seed = seed;
            var random = new System.Random(seed);
            _shoreOffset = NextOffset(random);
            _hillOffset = NextOffset(random);
            _detailOffset = NextOffset(random);
            _colourOffset = NextOffset(random);
            _forestOffset = NextOffset(random);
        }

        private static Vector2 NextOffset(System.Random random) =>
            new Vector2((float)random.NextDouble() * 5000f + 100f, (float)random.NextDouble() * 5000f + 100f);

        /// <summary>Height of the ground at a world position, in metres.</summary>
        public float Height(float x, float z)
        {
            float land = LandMask(x, z);

            // Rolling hills, with a mountain towards the middle.
            float hills = Fbm(x * 0.0065f + _hillOffset.x, z * 0.0065f + _hillOffset.y, 4);
            float detail = Fbm(x * 0.03f + _detailOffset.x, z * 0.03f + _detailOffset.y, 2);
            float centre = 1f - Mathf.Clamp01(Mathf.Sqrt(x * x + z * z) / IslandRadius);

            float inland = 1.6f
                           + Mathf.Pow(hills, 1.6f) * 34f * Mathf.Lerp(0.45f, 1f, centre)
                           + (detail - 0.5f) * 2.2f;

            // The sea floor rises to a flat beach, and the hills start a little further in.
            float shore = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(land / 0.3f));
            float inlandBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, land));
            float onLand = Mathf.Lerp(BeachHeight, inland, inlandBlend);

            return Mathf.Lerp(SeaFloor, onLand, shore);
        }

        /// <summary>Surface normal from the height function, so chunk seams always match.</summary>
        public Vector3 Normal(float x, float z)
        {
            const float e = 0.75f;
            float hl = Height(x - e, z);
            float hr = Height(x + e, z);
            float hd = Height(x, z - e);
            float hu = Height(x, z + e);
            return new Vector3(hl - hr, 2f * e, hd - hu).normalized;
        }

        /// <summary>
        /// 0 in the sea, rising through the shore to 1 well inland. The shore is pushed in and out by
        /// noise so the coast is not a circle.
        /// </summary>
        public float LandMask(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z) / IslandRadius;
            float wobble = Fbm(x * 0.008f + _shoreOffset.x, z * 0.008f + _shoreOffset.y, 3) - 0.5f;
            r += wobble * 0.4f;
            return Mathf.Clamp01((1.05f - r) / 0.5f);
        }

        /// <summary>0..1, how much a spot wants trees. Forests come in patches.</summary>
        public float Forest(float x, float z) =>
            Fbm(x * 0.012f + _forestOffset.x, z * 0.012f + _forestOffset.y, 3);

        /// <summary>The ground's colour: sand, grass, dirt and rock, by height and slope.</summary>
        public Color GroundColour(float x, float z)
        {
            float h = Height(x, z);
            float slope = 1f - Normal(x, z).y; // 0 flat, ~0.3 steep
            float n = Fbm(x * 0.05f + _colourOffset.x, z * 0.05f + _colourOffset.y, 3);
            float patches = Fbm(x * 0.015f + _colourOffset.y, z * 0.015f + _colourOffset.x, 2);

            var wetSand = new Color(0.55f, 0.49f, 0.36f);
            var sand = new Color(0.80f, 0.72f, 0.53f);
            var grassA = new Color(0.30f, 0.42f, 0.18f);
            var grassB = new Color(0.45f, 0.50f, 0.24f);
            var dirt = new Color(0.42f, 0.34f, 0.24f);
            var rock = new Color(0.46f, 0.44f, 0.41f);
            var highRock = new Color(0.56f, 0.55f, 0.52f);

            Color c;
            if (h < 0.4f) c = Color.Lerp(wetSand, sand, Mathf.InverseLerp(-1.5f, 0.4f, h));
            else if (h < 2.2f) c = Color.Lerp(sand, Color.Lerp(grassB, sand, 0.4f), Mathf.InverseLerp(1.4f, 2.2f, h));
            else
            {
                c = Color.Lerp(grassA, grassB, Mathf.SmoothStep(0f, 1f, patches));
                c = Color.Lerp(c, dirt, Mathf.Clamp01((n - 0.62f) * 3f));
                c = Color.Lerp(c, highRock, Mathf.Clamp01((h - 17f) / 6f));
            }

            c = Color.Lerp(c, rock, Mathf.Clamp01((slope - 0.12f) * 6f));
            float shade = 0.9f + n * 0.2f;
            return new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
        }

        /// <summary>Fractal noise in roughly 0..1.</summary>
        public static float Fbm(float x, float z, int octaves)
        {
            float sum = 0f, amplitude = 0.5f, frequency = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x * frequency, z * frequency) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.03f;
            }
            return sum / norm;
        }
    }
}
