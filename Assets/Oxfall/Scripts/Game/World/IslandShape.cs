using System.Collections.Generic;
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
        private readonly List<Pond> _ponds = new List<Pond>();

        public int Seed { get; }

        /// <summary>A pool of fresh water: drinkable, unlike the sea.</summary>
        public readonly struct Pond
        {
            public readonly Vector2 Centre;
            public readonly float Radius;
            public readonly float Level;

            public Pond(Vector2 centre, float radius, float level)
            {
                Centre = centre;
                Radius = radius;
                Level = level;
            }
        }

        public IReadOnlyList<Pond> Ponds => _ponds;

        public IslandShape(int seed)
        {
            Seed = seed;
            var random = new System.Random(seed);
            _shoreOffset = NextOffset(random);
            _hillOffset = NextOffset(random);
            _detailOffset = NextOffset(random);
            _colourOffset = NextOffset(random);
            _forestOffset = NextOffset(random);
            PlacePonds(new System.Random(seed * 13 + 5));
        }

        /// <summary>
        /// Two or three ponds on fairly flat ground inland, well apart. The water sits a little below
        /// the lowest ground around each, so it never spills over its rim.
        /// </summary>
        private void PlacePonds(System.Random random)
        {
            for (int attempt = 0; attempt < 400 && _ponds.Count < 3; attempt++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float distance = 30f + (float)random.NextDouble() * 90f;
                var c = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                float radius = 6f + (float)random.NextDouble() * 4f;
                float h = BaseHeight(c.x, c.y);
                if (h < 3f || h > 13f) continue;

                bool apart = true;
                foreach (var other in _ponds)
                    if ((other.Centre - c).magnitude < 70f) apart = false;
                if (!apart) continue;

                // The ground around the rim must be fairly level.
                float lowest = float.MaxValue, highest = float.MinValue;
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8f;
                    var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius + 2f);
                    float rim = BaseHeight(p.x, p.y);
                    lowest = Mathf.Min(lowest, rim);
                    highest = Mathf.Max(highest, rim);
                }
                if (highest - lowest > 2.2f) continue;
                _ponds.Add(new Pond(c, radius, lowest - 0.3f));
            }
        }

        /// <summary>1 in a pond, fading to 0 a few metres up its bank: mud, no grass, no trees.</summary>
        public float PondBank(float x, float z)
        {
            float bank = 0f;
            foreach (var p in _ponds)
            {
                float d = (new Vector2(x, z) - p.Centre).magnitude;
                bank = Mathf.Max(bank, 1f - Mathf.InverseLerp(p.Radius, p.Radius + 3f, d));
            }
            return bank;
        }

        /// <summary>The fresh-water pond at a point, if the point is over one.</summary>
        public bool PondAt(float x, float z, out Pond pond)
        {
            foreach (var p in _ponds)
            {
                if ((new Vector2(x, z) - p.Centre).sqrMagnitude <= p.Radius * p.Radius)
                {
                    pond = p;
                    return true;
                }
            }
            pond = default;
            return false;
        }

        private static Vector2 NextOffset(System.Random random) =>
            new Vector2((float)random.NextDouble() * 5000f + 100f, (float)random.NextDouble() * 5000f + 100f);

        /// <summary>Height of the ground at a world position, in metres: the land, with the ponds dug in.</summary>
        public float Height(float x, float z)
        {
            float h = BaseHeight(x, z);
            foreach (var pond in _ponds)
            {
                float d = (new Vector2(x, z) - pond.Centre).magnitude;
                if (d > pond.Radius + 12f) continue;
                float t = d / pond.Radius;
                // A bowl under the water, its rim a little under the surface, then a bank rising out
                // of the water over a metre and a half and on gently to meet the land.
                float dug = t < 1f ? pond.Level - 0.3f - 1.4f * (1f - t * t)
                    : d < pond.Radius + 1.5f ? pond.Level - 0.3f + (d - pond.Radius) * 0.55f
                    : pond.Level + 0.525f + (d - pond.Radius - 1.5f) * 0.35f;
                h = Mathf.Min(h, dug);
            }
            return h;
        }

        /// <summary>Height of the land before the ponds are dug.</summary>
        private float BaseHeight(float x, float z)
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

            // The muted, rusty palette of the style mock-up.
            var wetSand = new Color(0.40f, 0.36f, 0.29f);
            var sand = new Color(0.64f, 0.57f, 0.44f);
            var grassA = new Color(0.29f, 0.34f, 0.20f);
            var grassB = new Color(0.42f, 0.44f, 0.26f);
            var dirt = new Color(0.40f, 0.32f, 0.24f);
            var rock = new Color(0.46f, 0.44f, 0.40f);
            var highRock = new Color(0.54f, 0.52f, 0.48f);

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
            c = Color.Lerp(c, new Color(0.30f, 0.26f, 0.20f), PondBank(x, z));
            float shade = 0.9f + n * 0.2f;
            return new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
        }

        /// <summary>
        /// How much of each ground texture shows here, as (grass, dirt, sand, rock) summing to 1:
        /// sand on the beach, rock on steep slopes and high up, dirt in patches and under forests.
        /// </summary>
        public Color GroundWeights(float x, float z)
        {
            float h = Height(x, z);
            float slope = 1f - Normal(x, z).y;
            float n = Fbm(x * 0.05f + _colourOffset.x, z * 0.05f + _colourOffset.y, 3);
            float forest = Forest(x, z);

            float rock = Mathf.Clamp01((slope - 0.1f) * 7f) + Mathf.Clamp01((h - 18f) / 5f);
            rock = Mathf.Clamp01(rock);
            float sand = 1f - Mathf.InverseLerp(1.5f, 2.5f, h);
            sand = Mathf.Clamp01(sand) * (1f - rock);
            float dirt = Mathf.Clamp01((n - 0.56f) * 3.5f) + Mathf.Clamp01((forest - 0.55f) * 2.5f) * 0.6f;
            dirt = Mathf.Clamp01(dirt) * (1f - rock) * (1f - sand);
            // Mud round the ponds.
            float mud = PondBank(x, z);
            if (mud > 0f)
            {
                dirt = Mathf.Lerp(dirt, 1f - rock, mud);
                sand *= 1f - mud;
            }
            float grass = Mathf.Max(0f, 1f - rock - sand - dirt);
            return new Color(grass, dirt, sand, rock);
        }

        /// <summary>
        /// 0..1, how much grass grows here: none on sand, rock, dirt patches or steep ground. Uses the
        /// same rules as <see cref="GroundColour"/> so blades stand where the ground is green.
        /// </summary>
        public float GrassAmount(float x, float z, float h)
        {
            if (h < 2.0f || h > 20f) return 0f;
            if (_ponds.Count > 0 && PondBank(x, z) > 0.2f) return 0f;
            float slope = Mathf.Abs(Height(x + 1f, z) - h) + Mathf.Abs(Height(x, z + 1f) - h);
            float n = Fbm(x * 0.05f + _colourOffset.x, z * 0.05f + _colourOffset.y, 3);
            float amount = Mathf.InverseLerp(2.0f, 2.8f, h)
                           * (1f - Mathf.InverseLerp(16f, 20f, h))
                           * (1f - Mathf.Clamp01((n - 0.58f) * 4f))
                           * (1f - Mathf.InverseLerp(0.35f, 0.7f, slope));
            return Mathf.Clamp01(amount);
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
