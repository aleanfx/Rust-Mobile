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
        /// <summary>Foliage cards: alpha-cut photo leaves, in trees made with cards.</summary>
        public const int CardSubmesh = 2;

        /// <summary>
        /// A leafy frond in the foliage atlas (the CC0 Jacaranda leaves from Poly Haven): its box in
        /// UV space and which end the stem starts from. Measured from the alpha of the atlas.
        /// </summary>
        private readonly struct Frond
        {
            private readonly float _u0, _u1, _v0, _v1;
            private readonly bool _stemOnRight;

            public Frond(float u0, float v0, float u1, float v1, bool stemOnRight)
            {
                _u0 = u0; _v0 = v0; _u1 = u1; _v1 = v1;
                _stemOnRight = stemOnRight;
            }

            /// <summary>Length over width.</summary>
            public float Aspect => (_u1 - _u0) / (_v1 - _v0);

            /// <summary>The UV at a point across (0..1) and along (0 = stem, 1 = tip) the frond.</summary>
            public Vector2 Uv(float across, float along) => new Vector2(
                _stemOnRight ? _u1 - along * (_u1 - _u0) : _u0 + along * (_u1 - _u0),
                _v0 + across * (_v1 - _v0));
        }

        private static readonly Frond[] LongFronds =
        {
            new Frond(0.119f, 0.500f, 0.988f, 0.973f, false),
            new Frond(0.199f, 0.006f, 1.000f, 0.422f, true),
        };
        private static readonly Frond[] AllFronds =
        {
            LongFronds[0], LongFronds[1],
            new Frond(0.000f, 0.281f, 0.459f, 0.762f, false),
        };

        /// <summary>
        /// A pine. With cards: a trunk, a dark core and whorls of drooping photo-leaf branches.
        /// Without: a trunk and stacked cones of needles.
        /// </summary>
        public static Mesh Pine(System.Random random, bool cards = false)
        {
            if (cards) return CardPine(random);

            var b = new FlatMeshBuilder(2) { UvScale = 1.3f };
            float trunkHeight = 7.5f;
            b.AddCylinder(BarkSubmesh, Vector3.zero, 0.34f, 0.12f, trunkHeight, 7, Rand(random, 0f, 60f));

            // Six overlapping tiers, narrowing to the top and leaning a little, so the crown reads as
            // a dense conifer rather than stacked cones, and no two pines look stamped out.
            float y = 1.3f;
            float radius = 2.3f;
            float height = 2.2f;
            for (int i = 0; i < 6; i++)
            {
                float jitter = Rand(random, 0.85f, 1.15f);
                var offset = new Vector3(Rand(random, -0.1f, 0.1f), 0f, Rand(random, -0.1f, 0.1f));
                b.AddCone(LeavesSubmesh, new Vector3(0f, y, 0f) + offset, radius * jitter, height, 9, Rand(random, 0f, 60f));
                y += height * 0.48f;
                radius *= 0.82f;
                height *= 0.92f;
            }
            return b.Build("Pine", 80f);
        }

        private static Mesh CardPine(System.Random random)
        {
            var b = new FlatMeshBuilder(3) { UvScale = 1.3f };
            float height = Rand(random, 9f, 10.5f);
            b.AddCylinder(BarkSubmesh, Vector3.zero, 0.32f, 0.05f, height, 7, Rand(random, 0f, 60f));

            float crownBottom = 1.8f, crownTop = height - 0.6f;
            // A dark core keeps the crown from looking hollow between the branches.
            b.AddCone(LeavesSubmesh, new Vector3(0f, crownBottom + 0.3f, 0f), 1.2f, crownTop - crownBottom, 7,
                Rand(random, 0f, 60f));

            const int whorls = 11;
            for (int w = 0; w < whorls; w++)
            {
                float t = w / (whorls - 1f);
                float y = Mathf.Lerp(crownBottom, crownTop, t) + Rand(random, -0.15f, 0.15f);
                float length = Mathf.Lerp(3.0f, 0.9f, Mathf.Pow(t, 0.9f)) * Rand(random, 0.85f, 1.1f);
                int branches = t < 0.7f ? 6 : 5;
                float whorlYaw = Rand(random, 0f, 360f);
                for (int k = 0; k < branches; k++)
                {
                    float yaw = (whorlYaw + k * 360f / branches + Rand(random, -18f, 18f)) * Mathf.Deg2Rad;
                    float droop = (Mathf.Lerp(24f, 6f, t) + Rand(random, -6f, 6f)) * Mathf.Deg2Rad;
                    var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                    var dir = outward * Mathf.Cos(droop) + Vector3.down * Mathf.Sin(droop);
                    var flatSide = Vector3.Cross(Vector3.up, dir).normalized;
                    var start = new Vector3(0f, y, 0f) + outward * 0.1f;
                    var frond = LongFronds[random.Next(LongFronds.Length)];
                    float width = length / frond.Aspect * 0.85f;
                    float bend = Rand(random, 8f, 18f);
                    // A spray lying nearly flat and, on the long lower branches, a steeper one under it
                    // so the branch has body seen from the side.
                    var side = Quaternion.AngleAxis(Rand(random, -20f, 20f), dir) * flatSide;
                    Card(b, start, dir, bend, side, length, width, frond, Vector3.zero, true, 0.55f);
                    if (t >= 0.6f) continue;
                    side = Quaternion.AngleAxis(Rand(random, 65f, 110f), dir) * flatSide;
                    frond = LongFronds[random.Next(LongFronds.Length)];
                    Card(b, start, dir, bend, side, length * 0.85f, width * 0.7f, frond, Vector3.zero, true, 0.55f);
                }
            }

            // The leader: two crossed upright sprays at the very top.
            var top = new Vector3(0f, crownTop - 0.3f, 0f);
            Card(b, top, Vector3.up, 0f, Vector3.right, 1.5f, 0.8f, LongFronds[0], Vector3.zero, true, 1f);
            Card(b, top, Vector3.up, 0f, Vector3.forward, 1.4f, 0.75f, LongFronds[1], Vector3.zero, true, 1f);
            return b.Build("Pine", 80f);
        }

        /// <summary>
        /// One foliage card: a strip of two quads from the stem end along a direction, the outer half
        /// bending down by some degrees. Normals point away from a centre (or from the trunk's axis,
        /// when axial) and up, so the crown is lit like one soft volume instead of flat cards.
        /// </summary>
        private static void Card(FlatMeshBuilder b, Vector3 start, Vector3 dir, float bendDegrees, Vector3 side,
            float length, float width, Frond frond, Vector3 centre, bool axial, float upBias)
        {
            var bendAxis = Vector3.Cross(Vector3.up, dir);
            bendAxis = bendAxis.sqrMagnitude > 1e-4f ? bendAxis.normalized : side;
            var outer = Quaternion.AngleAxis(bendDegrees, bendAxis) * dir;
            var half = side.normalized * (width * 0.5f);
            var mid = start + dir * (length * 0.5f);
            var tip = mid + outer * (length * 0.5f);
            var spine = new[] { start, mid, tip };

            int first = -1;
            for (int r = 0; r < 3; r++)
            {
                float along = r * 0.5f;
                for (int c = 0; c < 2; c++)
                {
                    var p = spine[r] + (c == 0 ? -half : half);
                    var from = axial ? new Vector3(0f, p.y, 0f) : centre;
                    var away = p - from;
                    if (axial) away.y = 0f;
                    if (away.sqrMagnitude < 0.04f) away = new Vector3(dir.x, 0f, dir.z);
                    var normal = (away.normalized + Vector3.up * upBias).normalized;
                    int index = b.CardVertex(p, normal, frond.Uv(c, along));
                    if (first < 0) first = index;
                }
            }
            for (int r = 0; r < 2; r++)
            {
                int l0 = first + r * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                b.CardTriangle(CardSubmesh, l0, r0, r1);
                b.CardTriangle(CardSubmesh, l0, r1, l1);
            }
        }

        /// <summary>
        /// A broadleaf tree. With cards: a trunk, boughs, dark inner clumps and photo-leaf sprays
        /// around them. Without: a trunk and a lumpy crown.
        /// </summary>
        public static Mesh Broadleaf(System.Random random, bool cards = false)
        {
            if (cards) return CardBroadleaf(random);

            var b = new FlatMeshBuilder(2) { UvScale = 1.3f };
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

        private static Mesh CardBroadleaf(System.Random random)
        {
            var b = new FlatMeshBuilder(3) { UvScale = 1.3f };
            float trunkHeight = Rand(random, 3.8f, 4.5f);
            b.AddCylinder(BarkSubmesh, Vector3.zero, 0.3f, 0.14f, trunkHeight + 0.8f, 6, Rand(random, 0f, 60f));

            var crown = new Vector3(Rand(random, 2.4f, 2.9f), Rand(random, 1.8f, 2.2f), Rand(random, 2.4f, 2.9f));
            var centre = new Vector3(0f, trunkHeight + crown.y * 0.45f, 0f);
            var clumpCentres = new List<Vector3> { centre + new Vector3(0f, 0.5f, 0f) };
            var clumpSizes = new List<Vector3> { crown * 0.85f };
            for (int i = 0; i < 4; i++)
            {
                float angle = (i * 90f + Rand(random, -25f, 25f)) * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Rand(random, 1.1f, 1.5f);
                var c = new Vector3(offset.x, centre.y + Rand(random, -0.5f, 0.3f), offset.z);
                clumpCentres.Add(c);
                clumpSizes.Add(crown * Rand(random, 0.55f, 0.7f));
                // A bough from the trunk out to the clump.
                var from = new Vector3(0f, trunkHeight - 0.6f, 0f);
                var to = c - new Vector3(0f, 0.3f, 0f);
                var lean = Quaternion.FromToRotation(Vector3.up, to - from);
                b.AddCylinder(BarkSubmesh, from, 0.12f, 0.05f, (to - from).magnitude, 5, 0f, lean);
            }

            for (int i = 0; i < clumpCentres.Count; i++)
            {
                var c = clumpCentres[i];
                var size = clumpSizes[i];
                // Dark inner clump: the shade inside the crown, seen through gaps between the sprays.
                b.AddBlob(LeavesSubmesh, c, size * 0.6f, random, 0.15f);
                int count = i == 0 ? 16 : 9;
                for (int j = 0; j < count; j++)
                {
                    var d = RandomDirection(random);
                    d.y = d.y * 0.7f + 0.2f;
                    d.Normalize();
                    var p = c + Vector3.Scale(d, size) * Rand(random, 0.45f, 0.75f);
                    // Sprays point outwards and a little down, faces turned roughly to the sky.
                    var flat = new Vector3(d.x, 0f, d.z);
                    if (flat.sqrMagnitude < 0.05f) flat = RandomDirection(random);
                    flat.y = 0f;
                    var dir = (flat.normalized + Vector3.down * Rand(random, 0.1f, 0.45f)).normalized;
                    var side = Quaternion.AngleAxis(Rand(random, -50f, 50f), dir) * Vector3.Cross(Vector3.up, dir).normalized;
                    var frond = AllFronds[random.Next(AllFronds.Length)];
                    float length = Rand(random, 1.4f, 2.0f) * (frond.Aspect < 1.2f ? 0.7f : 1f);
                    float width = length / frond.Aspect;
                    Card(b, p - dir * (length * 0.35f), dir, Rand(random, 5f, 20f), side, length, width, frond,
                        centre, false, 0.35f);
                }
            }
            return b.Build("Broadleaf", 75f);
        }

        private static Vector3 RandomDirection(System.Random random)
        {
            while (true)
            {
                var v = new Vector3(Rand(random, -1f, 1f), Rand(random, -1f, 1f), Rand(random, -1f, 1f));
                float m = v.sqrMagnitude;
                if (m > 0.01f && m <= 1f) return v / Mathf.Sqrt(m);
            }
        }

        /// <summary>
        /// A boulder, about a metre across before scaling: a finer sphere bent by smooth noise and
        /// split by a few flat fracture faces, so it reads as weathered stone, not a faceted ball.
        /// </summary>
        public static Mesh Rock(System.Random random)
        {
            var b = new FlatMeshBuilder(1) { UvScale = 1.1f };
            b.AddRock(0, new Vector3(0f, 0.25f, 0f), new Vector3(1f, 0.7f, 1f), random);
            return b.Build("Rock", 50f);
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
            var b = new FlatMeshBuilder(1) { UvScale = 1.6f };
            b.AddRock(0, Vector3.zero, new Vector3(1f, 0.8f, 1.2f), new System.Random(7));
            return b.Build("HandRock", 50f);
        }

        /// <summary>One flat-shaded mesh made of boxes. Building pieces are made of these.</summary>
        public static Mesh Boxes(string name, params Bounds[] boxes)
        {
            // About eight planks per 1.6 m tile of the planks texture.
            var b = new FlatMeshBuilder(1) { UvScale = 1.6f };
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

            // Vertices with their own normals and UVs (foliage cards), kept out of the smoothing and
            // appended when the mesh is built.
            private readonly List<Vector3> _cardVertices = new List<Vector3>();
            private readonly List<Vector3> _cardNormals = new List<Vector3>();
            private readonly List<Vector2> _cardUvs = new List<Vector2>();
            private readonly List<int> _cardTriangles = new List<int>();
            private readonly List<int> _cardSubmesh = new List<int>();

            /// <summary>
            /// Metres per texture tile. Above 0, each face gets UVs projected along its main axis
            /// (from above, or from the side with "up" up), and the mesh gets tangents for normal maps.
            /// </summary>
            public float UvScale;

            public FlatMeshBuilder(int submeshes)
            {
                _triangles = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++) _triangles[i] = new List<int>();
            }

            /// <summary>A vertex for a card, with its own normal and UV. Returns its index among card vertices.</summary>
            public int CardVertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                _cardVertices.Add(position);
                _cardNormals.Add(normal);
                _cardUvs.Add(uv);
                return _cardVertices.Count - 1;
            }

            /// <summary>A triangle between three card vertices.</summary>
            public void CardTriangle(int submesh, int a, int b, int c)
            {
                _cardTriangles.Add(a); _cardTriangles.Add(b); _cardTriangles.Add(c);
                _cardSubmesh.Add(submesh);
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
                if (UvScale > 0f)
                {
                    _uvs.Add(Project(a, normal)); _uvs.Add(Project(b, normal)); _uvs.Add(Project(c, normal));
                }
                else
                {
                    _uvs.Add(new Vector2(0f, 0f)); _uvs.Add(new Vector2(1f, 0f)); _uvs.Add(new Vector2(0.5f, 1f));
                }
                var list = _triangles[submesh];
                list.Add(start); list.Add(start + 1); list.Add(start + 2);
            }

            private Vector2 Project(Vector3 p, Vector3 normal)
            {
                float ax = Mathf.Abs(normal.x), ay = Mathf.Abs(normal.y), az = Mathf.Abs(normal.z);
                Vector2 uv;
                if (ay >= ax && ay >= az) uv = new Vector2(p.x, p.z);
                else if (ax >= az) uv = new Vector2(p.z, p.y);
                else uv = new Vector2(p.x, p.y);
                return uv / UvScale;
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

            /// <summary>
            /// A stone: a twice-subdivided icosahedron pushed in and out by smooth noise, with a few
            /// fracture planes that cut it flat where they pass.
            /// </summary>
            public void AddRock(int submesh, Vector3 centre, Vector3 size, System.Random random)
            {
                Icosphere(2, out var points, out var faces);
                var seed = new Vector3(Rand(random, 0f, 100f), Rand(random, 0f, 100f), Rand(random, 0f, 100f));
                int cuts = 3 + random.Next(3);
                var planes = new Vector4[cuts];
                for (int c = 0; c < cuts; c++)
                {
                    var n = RandomDirection(random);
                    planes[c] = new Vector4(n.x, n.y, n.z, Rand(random, 0.62f, 0.85f));
                }
                for (int i = 0; i < points.Count; i++)
                {
                    var p = points[i];
                    float shape = Noise3(p * 1.3f + seed) - 0.5f;
                    float bumps = Noise3(p * 3.4f + seed * 1.7f) - 0.5f;
                    p *= 1f + shape * 0.5f + bumps * 0.14f;
                    foreach (var plane in planes)
                    {
                        var n = new Vector3(plane.x, plane.y, plane.z);
                        float d = Vector3.Dot(p, n);
                        if (d > plane.w) p -= n * (d - plane.w);
                    }
                    points[i] = centre + new Vector3(p.x * size.x, p.y * size.y, p.z * size.z);
                }
                for (int f = 0; f < faces.Count; f += 3)
                    Triangle(submesh, points[faces[f]], points[faces[f + 1]], points[faces[f + 2]]);
            }

            /// <summary>Smooth noise in 3D, about 0..1, from three crossed 2D Perlin planes.</summary>
            private static float Noise3(Vector3 p) =>
                (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z) +
                 Mathf.PerlinNoise(p.z + 57.3f, p.x + 11.1f)) / 3f;

            /// <summary>A lumpy, squashed sphere (a subdivided icosahedron with its corners pushed about).</summary>
            public void AddBlob(int submesh, Vector3 centre, Vector3 size, System.Random random, float lumpiness)
            {
                Icosphere(1, out var points, out var faces);
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
                int offset = _vertices.Count;
                _vertices.AddRange(_cardVertices);
                _normals.AddRange(_cardNormals);
                _uvs.AddRange(_cardUvs);
                for (int t = 0; t < _cardSubmesh.Count; t++)
                {
                    var list = _triangles[_cardSubmesh[t]];
                    list.Add(offset + _cardTriangles[t * 3]);
                    list.Add(offset + _cardTriangles[t * 3 + 1]);
                    list.Add(offset + _cardTriangles[t * 3 + 2]);
                }
                var mesh = new Mesh { name = name };
                if (_vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.subMeshCount = _triangles.Length;
                for (int i = 0; i < _triangles.Length; i++) mesh.SetTriangles(_triangles[i], i);
                mesh.RecalculateBounds();
                if (UvScale > 0f) mesh.RecalculateTangents();
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

            private static void Icosphere(int subdivisions, out List<Vector3> points, out List<int> faces)
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

                // Each subdivision splits every face in four: 80 faces after one, 320 after two.
                faces = baseFaces;
                for (int s = 0; s < subdivisions; s++)
                {
                    var finer = new List<int>(faces.Count * 4);
                    var midpoints = new Dictionary<long, int>();
                    for (int f = 0; f < faces.Count; f += 3)
                    {
                        int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                        int ab = Midpoint(points, midpoints, a, b);
                        int bc = Midpoint(points, midpoints, b, c);
                        int ca = Midpoint(points, midpoints, c, a);
                        finer.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                    }
                    faces = finer;
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
