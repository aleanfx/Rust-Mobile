using System;
using System.Collections.Generic;
using Oxfall.Core.Gathering;

namespace Oxfall.Core.World
{
    /// <summary>
    /// The trunks and boulders that stand in the way of anything walking about the island (animals:
    /// players have their own colliders). At most one per 6 m cell of the resource grid, so a spot is
    /// checked against the few cells around it.
    /// </summary>
    public sealed class Obstacles
    {
        private readonly Dictionary<int, (float X, float Z, float R)> _byCell = new Dictionary<int, (float, float, float)>();

        public Obstacles(IEnumerable<NodeSpawn> nodes)
        {
            foreach (var n in nodes)
            {
                float r;
                switch (n.Type)
                {
                    case ResourceNodeType.Tree: r = 0.35f * n.Scale; break;
                    case ResourceNodeType.Stone:
                    case ResourceNodeType.MetalOre:
                    case ResourceNodeType.Sulfur: r = 0.5f * n.Scale; break;
                    case ResourceNodeType.Barrel: r = 0.3f; break;
                    default: continue;
                }
                _byCell[n.Id] = (n.X, n.Z, r);
            }
        }

        public int Count => _byCell.Count;

        /// <summary>True if something this wide (<paramref name="pad"/> from its centre) at (x, z) would stand in a trunk or a boulder.</summary>
        public bool Blocks(float x, float z, float pad)
        {
            int gx = (int)Math.Floor((x + IslandGround.HalfSize) / ResourceLayout.Cell);
            int gz = (int)Math.Floor((z + IslandGround.HalfSize) / ResourceLayout.Cell);
            for (int cz = gz - 1; cz <= gz + 1; cz++)
            for (int cx = gx - 1; cx <= gx + 1; cx++)
            {
                if (cx < 0 || cz < 0 || cx >= ResourceLayout.Cells || cz >= ResourceLayout.Cells) continue;
                if (!_byCell.TryGetValue(cz * ResourceLayout.Cells + cx + 1, out var o)) continue;
                float dx = x - o.X, dz = z - o.Z, reach = o.R + pad;
                if (dx * dx + dz * dz < reach * reach) return true;
            }
            return false;
        }
    }
}
