using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>
    /// Grass blades around the player. The ground near the player is split into square tiles; each
    /// tile is one mesh of blades, built when it comes within reach and destroyed when it leaves.
    /// The same tile always gets the same blades, and blades grow only where the ground is green.
    /// </summary>
    public sealed class GrassField : MonoBehaviour
    {
        public const float TileSize = 24f;

        private IslandShape _shape;
        private Material _material;
        private Transform _viewer;
        private float _radius;
        private float _density;

        private readonly Dictionary<Vector2Int, Tile> _tiles = new Dictionary<Vector2Int, Tile>();

        private struct Tile
        {
            public GameObject Object;
            public Mesh Mesh;
            public int Blades;
        }
        private readonly List<Vector2Int> _leaving = new List<Vector2Int>();
        private Vector2Int _centre = new Vector2Int(int.MinValue, int.MinValue);

        private static readonly List<Vector3> Vertices = new List<Vector3>();
        private static readonly List<Vector3> Normals = new List<Vector3>();
        private static readonly List<Vector2> Uvs = new List<Vector2>();
        private static readonly List<int> Triangles = new List<int>();

        public int BladeCount { get; private set; }

        public void Init(IslandShape shape, Material material, Transform viewer)
        {
            _shape = shape;
            _material = material;
            _viewer = viewer;
        }

        /// <summary>How far the grass reaches (0 turns it off) and how many blades per square metre.</summary>
        public void SetQuality(float radius, float density)
        {
            if (Mathf.Approximately(radius, _radius) && Mathf.Approximately(density, _density)) return;
            _radius = radius;
            _density = density;
            foreach (var tile in _tiles.Values) DestroyTile(tile);
            _tiles.Clear();
            BladeCount = 0;
            _centre = new Vector2Int(int.MinValue, int.MinValue);
        }

        private void LateUpdate()
        {
            if (_viewer == null || _radius <= 0f) return;
            var p = _viewer.position;
            var centre = new Vector2Int(Mathf.FloorToInt(p.x / TileSize), Mathf.FloorToInt(p.z / TileSize));
            if (centre == _centre) return;
            _centre = centre;

            _leaving.Clear();
            foreach (var pair in _tiles)
                if (!InReach(pair.Key, p, _radius + TileSize * 0.5f)) _leaving.Add(pair.Key);
            foreach (var key in _leaving)
            {
                DestroyTile(_tiles[key]);
                _tiles.Remove(key);
            }

            int reach = Mathf.CeilToInt(_radius / TileSize);
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                var key = new Vector2Int(centre.x + dx, centre.y + dz);
                if (_tiles.ContainsKey(key) || !InReach(key, p, _radius)) continue;
                _tiles[key] = BuildTile(key);
            }
        }

        /// <summary>Whether the nearest point of a tile is within a distance of a position.</summary>
        private static bool InReach(Vector2Int key, Vector3 p, float distance)
        {
            float x0 = key.x * TileSize, z0 = key.y * TileSize;
            float dx = Mathf.Max(x0 - p.x, 0f, p.x - (x0 + TileSize));
            float dz = Mathf.Max(z0 - p.z, 0f, p.z - (z0 + TileSize));
            return dx * dx + dz * dz <= distance * distance;
        }

        private Tile BuildTile(Vector2Int key)
        {
            var random = new System.Random(unchecked(key.x * 73856093 ^ key.y * 19349663 ^ _shape.Seed * 83492791));
            float x0 = key.x * TileSize, z0 = key.y * TileSize;
            int attempts = Mathf.RoundToInt(TileSize * TileSize * _density);

            Vertices.Clear();
            Normals.Clear();
            Uvs.Clear();
            Triangles.Clear();

            for (int i = 0; i < attempts; i++)
            {
                float lx = (float)random.NextDouble() * TileSize;
                float lz = (float)random.NextDouble() * TileSize;
                float roll = (float)random.NextDouble();
                float yaw = (float)random.NextDouble() * Mathf.PI * 2f;
                float size = (float)random.NextDouble();
                float tint = (float)random.NextDouble();
                float leanX = (float)random.NextDouble() - 0.5f;
                float leanZ = (float)random.NextDouble() - 0.5f;

                float x = x0 + lx, z = z0 + lz;
                float h = _shape.Height(x, z);
                float amount = _shape.GrassAmount(x, z, h);
                if (roll >= amount) continue;

                // Thin tapered blades, shorter where the grass thins out.
                float height = Mathf.Lerp(0.28f, 0.7f, size) * Mathf.Lerp(0.6f, 1f, amount);
                float halfWidth = Mathf.Lerp(0.035f, 0.06f, size);
                var right = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * halfWidth;
                var root = new Vector3(lx, h - 0.04f, lz);
                var tip = root + new Vector3(leanX * height * 0.6f, height, leanZ * height * 0.6f);

                int start = Vertices.Count;
                Vertices.Add(root - right);
                Vertices.Add(tip);
                Vertices.Add(root + right);
                // Normals point up so blades light like the ground they stand on.
                Normals.Add(Vector3.up);
                Normals.Add(Vector3.up);
                Normals.Add(Vector3.up);
                float u = (Mathf.Floor(tint * 8f) + 0.5f) / 8f;
                Uvs.Add(new Vector2(u, 0.03f));
                Uvs.Add(new Vector2(u, 0.97f));
                Uvs.Add(new Vector2(u, 0.03f));
                Triangles.Add(start);
                Triangles.Add(start + 1);
                Triangles.Add(start + 2);
            }

            var go = new GameObject($"Grass {key.x},{key.y}");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(x0, 0f, z0);
            int blades = Triangles.Count / 3;
            BladeCount += blades;
            if (blades == 0) return new Tile { Object = go };

            var mesh = new Mesh { name = go.name };
            if (Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return new Tile { Object = go, Mesh = mesh, Blades = blades };
        }

        private void DestroyTile(Tile tile)
        {
            BladeCount -= tile.Blades;
            if (tile.Mesh != null) Destroy(tile.Mesh);
            Destroy(tile.Object);
        }
    }
}
