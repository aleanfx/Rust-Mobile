using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>
    /// Low-poly, flat-shaded meshes made in code: trees, rocks, plants. Flat shading reads well at
    /// phone resolution, costs almost nothing, and gives the stylised look without textures.
    /// These are placeholders with the right size and collision until real models replace them.
    /// </summary>
    public static class MeshFactory
    {
        public const int BarkSubmesh = 0;
        public const int LeavesSubmesh = 1;

        /// <summary>A pine: a trunk and three stacked cones of needles.</summary>
        public static Mesh Pine(System.Random random)
        {
            var b = new FlatMeshBuilder(2);
            float trunkHeight = 7.5f;
            b.AddCylinder(BarkSubmesh, Vector3.zero, 0.32f, 0.12f, trunkHeight, 6, Rand(random, 0f, 60f));

            float y = 1.9f;
            float radius = 2.3f;
            float height = 3.4f;
            for (int i = 0; i < 3; i++)
            {
                float jitter = Rand(random, 0.9f, 1.1f);
                b.AddCone(LeavesSubmesh, new Vector3(0f, y, 0f), radius * jitter, height, 7, Rand(random, 0f, 60f));
                y += height * 0.55f;
                radius *= 0.72f;
                height *= 0.85f;
            }
            return b.Build("Pine");
        }

        /// <summary>A broadleaf tree: a trunk and a lumpy crown.</summary>
        public static Mesh Broadleaf(System.Random random)
        {
            var b = new FlatMeshBuilder(2);
            float trunkHeight = 4.2f;
            b.AddCylinder(BarkSubmesh, Vector3.zero, 0.3f, 0.2f, trunkHeight, 6, Rand(random, 0f, 60f));
            var crownSize = new Vector3(Rand(random, 2.4f, 3.0f), Rand(random, 2.0f, 2.6f), Rand(random, 2.4f, 3.0f));
            b.AddBlob(LeavesSubmesh, new Vector3(0f, trunkHeight + crownSize.y * 0.55f, 0f), crownSize, random, 0.22f);
            b.AddBlob(LeavesSubmesh, new Vector3(Rand(random, -1f, 1f), trunkHeight + 0.2f, Rand(random, -1f, 1f)),
                crownSize * 0.6f, random, 0.2f);
            return b.Build("Broadleaf");
        }

        /// <summary>A boulder, about a metre across before scaling.</summary>
        public static Mesh Rock(System.Random random)
        {
            var b = new FlatMeshBuilder(1);
            b.AddBlob(0, new Vector3(0f, 0.25f, 0f), new Vector3(1f, 0.7f, 1f), random, 0.28f);
            return b.Build("Rock");
        }

        /// <summary>A hemp plant: a few stalks topped with leaves.</summary>
        public static Mesh Hemp(System.Random random)
        {
            var b = new FlatMeshBuilder(1);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f + Rand(random, -20f, 20f);
                var lean = Quaternion.Euler(Rand(random, 6f, 14f), angle, 0f);
                var top = lean * new Vector3(0f, Rand(random, 1.0f, 1.35f), 0f);
                b.AddCylinder(0, Vector3.zero, 0.035f, 0.025f, top.magnitude, 4, angle, lean);
                b.AddCone(0, top - new Vector3(0f, 0.35f, 0f), 0.22f, 0.6f, 5, angle);
            }
            return b.Build("Hemp");
        }

        /// <summary>The rock in the player's hand.</summary>
        public static Mesh HandRock()
        {
            var b = new FlatMeshBuilder(1);
            b.AddBlob(0, Vector3.zero, new Vector3(1f, 0.8f, 1.2f), new System.Random(7), 0.2f);
            return b.Build("HandRock");
        }

        /// <summary>One flat-shaded mesh made of boxes. Building pieces are made of these.</summary>
        public static Mesh Boxes(string name, params Bounds[] boxes)
        {
            var b = new FlatMeshBuilder(1);
            foreach (var box in boxes) b.AddBox(0, box.min, box.max);
            return b.Build(name);
        }

        /// <summary>A flat square centred on the origin, facing up.</summary>
        public static Mesh Quad(float size, float uvTiles)
        {
            float h = size * 0.5f;
            var mesh = new Mesh { name = "Quad" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h),
            });
            mesh.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetUVs(0, new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(0f, uvTiles), new Vector2(uvTiles, uvTiles), new Vector2(uvTiles, 0f),
            });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float Rand(System.Random random, float min, float max) =>
            min + (float)random.NextDouble() * (max - min);

        /// <summary>
        /// Builds a mesh where every triangle has its own vertices and a face normal: the flat,
        /// faceted look.
        /// </summary>
        private sealed class FlatMeshBuilder
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int>[] _triangles;

            public FlatMeshBuilder(int submeshes)
            {
                _triangles = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++) _triangles[i] = new List<int>();
            }

            public void Triangle(int submesh, Vector3 a, Vector3 b, Vector3 c)
            {
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f) return;
                normal.Normalize();
                int start = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _normals.Add(normal); _normals.Add(normal); _normals.Add(normal);
                _uvs.Add(new Vector2(0f, 0f)); _uvs.Add(new Vector2(1f, 0f)); _uvs.Add(new Vector2(0.5f, 1f));
                var list = _triangles[submesh];
                list.Add(start); list.Add(start + 1); list.Add(start + 2);
            }

            /// <summary>An axis-aligned box, every face wound to face outwards.</summary>
            public void AddBox(int submesh, Vector3 lo, Vector3 hi)
            {
                Vector3 P(float x, float y, float z) => new Vector3(x, y, z);
                Quad(submesh, P(lo.x, hi.y, lo.z), P(lo.x, hi.y, hi.z), P(hi.x, hi.y, hi.z), P(hi.x, hi.y, lo.z)); // top
                Quad(submesh, P(lo.x, lo.y, lo.z), P(hi.x, lo.y, lo.z), P(hi.x, lo.y, hi.z), P(lo.x, lo.y, hi.z)); // bottom
                Quad(submesh, P(hi.x, lo.y, lo.z), P(hi.x, hi.y, lo.z), P(hi.x, hi.y, hi.z), P(hi.x, lo.y, hi.z)); // +X
                Quad(submesh, P(lo.x, lo.y, lo.z), P(lo.x, lo.y, hi.z), P(lo.x, hi.y, hi.z), P(lo.x, hi.y, lo.z)); // -X
                Quad(submesh, P(lo.x, lo.y, hi.z), P(hi.x, lo.y, hi.z), P(hi.x, hi.y, hi.z), P(lo.x, hi.y, hi.z)); // +Z
                Quad(submesh, P(lo.x, lo.y, lo.z), P(lo.x, hi.y, lo.z), P(hi.x, hi.y, lo.z), P(hi.x, lo.y, lo.z)); // -Z
            }

            private void Quad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Triangle(submesh, a, b, c);
                Triangle(submesh, a, c, d);
            }

            public void AddCone(int submesh, Vector3 baseCentre, float radius, float height, int sides, float yawDegrees)
            {
                var apex = baseCentre + new Vector3(0f, height, 0f);
                for (int i = 0; i < sides; i++)
                {
                    var p0 = baseCentre + Ring(i, sides, radius, yawDegrees);
                    var p1 = baseCentre + Ring(i + 1, sides, radius, yawDegrees);
                    Triangle(submesh, p0, apex, p1);
                    Triangle(submesh, p1, baseCentre, p0);
                }
            }

            public void AddCylinder(int submesh, Vector3 baseCentre, float bottomRadius, float topRadius, float height,
                int sides, float yawDegrees)
            {
                AddCylinder(submesh, baseCentre, bottomRadius, topRadius, height, sides, yawDegrees, Quaternion.identity);
            }

            public void AddCylinder(int submesh, Vector3 baseCentre, float bottomRadius, float topRadius, float height,
                int sides, float yawDegrees, Quaternion rotation)
            {
                var top = new Vector3(0f, height, 0f);
                for (int i = 0; i < sides; i++)
                {
                    var b0 = baseCentre + rotation * Ring(i, sides, bottomRadius, yawDegrees);
                    var b1 = baseCentre + rotation * Ring(i + 1, sides, bottomRadius, yawDegrees);
                    var t0 = baseCentre + rotation * (top + Ring(i, sides, topRadius, yawDegrees));
                    var t1 = baseCentre + rotation * (top + Ring(i + 1, sides, topRadius, yawDegrees));
                    Triangle(submesh, b0, t0, t1);
                    Triangle(submesh, b0, t1, b1);
                }
            }

            /// <summary>A lumpy, squashed sphere (a subdivided icosahedron with its corners pushed about).</summary>
            public void AddBlob(int submesh, Vector3 centre, Vector3 size, System.Random random, float lumpiness)
            {
                Icosphere(out var points, out var faces);
                for (int i = 0; i < points.Count; i++)
                {
                    float push = 1f + ((float)random.NextDouble() * 2f - 1f) * lumpiness;
                    var p = points[i] * push;
                    points[i] = centre + new Vector3(p.x * size.x, p.y * size.y, p.z * size.z);
                }
                for (int f = 0; f < faces.Count; f += 3)
                    Triangle(submesh, points[faces[f]], points[faces[f + 1]], points[faces[f + 2]]);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.subMeshCount = _triangles.Length;
                for (int i = 0; i < _triangles.Length; i++) mesh.SetTriangles(_triangles[i], i);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }

            private static Vector3 Ring(int i, int sides, float radius, float yawDegrees)
            {
                float a = (i % sides) * Mathf.PI * 2f / sides + yawDegrees * Mathf.Deg2Rad;
                return new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }

            private static void Icosphere(out List<Vector3> points, out List<int> faces)
            {
                float t = (1f + Mathf.Sqrt(5f)) / 2f;
                points = new List<Vector3>
                {
                    new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                    new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                    new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
                };
                for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;
                var baseFaces = new List<int>
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                    1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                    4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
                };

                // One subdivision: 80 faces, enough to look like a rock and still cheap.
                faces = new List<int>(baseFaces.Count * 4);
                var midpoints = new Dictionary<long, int>();
                for (int f = 0; f < baseFaces.Count; f += 3)
                {
                    int a = baseFaces[f], b = baseFaces[f + 1], c = baseFaces[f + 2];
                    int ab = Midpoint(points, midpoints, a, b);
                    int bc = Midpoint(points, midpoints, b, c);
                    int ca = Midpoint(points, midpoints, c, a);
                    faces.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
            }

            private static int Midpoint(List<Vector3> points, Dictionary<long, int> cache, int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int index)) return index;
                points.Add(((points[a] + points[b]) * 0.5f).normalized);
                index = points.Count - 1;
                cache[key] = index;
                return index;
            }
        }
    }
}
