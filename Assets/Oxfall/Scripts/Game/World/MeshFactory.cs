using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>
    /// Low-poly meshes made in code: trees, rocks, plants, building pieces. Plants and rocks are
    /// smooth-shaded (normals averaged across gentle edges, sharp edges kept), which reads as soft
    /// and natural rather than faceted; building pieces stay flat. These are placeholders with the
    /// right size and collision until real models replace them.
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
            b.AddCylinder(BarkSubmesh, Vector3.zero, 0.34f, 0.12f, trunkHeight, 7, Rand(random, 0f, 60f));

            // Four tiers that droop a little and lean a little, so no two pines look stamped out.
            float y = 1.6f;
            float radius = 2.4f;
            float height = 2.9f;
            for (int i = 0; i < 4; i++)
            {
                float jitter = Rand(random, 0.88f, 1.12f);
                var offset = new Vector3(Rand(random, -0.12f, 0.12f), 0f, Rand(random, -0.12f, 0.12f));
                b.AddCone(LeavesSubmesh, new Vector3(0f, y, 0f) + offset, radius * jitter, height, 9, Rand(random, 0f, 60f));
                y += height * 0.5f;
                radius *= 0.76f;
                height *= 0.88f;
            }
            return b.Build("Pine", 80f);
        }

        /// <summary>A broadleaf tree: a trunk and a lumpy crown.</summary>
        public static Mesh Broadleaf(System.Random random)
        {
            var b = new FlatMeshBuilder(2);
            float trunkHeight = 4.2f;
            b.AddCylinder(BarkSubmesh, Vector3.zero, 0.3f, 0.2f, trunkHeight, 6, Rand(random, 0f, 60f));
            // A crown of several overlapping clumps reads as foliage rather than one ball on a stick.
            var crownSize = new Vector3(Rand(random, 2.2f, 2.7f), Rand(random, 1.7f, 2.1f), Rand(random, 2.2f, 2.7f));
            float crownY = trunkHeight + crownSize.y * 0.45f;
            b.AddBlob(LeavesSubmesh, new Vector3(0f, crownY + 0.5f, 0f), crownSize * 0.85f, random, 0.18f);
            for (int i = 0; i < 4; i++)
            {
                float angle = (i * 90f + Rand(random, -25f, 25f)) * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Rand(random, 1.1f, 1.5f);
                b.AddBlob(LeavesSubmesh, new Vector3(offset.x, crownY + Rand(random, -0.5f, 0.3f), offset.z),
                    crownSize * Rand(random, 0.55f, 0.7f), random, 0.2f);
            }
            // Two short boughs carry the side clumps.
            for (int i = 0; i < 2; i++)
            {
                var lean = Quaternion.Euler(Rand(random, 35f, 50f), i * 180f + Rand(random, -30f, 30f), 0f);
                b.AddCylinder(BarkSubmesh, new Vector3(0f, trunkHeight - 0.9f, 0f), 0.13f, 0.06f, 1.5f, 5, 0f, lean);
            }
            return b.Build("Broadleaf", 75f);
        }

        /// <summary>A boulder, about a metre across before scaling.</summary>
        public static Mesh Rock(System.Random random)
        {
            var b = new FlatMeshBuilder(1);
            b.AddBlob(0, new Vector3(0f, 0.25f, 0f), new Vector3(1f, 0.7f, 1f), random, 0.28f);
            return b.Build("Rock", 42f);
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
            return b.Build("Hemp", 70f);
        }

        /// <summary>The rock in the player's hand.</summary>
        public static Mesh HandRock()
        {
            var b = new FlatMeshBuilder(1);
            b.AddBlob(0, Vector3.zero, new Vector3(1f, 0.8f, 1.2f), new System.Random(7), 0.2f);
            return b.Build("HandRock", 42f);
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
            private readonly List<float> _areas = new List<float>();
            private readonly List<int> _submeshOf = new List<int>();
            private readonly List<int>[] _triangles;

            public FlatMeshBuilder(int submeshes)
            {
                _triangles = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++) _triangles[i] = new List<int>();
            }

            public void Triangle(int submesh, Vector3 a, Vector3 b, Vector3 c)
            {
                var normal = Vector3.Cross(b - a, c - a);
                float area = normal.magnitude;
                if (area < 1e-6f) return;
                normal /= area;
                int start = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _normals.Add(normal); _normals.Add(normal); _normals.Add(normal);
                _areas.Add(area); _areas.Add(area); _areas.Add(area);
                _submeshOf.Add(submesh); _submeshOf.Add(submesh); _submeshOf.Add(submesh);
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

            /// <param name="smoothAngle">
            /// 0 keeps every face flat. Otherwise corners that meet at the same point share a normal
            /// with the faces whose normals are within this many degrees of their own.
            /// </param>
            public Mesh Build(string name, float smoothAngle = 0f)
            {
                if (smoothAngle > 0f) SmoothNormals(smoothAngle);
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

            private void SmoothNormals(float maxAngle)
            {
                float minDot = Mathf.Cos(maxAngle * Mathf.Deg2Rad);
                var faceNormals = new List<Vector3>(_normals);
                var corners = new Dictionary<(Vector3Int, int), List<int>>();
                for (int i = 0; i < _vertices.Count; i++)
                {
                    var p = _vertices[i];
                    var key = (new Vector3Int(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f),
                        Mathf.RoundToInt(p.z * 1000f)), _submeshOf[i]);
                    if (!corners.TryGetValue(key, out var list)) corners[key] = list = new List<int>(6);
                    list.Add(i);
                }
                foreach (var list in corners.Values)
                {
                    foreach (int i in list)
                    {
                        var sum = Vector3.zero;
                        foreach (int j in list)
                            if (Vector3.Dot(faceNormals[i], faceNormals[j]) >= minDot) sum += faceNormals[j] * _areas[j];
                        if (sum.sqrMagnitude > 1e-12f) _normals[i] = sum.normalized;
                    }
                }
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
