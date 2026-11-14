using System;
using System.Collections.Generic;
using Oxfall.Core.Gathering;

namespace Oxfall.Core.World
{
    /// <summary>One tree, rock, ore, hemp plant or berry bush: where it stands and how it looks.</summary>
    public readonly struct NodeSpawn
    {
        /// <summary>Fixed by its cell on the island's grid, so it is the same on every machine.</summary>
        public readonly int Id;
        public readonly ResourceNodeType Type;
        public readonly float X, Y, Z;
        public readonly float Yaw;
        public readonly float Scale;
        /// <summary>A free number for the look (which mesh, which leaves).</summary>
        public readonly int Variant;
        /// <summary>For trees: a pine rather than a broadleaf.</summary>
        public readonly bool Pine;

        public NodeSpawn(int id, ResourceNodeType type, float x, float y, float z, float yaw, float scale, int variant, bool pine)
        {
            Id = id;
            Type = type;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Scale = scale;
            Variant = variant;
            Pine = pine;
        }
    }

    /// <summary>
    /// Where the gatherable things stand, from the island's seed. The island is cut into 6 m cells
    /// and each cell rolls its own dice (seeded by the cell), so the game and the server agree on
    /// every node, and even if one machine's arithmetic tips a borderline cell the other way, no
    /// other cell changes.
    /// </summary>
    public static class ResourceLayout
    {
        public const float Cell = 6f;
        public static readonly int Cells = (int)Math.Round(IslandGround.WorldSize / Cell);

        public static List<NodeSpawn> Generate(IslandGround ground)
        {
            var spawns = new List<NodeSpawn>(2048);
            for (int gz = 0; gz < Cells; gz++)
            for (int gx = 0; gx < Cells; gx++)
            {
                var dice = new CellDice(ground.Seed, gx, gz);
                float x = -IslandGround.HalfSize + (gx + 0.15f + 0.7f * dice.Next()) * Cell;
                float z = -IslandGround.HalfSize + (gz + 0.15f + 0.7f * dice.Next()) * Cell;
                float h = ground.Height(x, z);
                if (h < 1.3f || ground.PondBank(x, z) > 0f || Monuments.Inside(ground, x, z)) continue;

                float flatness = ground.Flatness(x, z);
                float forest = ground.Forest(x, z);
                float roll = dice.Next();
                float yaw = dice.Next() * 360f;
                int variant = dice.Int();
                bool grassy = h > 2.4f && h < 17f && flatness > 0.88f;

                float treeChance = grassy ? IslandGround.Clamp01((forest - 0.44f) * 3.2f) * 0.55f + 0.02f : 0f;
                float stoneChance = 0.035f + (flatness < 0.9f ? 0.08f : 0f);
                float oreChance = h > 5f ? 0.022f : 0f;
                float hempChance = grassy && forest < 0.5f ? 0.05f : 0f;
                float berryChance = grassy && forest > 0.3f && forest < 0.7f ? 0.035f : 0f;
                // Old barrels where people once went: the beaches' edge and open grass, rarely the woods.
                float barrelChance = h < 17f && flatness > 0.88f ? (forest < 0.45f ? 0.024f : 0.008f) : 0f;
                // Sulfur on the higher, rougher ground, like metal ore but a little rarer.
                float sulfurChance = h > 4f ? (flatness < 0.9f ? 0.026f : 0.014f) : 0f;

                int id = gz * Cells + gx + 1;
                if ((roll -= treeChance) < 0f)
                {
                    bool pine = forest > 0.55f || dice.Next() < 0.5f;
                    spawns.Add(new NodeSpawn(id, ResourceNodeType.Tree, x, h, z, yaw, 0.8f + dice.Next() * 0.5f, variant, pine));
                }
                else if ((roll -= stoneChance) < 0f)
                    spawns.Add(new NodeSpawn(id, ResourceNodeType.Stone, x, h, z, yaw, 1.3f + dice.Next(), variant, false));
                else if ((roll -= oreChance) < 0f)
                    spawns.Add(new NodeSpawn(id, ResourceNodeType.MetalOre, x, h, z, yaw, 1.1f + dice.Next() * 0.7f, variant, false));
                else if ((roll -= hempChance) < 0f)
                    spawns.Add(new NodeSpawn(id, ResourceNodeType.Hemp, x, h, z, yaw, 1f, variant, false));
                else if ((roll -= berryChance) < 0f)
                    spawns.Add(new NodeSpawn(id, ResourceNodeType.BerryBush, x, h, z, yaw, 0.85f + dice.Next() * 0.4f, variant, false));
                // Last, so no cell that held something before holds anything else now.
                else if ((roll -= barrelChance) < 0f)
                    spawns.Add(new NodeSpawn(id, ResourceNodeType.Barrel, x, h, z, yaw, 1f, variant, false));
                // Added after the others, for the same reason.
                else if ((roll -= sulfurChance) < 0f)
                    spawns.Add(new NodeSpawn(id, ResourceNodeType.Sulfur, x, h, z, yaw, 1.1f + dice.Next() * 0.7f, variant, false));
            }
            // The ruins' crates and barrels, after every cell's own.
            Monuments.AddNodes(ground, spawns);
            return spawns;
        }

        /// <summary>A small random number source of its own for each cell (xorshift, seeded by a hash).</summary>
        private struct CellDice
        {
            private ulong _state;

            public CellDice(int seed, int gx, int gz)
            {
                ulong h = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL;
                h ^= (ulong)(uint)gx * 0xBF58476D1CE4E5B9UL;
                h ^= (ulong)(uint)gz * 0x94D049BB133111EBUL;
                h ^= h >> 31;
                _state = h == 0 ? 0x2545F4914F6CDD1DUL : h;
            }

            private ulong NextBits()
            {
                _state ^= _state >> 12;
                _state ^= _state << 25;
                _state ^= _state >> 27;
                return _state * 0x2545F4914F6CDD1DUL;
            }

            /// <summary>0 (inclusive) to 1 (exclusive).</summary>
            public float Next() => (NextBits() >> 40) / (float)(1UL << 24);

            public int Int() => (int)(NextBits() >> 33);
        }
    }
}
