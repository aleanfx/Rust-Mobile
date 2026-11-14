using System.Collections.Generic;
using Oxfall.Core.World;
using UnityEngine;

namespace Oxfall.Game.World
{
    /// <summary>
    /// The island's height and colour as pure functions of a seed. Every client computes the same
    /// island from the same seed, so the server only has to send the seed (and the weekly wipe is a
    /// new seed).
    /// </summary>
    /// <summary>
    /// The island as the game sees it: the shared ground rules of <see cref="IslandGround"/> (which the
    /// server computes too) in Unity's types, plus what only the look needs (ground colours,
    /// texture weights, where grass grows).
    /// </summary>
    public sealed class IslandShape
    {
        public const float WorldSize = IslandGround.WorldSize;
        public const float HalfSize = IslandGround.HalfSize;
        public const float IslandRadius = IslandGround.IslandRadius;
        public const float WaterLevel = IslandGround.WaterLevel;
        public const float SeaFloor = IslandGround.SeaFloor;
        public const float BeachHeight = IslandGround.BeachHeight;

        private readonly Vector2 _colourOffset;
        private readonly List<Pond> _ponds = new List<Pond>();

        /// <summary>The ground rules shared with the server.</summary>
        public IslandGround Ground { get; }

        public int Seed => Ground.Seed;

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
            Ground = new IslandGround(seed);
            _colourOffset = new Vector2(Ground.ColourX, Ground.ColourZ);
            foreach (var p in Ground.Ponds) _ponds.Add(new Pond(new Vector2(p.X, p.Z), p.Radius, p.Level));
        }

        public float PondBank(float x, float z) => Ground.PondBank(x, z);

        public bool PondAt(float x, float z, out Pond pond)
        {
            if (Ground.PondAt(x, z, out var p))
            {
                pond = new Pond(new Vector2(p.X, p.Z), p.Radius, p.Level);
                return true;
            }
            pond = default;
            return false;
        }

        public float Height(float x, float z) => Ground.Height(x, z);

        public Vector3 Normal(float x, float z)
        {
            Ground.Normal(x, z, out float nx, out float ny, out float nz);
            return new Vector3(nx, ny, nz);
        }

        public float LandMask(float x, float z) => Ground.LandMask(x, z);

        public float Forest(float x, float z) => Ground.Forest(x, z);

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
            // Nothing grows through the ruins' concrete.
            if (Oxfall.Core.World.Monuments.Inside(Ground, x, z, 0.5f)) return 0f;
            float slope = Mathf.Abs(Height(x + 1f, z) - h) + Mathf.Abs(Height(x, z + 1f) - h);
            float n = Fbm(x * 0.05f + _colourOffset.x, z * 0.05f + _colourOffset.y, 3);
            float amount = Mathf.InverseLerp(2.0f, 2.8f, h)
                           * (1f - Mathf.InverseLerp(16f, 20f, h))
                           * (1f - Mathf.Clamp01((n - 0.58f) * 4f))
                           * (1f - Mathf.InverseLerp(0.35f, 0.7f, slope));
            return Mathf.Clamp01(amount);
        }

        /// <summary>Fractal noise in roughly 0..1 (the same noise the island is made of).</summary>
        public static float Fbm(float x, float z, int octaves) => IslandGround.Fbm(x, z, octaves);
    }
}
