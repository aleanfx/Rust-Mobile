using System.Collections.Generic;
using UnityEngine;

namespace Oxfall.Game.World
{
    /// <summary>
    /// Turns an <see cref="IslandShape"/> into ground: a grid of chunk meshes (so the camera can cull
    /// the ones behind it), their colliders, and one colour texture stretched over the whole island.
    /// </summary>
    public static class TerrainBuilder
    {
        /// <summary>Metres between grid points. 3 m keeps the whole island near 25k vertices.</summary>
        public const float CellSize = 3f;

        /// <summary>Cells along one side of a chunk.</summary>
        public const int ChunkCells = 20;

        public const int ColourMapSize = 512;

        public static GameObject Build(IslandShape shape, Material groundMaterial, Transform parent)
        {
            var root = new GameObject("Terrain");
            root.transform.SetParent(parent, false);
            root.isStatic = true;

            int cellsPerSide = Mathf.RoundToInt(IslandShape.WorldSize / CellSize);
            int chunksPerSide = Mathf.CeilToInt(cellsPerSide / (float)ChunkCells);

            for (int cz = 0; cz < chunksPerSide; cz++)
            for (int cx = 0; cx < chunksPerSide; cx++)
            {
                int startX = cx * ChunkCells;
                int startZ = cz * ChunkCells;
                int countX = Mathf.Min(ChunkCells, cellsPerSide - startX);
                int countZ = Mathf.Min(ChunkCells, cellsPerSide - startZ);
                if (countX <= 0 || countZ <= 0) continue;

                var mesh = BuildChunk(shape, startX, startZ, countX, countZ);
                var chunk = new GameObject($"Chunk {cx},{cz}");
                chunk.transform.SetParent(root.transform, false);
                chunk.isStatic = true;
                chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = chunk.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = groundMaterial;
                chunk.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            return root;
        }

        /// <summary>
        /// The height of the terrain mesh as drawn at a point: the island's height at the grid corners,
        /// spread over the two triangles of the cell the way the mesh is cut (corner to far corner).
        /// It can differ from <see cref="IslandShape.Height"/> by a little between grid points.
        /// </summary>
        public static float MeshHeight(IslandShape shape, float x, float z)
        {
            float gx = (x + IslandShape.HalfSize) / CellSize, gz = (z + IslandShape.HalfSize) / CellSize;
            float ix = Mathf.Floor(gx), iz = Mathf.Floor(gz);
            float fx = gx - ix, fz = gz - iz;
            float x0 = -IslandShape.HalfSize + ix * CellSize, z0 = -IslandShape.HalfSize + iz * CellSize;
            float h00 = shape.Height(x0, z0);
            float h11 = shape.Height(x0 + CellSize, z0 + CellSize);
            if (fz >= fx)
            {
                float h01 = shape.Height(x0, z0 + CellSize);
                return h00 + fz * (h01 - h00) + fx * (h11 - h01);
            }
            float h10 = shape.Height(x0 + CellSize, z0);
            return h00 + fx * (h10 - h00) + fz * (h11 - h10);
        }

        private static Mesh BuildChunk(IslandShape shape, int startX, int startZ, int countX, int countZ)
        {
            int vx = countX + 1;
            int vz = countZ + 1;
            var vertices = new List<Vector3>(vx * vz);
            var normals = new List<Vector3>(vx * vz);
            var uvs = new List<Vector2>(vx * vz);
            var triangles = new List<int>(countX * countZ * 6);

            for (int z = 0; z < vz; z++)
            for (int x = 0; x < vx; x++)
            {
                float wx = -IslandShape.HalfSize + (startX + x) * CellSize;
                float wz = -IslandShape.HalfSize + (startZ + z) * CellSize;
                vertices.Add(new Vector3(wx, shape.Height(wx, wz), wz));
                normals.Add(shape.Normal(wx, wz));
                uvs.Add(new Vector2((wx + IslandShape.HalfSize) / IslandShape.WorldSize,
                                    (wz + IslandShape.HalfSize) / IslandShape.WorldSize));
            }

            for (int z = 0; z < countZ; z++)
            for (int x = 0; x < countX; x++)
            {
                int i = z * vx + x;
                // Clockwise seen from above, which is front-facing in Unity.
                triangles.Add(i); triangles.Add(i + vx); triangles.Add(i + vx + 1);
                triangles.Add(i); triangles.Add(i + vx + 1); triangles.Add(i + 1);
            }

            var mesh = new Mesh { name = $"Terrain {startX},{startZ}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Which ground texture shows where, (grass, dirt, sand, rock) in RGBA, one texel per ~1 m.
        /// Linear: these are weights, not colours.
        /// </summary>
        public static Texture2D BuildSplatMap(IslandShape shape)
        {
            int size = ColourMapSize;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float wx = -IslandShape.HalfSize + (x + 0.5f) / size * IslandShape.WorldSize;
                float wz = -IslandShape.HalfSize + (y + 0.5f) / size * IslandShape.WorldSize;
                pixels[y * size + x] = shape.GroundWeights(wx, wz);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Island splat",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        /// <summary>Metres of water at the deepest value of the depth map.</summary>
        public const float DepthMapRange = 9f;

        /// <summary>
        /// How deep the sea is over the island, about 2 m per texel: red = depth / DepthMapRange.
        /// The sea shader colours shallow water and paints foam on the shore from it, without
        /// needing the camera's depth texture.
        /// </summary>
        public static Texture2D BuildDepthMap(IslandShape shape)
        {
            const int size = 256;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float wx = -IslandShape.HalfSize + (x + 0.5f) / size * IslandShape.WorldSize;
                float wz = -IslandShape.HalfSize + (y + 0.5f) / size * IslandShape.WorldSize;
                float depth = IslandShape.WaterLevel - shape.Height(wx, wz);
                byte d = (byte)Mathf.RoundToInt(Mathf.Clamp01(depth / DepthMapRange) * 255f);
                // The outermost texels are open sea, so outside the map (clamped) is deep water.
                if (x == 0 || y == 0 || x == size - 1 || y == size - 1) d = 255;
                pixels[y * size + x] = new Color32(d, d, d, 255);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Sea depth",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>The island's colours, one texel per ~1 m, smoothed by bilinear filtering.</summary>
        public static Texture2D BuildColourMap(IslandShape shape)
        {
            int size = ColourMapSize;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float wx = -IslandShape.HalfSize + (x + 0.5f) / size * IslandShape.WorldSize;
                float wz = -IslandShape.HalfSize + (y + 0.5f) / size * IslandShape.WorldSize;
                pixels[y * size + x] = shape.GroundColour(wx, wz);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Island colours",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
