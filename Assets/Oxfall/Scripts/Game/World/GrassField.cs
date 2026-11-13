using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>
    /// Grass around the player. The ground near the player is split into square tiles; each tile is
    /// one mesh of grass, built when it comes within reach and destroyed when it leaves. The same tile
    /// always gets the same grass, and grass grows only where the ground is green. With the photo
    /// grass atlas each tuft is two crossed cards cut out of it; without, a fan of plain blades.
    /// </summary>
    public sealed class GrassField : MonoBehaviour
    {
        public const float TileSize = 24f;

        private IslandShape _shape;
        private Material _material;
        private bool _cards;
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

        public void Init(IslandShape shape, Material material, bool cards, Transform viewer)
        {
            _shape = shape;
            _material = material;
            _cards = cards;
            _viewer = viewer;
        }

        /// <summary>
        /// Tufts in the grass atlas (CC0 Bermuda grass from Poly Haven), roots at the bottom: their
        /// boxes in UV space, measured from the atlas's alpha.
        /// </summary>
        private static readonly Vector4[] Tufts =
        {
            new Vector4(0.049f, 0.455f, 0.188f, 0.582f),
            new Vector4(0.279f, 0.459f, 0.457f, 0.596f),
            new Vector4(0.535f, 0.449f, 0.719f, 0.643f),
            new Vector4(0.779f, 0.449f, 0.940f, 0.680f),
            new Vector4(0.043f, 0.010f, 0.170f, 0.318f),
            new Vector4(0.234f, 0.012f, 0.387f, 0.371f),
            new Vector4(0.420f, 0.006f, 0.617f, 0.316f),
            new Vector4(0.695f, 0.006f, 0.867f, 0.336f),
            new Vector4(0.353f, 0.685f, 0.473f, 0.973f),
            new Vector4(0.535f, 0.766f, 0.666f, 0.971f),
        };

        /// <summary>Blades in each tuft.</summary>
        private const int TuftBlades = 4;

        /// <summary>How far the grass reaches (0 turns it off) and how many tufts per square metre.</summary>
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

                if (_cards)
                {
                    AddTuftCards(new Vector3(lx, h - 0.03f, lz), yaw, size, amount, tint, leanX);
                    continue;
                }

                // A tuft: a few short blades fanning out from one spot, shorter where grass thins out.
                float tuftHeight = Mathf.Lerp(0.2f, 0.45f, size) * Mathf.Lerp(0.7f, 1f, amount);
                float u = (Mathf.Floor(tint * 8f) + 0.5f) / 8f;
                for (int k = 0; k < TuftBlades; k++)
                {
                    float angle = yaw + k * (Mathf.PI * 2f / TuftBlades) + leanX;
                    var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    var side = new Vector3(-outward.z, 0f, outward.x);
                    float height = tuftHeight * (0.75f + 0.25f * Mathf.Sin(k * 2.1f + leanZ * 6f));
                    float halfWidth = Mathf.Lerp(0.045f, 0.075f, size);
                    var root = new Vector3(lx, h - 0.03f, lz) + outward * 0.05f;
                    var tip = root + outward * (height * 0.45f) + Vector3.up * height;

                    int start = Vertices.Count;
                    Vertices.Add(root - side * halfWidth);
                    Vertices.Add(tip);
                    Vertices.Add(root + side * halfWidth);
                    // Normals point up so blades light like the ground they stand on.
                    Normals.Add(Vector3.up);
                    Normals.Add(Vector3.up);
                    Normals.Add(Vector3.up);
                    Uvs.Add(new Vector2(u, 0.03f));
                    Uvs.Add(new Vector2(u, 0.97f));
                    Uvs.Add(new Vector2(u, 0.03f));
                    Triangles.Add(start);
                    Triangles.Add(start + 1);
                    Triangles.Add(start + 2);
                }
            }

            var go = new GameObject($"Grass {key.x},{key.y}");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(x0, 0f, z0);
            // Blades for plain grass, cards (two triangles each) for photo grass.
            int blades = Triangles.Count / (_cards ? 6 : 3);
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

        /// <summary>A tuft from the atlas on two crossed upright cards, shorter where grass thins out.</summary>
        private static void AddTuftCards(Vector3 root, float yaw, float size, float amount, float pick, float lean)
        {
            var box = Tufts[Mathf.Min(Tufts.Length - 1, (int)(pick * Tufts.Length))];
            float aspect = (box.z - box.x) / (box.w - box.y);
            float height = Mathf.Lerp(0.28f, 0.55f, size) * Mathf.Lerp(0.65f, 1f, amount);
            float width = height * aspect;
            if (width > 0.6f)
            {
                width = 0.6f;
                height = width / aspect;
            }
            var tilt = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * (lean * 0.15f * height);
            for (int k = 0; k < 2; k++)
            {
                float angle = yaw + k * Mathf.PI * 0.5f;
                var half = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (width * 0.5f);
                var top = root + Vector3.up * height + tilt;
                int start = Vertices.Count;
                Vertices.Add(root - half);
                Vertices.Add(root + half);
                Vertices.Add(top + half);
                Vertices.Add(top - half);
                // Normals point up so tufts light like the ground they stand on.
                for (int n = 0; n < 4; n++) Normals.Add(Vector3.up);
                Uvs.Add(new Vector2(box.x, box.y));
                Uvs.Add(new Vector2(box.z, box.y));
                Uvs.Add(new Vector2(box.z, box.w));
                Uvs.Add(new Vector2(box.x, box.w));
                Triangles.Add(start);
                Triangles.Add(start + 2);
                Triangles.Add(start + 1);
                Triangles.Add(start);
                Triangles.Add(start + 3);
                Triangles.Add(start + 2);
            }
        }

        private void DestroyTile(Tile tile)
        {
            BladeCount -= tile.Blades;
            if (tile.Mesh != null) Destroy(tile.Mesh);
            Destroy(tile.Object);
        }
    }
}
