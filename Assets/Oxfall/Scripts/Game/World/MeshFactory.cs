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

        /// <summary>Which edge of a frond's box in the atlas its stem starts from.</summary>
        private enum Stem { Left, Right, Bottom }

        /// <summary>
        /// A leafy frond or needle spray in a leaf atlas: its box in UV space and which edge the stem
        /// starts from. Boxes were measured from the alpha of the CC0 atlases from Poly Haven.
        /// </summary>
        private readonly struct Frond
        {
            private readonly float _u0, _u1, _v0, _v1;
            private readonly Stem _stem;

            public Frond(float u0, float v0, float u1, float v1, Stem stem)
            {
                _u0 = u0; _v0 = v0; _u1 = u1; _v1 = v1;
                _stem = stem;
            }

            /// <summary>Length over width.</summary>
            public float Aspect => _stem == Stem.Bottom ? (_v1 - _v0) / (_u1 - _u0) : (_u1 - _u0) / (_v1 - _v0);

            /// <summary>The UV at a point across (0..1) and along (0 = stem, 1 = tip) the frond.</summary>
            public Vector2 Uv(float across, float along)
            {
                switch (_stem)
                {
                    case Stem.Right: return new Vector2(_u1 - along * (_u1 - _u0), _v0 + across * (_v1 - _v0));
                    case Stem.Bottom: return new Vector2(_u0 + across * (_u1 - _u0), _v0 + along * (_v1 - _v0));
                    default: return new Vector2(_u0 + along * (_u1 - _u0), _v0 + across * (_v1 - _v0));
                }
            }
        }

        // Jacaranda leaves (broadleaf trees).
        private static readonly Frond[] AllFronds =
        {
            new Frond(0.119f, 0.500f, 0.988f, 0.973f, Stem.Left),
            new Frond(0.199f, 0.006f, 1.000f, 0.422f, Stem.Right),
            new Frond(0.000f, 0.281f, 0.459f, 0.762f, Stem.Left),
        };

        // Fir branches (pines): two branch cards side by side, built from the CC0 fir twig photos by
        // tools/art/make_fir_branch.py, stems at the bottom.
        private static readonly Frond[] FirBranches =
        {
            new Frond(0.0f, 0.0f, 0.5f, 1.0f, Stem.Bottom),
            new Frond(0.5f, 0.0f, 1.0f, 1.0f, Stem.Bottom),
        };

        /// <summary>
        /// A pine. With cards: a trunk, a dark core and whorls of drooping branches of photo fir sprays.
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

            // No solid core: in the game a cone inside the branches read as a black plastic cone.
            float crownBottom = 1.8f, crownTop = height - 0.6f;

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
                    var branch = FirBranches[random.Next(FirBranches.Length)];
                    float width = length / branch.Aspect * Rand(random, 1.0f, 1.2f);
                    float bend = Rand(random, 8f, 18f);
                    // A branch lying nearly flat and, on the long lower ones, a steeper one under it so
                    // the branch has body seen from the side.
                    var side = Quaternion.AngleAxis(Rand(random, -20f, 20f), dir) * flatSide;
                    Card(b, start, dir, bend, side, length, width, branch, Vector3.zero, true, 0.55f);
                    if (t >= 0.6f) continue;
                    side = Quaternion.AngleAxis(Rand(random, 65f, 110f), dir) * flatSide;
                    branch = FirBranches[random.Next(FirBranches.Length)];
                    Card(b, start, dir, bend, side, length * 0.8f, width * 0.7f, branch, Vector3.zero, true, 0.55f);
                }
            }

            // The leader: two crossed upright branches at the very top.
            var top = new Vector3(0f, crownTop - 0.3f, 0f);
            Card(b, top, Vector3.up, 0f, Vector3.right, 1.6f, 0.8f, FirBranches[0], Vector3.zero, true, 1f);
            Card(b, top, Vector3.up, 0f, Vector3.forward, 1.5f, 0.75f, FirBranches[1], Vector3.zero, true, 1f);
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
                    // Still at the stem, freer towards the tip and higher up the tree.
                    float wind = Mathf.Clamp01(p.y / 10f) * 0.5f + along * 0.5f;
                    int index = b.CardVertex(p, normal, frond.Uv(c, along), wind);
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
            // A trunk that forks into a few limbs, under a tall, rounded crown of leafy clumps.
            float fork = Rand(random, 2.6f, 3.4f);
            b.AddCylinder(BarkSubmesh, Vector3.zero, 0.3f, 0.2f, fork + 0.3f, 6, Rand(random, 0f, 60f));
            float radius = Rand(random, 2.3f, 2.9f);
            float crownHeight = Rand(random, 3.2f, 4.0f);
            var centre = new Vector3(0f, fork + crownHeight * 0.55f, 0f);

            int limbs = 3;
            float limbYaw = Rand(random, 0f, 360f);
            for (int i = 0; i < limbs; i++)
            {
                float a = (limbYaw + i * 120f + Rand(random, -20f, 20f)) * Mathf.Deg2Rad;
                var from = new Vector3(0f, fork, 0f);
                var to = centre + new Vector3(Mathf.Cos(a) * radius * 0.5f, Rand(random, -0.3f, 0.6f), Mathf.Sin(a) * radius * 0.5f);
                var lean = Quaternion.FromToRotation(Vector3.up, to - from);
                b.AddCylinder(BarkSubmesh, from, 0.15f, 0.06f, (to - from).magnitude, 5, 0f, lean);
            }

            // Clumps spread over the inside of the crown: a ring low down, a ring higher up, one on top.
            var clumps = new List<Vector3>();
            float ringYaw = Rand(random, 0f, 360f);
            for (int i = 0; i < 4; i++)
            {
                float a = (ringYaw + i * 90f + Rand(random, -20f, 20f)) * Mathf.Deg2Rad;
                clumps.Add(centre + new Vector3(Mathf.Cos(a) * radius * 0.6f, -crownHeight * Rand(random, 0.1f, 0.2f), Mathf.Sin(a) * radius * 0.6f));
            }
            for (int i = 0; i < 3; i++)
            {
                float a = (ringYaw + 45f + i * 120f + Rand(random, -20f, 20f)) * Mathf.Deg2Rad;
                clumps.Add(centre + new Vector3(Mathf.Cos(a) * radius * 0.4f, crownHeight * Rand(random, 0.12f, 0.22f), Mathf.Sin(a) * radius * 0.4f));
            }
            clumps.Add(centre + new Vector3(0f, crownHeight * 0.32f, 0f));

            foreach (var c in clumps)
            {
                // No solid clumps inside: in the game they showed as dark balls among the leaves.
                float size = Rand(random, 1.1f, 1.5f);
                for (int j = 0; j < 11; j++)
                {
                    // Sprays point away from the middle of the crown and droop a little.
                    var outward = (c - centre) + RandomDirection(random) * 1.2f;
                    outward.y = outward.y * 0.5f - Rand(random, 0.1f, 0.4f);
                    if (outward.sqrMagnitude < 0.01f) outward = RandomDirection(random);
                    var dir = outward.normalized;
                    var p = c + RandomDirection(random) * (size * 0.35f);
                    var flatSide = Vector3.Cross(Vector3.up, dir);
                    if (flatSide.sqrMagnitude < 1e-4f) flatSide = Vector3.right;
                    var side = Quaternion.AngleAxis(Rand(random, -45f, 45f), dir) * flatSide.normalized;
                    var frond = AllFronds[random.Next(AllFronds.Length)];
                    float length = Rand(random, 1.3f, 1.8f) * (frond.Aspect < 1.2f ? 0.7f : 1f);
                    float width = length / frond.Aspect;
                    Card(b, p - dir * (length * 0.3f), dir, Rand(random, 5f, 20f), side, length, width, frond,
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

        /// <summary>
        /// A berry bush, knee to waist high: leafy sprays (photo cards, or a lumpy mass without
        /// them) dotted with dark berries. Submeshes: berries, leaves, leaf cards.
        /// </summary>
        public static Mesh BerryBush(System.Random random, bool cards)
        {
            var b = new FlatMeshBuilder(3);
            var centre = new Vector3(0f, 0.45f, 0f);
            if (cards)
            {
                for (int j = 0; j < 14; j++)
                {
                    var dir = RandomDirection(random);
                    dir.y = Mathf.Abs(dir.y) * 0.6f + 0.15f;
                    dir.Normalize();
                    var flatSide = Vector3.Cross(Vector3.up, dir);
                    if (flatSide.sqrMagnitude < 1e-4f) flatSide = Vector3.right;
                    var side = Quaternion.AngleAxis(Rand(random, -40f, 40f), dir) * flatSide.normalized;
                    var frond = AllFronds[random.Next(AllFronds.Length)];
                    float length = Rand(random, 0.55f, 0.8f);
                    Card(b, centre + RandomDirection(random) * 0.12f - dir * 0.15f, dir, Rand(random, 10f, 30f), side,
                        length, length / frond.Aspect, frond, centre - Vector3.up * 0.3f, false, 0.4f);
                }
            }
            else
            {
                b.AddBlob(LeavesSubmesh, centre, new Vector3(0.6f, 0.45f, 0.6f), random, 0.25f);
            }
            for (int i = 0; i < 12; i++)
            {
                var d = RandomDirection(random);
                d.y = Mathf.Abs(d.y) * 0.8f;
                AddBerry(b, centre + Vector3.Scale(d.normalized, new Vector3(0.5f, 0.4f, 0.5f)), random);
            }
            return b.Build("BerryBush", 70f);
        }

        /// <summary>A handful of berries on a leaf, for the inventory icon. Submeshes: berries, leaf.</summary>
        public static Mesh BerryCluster()
        {
            var b = new FlatMeshBuilder(2);
            var random = new System.Random(23);
            b.AddBlob(1, new Vector3(0f, -0.01f, 0f), new Vector3(0.11f, 0.012f, 0.07f), random, 0.1f);
            var spots = new[]
            {
                new Vector3(-0.03f, 0.02f, 0f), new Vector3(0.025f, 0.02f, 0.01f), new Vector3(0f, 0.02f, -0.03f),
                new Vector3(0.005f, 0.05f, 0f), new Vector3(0.05f, 0.02f, -0.025f),
            };
            foreach (var p in spots) AddBerry(b, p, random, 0.022f);
            return b.Build("Berries", 70f);
        }

        /// <summary>One berry: a small rounded lump (a capped six-sided tube smoothed round).</summary>
        private static void AddBerry(FlatMeshBuilder b, Vector3 at, System.Random random, float radius = 0.03f)
        {
            float r = radius * Rand(random, 0.85f, 1.15f);
            var axis = RandomDirection(random) * r * 0.5f;
            b.AddTube(0, at - axis, at + axis, r, r * 0.8f, 6, 0);
        }

        // ---- Items: what the player holds and what the inventory icons are rendered from. Tools are
        // built at their real size with the grip at the origin, the haft up +Y and the head or blade
        // towards +X. Submesh order is the material order in ItemModels.

        /// <summary>A stone hatchet: a knapped stone lashed to a wooden haft. Submeshes: wood, stone, lashing.</summary>
        public static Mesh Hatchet()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.25f };
            b.AddTube(0, new Vector3(0f, -0.04f, 0f), new Vector3(0f, 0.5f, 0f), 0.017f, 0.015f, 7, 0);
            b.AddRock(1, new Vector3(0.05f, 0.44f, 0f), new Vector3(0.08f, 0.045f, 0.022f), new System.Random(11));
            b.AddTube(2, new Vector3(0f, 0.395f, 0f), new Vector3(0f, 0.475f, 0f), 0.024f, 0.024f, 7, 2);
            return b.Build("Hatchet", 55f);
        }

        /// <summary>A stone pickaxe: a long stone point each side of the haft. Submeshes: wood, stone, lashing.</summary>
        public static Mesh Pickaxe()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.25f };
            b.AddTube(0, new Vector3(0f, -0.04f, 0f), new Vector3(0f, 0.53f, 0f), 0.017f, 0.015f, 7, 0);
            var centre = new Vector3(0f, 0.47f, 0f);
            b.AddTube(1, centre, new Vector3(0.17f, 0.425f, 0f), 0.032f, 0.006f, 6, 1);
            b.AddTube(1, centre, new Vector3(-0.15f, 0.435f, 0f), 0.032f, 0.008f, 6, 1);
            b.AddRock(1, centre, new Vector3(0.042f, 0.038f, 0.034f), new System.Random(5));
            b.AddTube(2, new Vector3(0f, 0.43f, 0f), new Vector3(0f, 0.51f, 0f), 0.024f, 0.024f, 7, 2);
            return b.Build("Pickaxe", 55f);
        }

        /// <summary>A torch: a stick with a wrap of charred cloth at the top. Submeshes: wood, cloth.</summary>
        public static Mesh Torch()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.25f };
            b.AddTube(0, new Vector3(0f, -0.04f, 0f), new Vector3(0f, 0.42f, 0f), 0.016f, 0.019f, 7, 0);
            b.AddTube(1, new Vector3(0f, 0.31f, 0f), new Vector3(0f, 0.44f, 0f), 0.031f, 0.036f, 8, 1);
            b.AddTube(1, new Vector3(0f, 0.36f, 0f), new Vector3(0f, 0.40f, 0f), 0.040f, 0.040f, 8, 1);
            return b.Build("Torch", 55f);
        }

        /// <summary>
        /// A hammer for building: a short haft and a squat stone head across it, lashed on.
        /// Submeshes: wood, stone, lashing (like the hatchet, so it shares its materials).
        /// </summary>
        public static Mesh Hammer()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.25f };
            b.AddTube(0, new Vector3(0f, -0.04f, 0f), new Vector3(0f, 0.44f, 0f), 0.017f, 0.016f, 7, 0);
            b.AddTube(1, new Vector3(-0.085f, 0.43f, 0f), new Vector3(0.085f, 0.43f, 0f), 0.038f, 0.036f, 8, 1);
            b.AddTube(1, new Vector3(0.085f, 0.43f, 0f), new Vector3(0.1f, 0.43f, 0f), 0.036f, 0.03f, 8, 1);
            b.AddTube(2, new Vector3(0f, 0.385f, 0f), new Vector3(0f, 0.405f, 0f), 0.024f, 0.024f, 7, 2);
            b.AddTube(2, new Vector3(-0.03f, 0.43f, 0f), new Vector3(0.03f, 0.43f, 0f), 0.041f, 0.041f, 8, 2);
            return b.Build("Hammer", 55f);
        }

        /// <summary>
        /// A twig wall or doorway frame: posts and rails with thin sticks woven between them, see-through
        /// in places, so a twig base reads as unfinished. Same footprint as the solid wall (3 x 3 m,
        /// standing on the origin, along X).
        /// </summary>
        public static Mesh TwigWall(bool doorway)
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.6f };
            var random = new System.Random(doorway ? 31 : 17);
            const float post = 0.07f, stick = 0.035f;
            Stick(b, -1.42f, 0f, -1.42f, 3f, 0f, post);
            Stick(b, 1.42f, 0f, 1.42f, 3f, 0f, post);
            Stick(b, -1.5f, 0.1f, 1.5f, 0.1f, 0f, post);
            Stick(b, -1.5f, 2.9f, 1.5f, 2.9f, 0f, post);
            if (!doorway)
            {
                for (int i = 0; i < 9; i++)
                {
                    float x = -1.2f + i * 0.3f + Rand(random, -0.04f, 0.04f);
                    Stick(b, x + Rand(random, -0.05f, 0.05f), 0.1f, x + Rand(random, -0.05f, 0.05f), 2.9f, i % 2 == 0 ? 0.035f : -0.035f, stick);
                }
                Stick(b, -1.42f, 1.0f, 1.42f, 1.05f, 0.06f, stick);
                Stick(b, -1.42f, 2.0f, 1.42f, 1.95f, -0.06f, stick);
                Stick(b, -1.38f, 0.15f, 1.38f, 2.85f, 0.075f, 0.045f);
                Stick(b, 1.38f, 0.15f, -1.38f, 2.85f, -0.075f, 0.045f);
            }
            else
            {
                Stick(b, -0.62f, 0f, -0.62f, 2.9f, 0f, post);
                Stick(b, 0.62f, 0f, 0.62f, 2.9f, 0f, post);
                Stick(b, -1.5f, 2.2f, 1.5f, 2.2f, 0f, post);
                foreach (float side in new[] { -1f, 1f })
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float x = side * (0.84f + i * 0.22f);
                        Stick(b, x, 0.1f, x + Rand(random, -0.04f, 0.04f), 2.9f, i % 2 == 0 ? 0.035f : -0.035f, stick);
                    }
                    Stick(b, side * 0.66f, 0.15f, side * 1.38f, 2.15f, 0.075f, 0.045f);
                }
                for (int i = 0; i < 4; i++)
                {
                    float x = -0.45f + i * 0.3f;
                    Stick(b, x, 2.2f, x + Rand(random, -0.03f, 0.03f), 2.9f, i % 2 == 0 ? 0.035f : -0.035f, stick);
                }
            }
            return b.Build(doorway ? "TwigDoorway" : "TwigWall", 50f);
        }

        /// <summary>A twig roof: a square frame with sticks laid across, 3.2 m square on the origin.</summary>
        public static Mesh TwigRoof()
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.6f };
            var random = new System.Random(23);
            const float h = 1.55f, beam = 0.07f;
            b.AddTube(0, new Vector3(-h, 0.05f, -h), new Vector3(h, 0.05f, -h), beam, beam, 6);
            b.AddTube(0, new Vector3(-h, 0.05f, h), new Vector3(h, 0.05f, h), beam, beam, 6);
            b.AddTube(0, new Vector3(-h, 0.05f, -h), new Vector3(-h, 0.05f, h), beam, beam, 6);
            b.AddTube(0, new Vector3(h, 0.05f, -h), new Vector3(h, 0.05f, h), beam, beam, 6);
            b.AddTube(0, new Vector3(0f, 0.08f, -h), new Vector3(0f, 0.08f, h), beam, beam, 6);
            for (int i = 0; i < 11; i++)
            {
                float z = -1.4f + i * 0.28f + Rand(random, -0.03f, 0.03f);
                float y = 0.14f + Rand(random, -0.01f, 0.02f);
                b.AddTube(0, new Vector3(-h, y, z + Rand(random, -0.05f, 0.05f)), new Vector3(h, y, z + Rand(random, -0.05f, 0.05f)), 0.04f, 0.035f, 5);
            }
            return b.Build("TwigRoof", 50f);
        }

        /// <summary>
        /// A plank door with braces and a handle, hinged at the origin: 1.16 m wide along +X, 2.17 m
        /// tall, so it fills the doorway's opening.
        /// </summary>
        public static Mesh Door()
        {
            var b = new FlatMeshBuilder(1) { UvScale = 1.2f };
            b.AddBox(0, new Vector3(0.01f, 0f, -0.035f), new Vector3(1.15f, 2.16f, 0.035f));
            foreach (float z in new[] { 1f, -1f })
            {
                float lo = z > 0f ? 0.035f : -0.07f, hi = z > 0f ? 0.07f : -0.035f;
                b.AddBox(0, new Vector3(0.06f, 0.3f, lo), new Vector3(1.1f, 0.44f, hi));
                b.AddBox(0, new Vector3(0.06f, 1.72f, lo), new Vector3(1.1f, 1.86f, hi));
                b.AddTube(0, new Vector3(0.14f, 0.42f, z * 0.052f), new Vector3(1.02f, 1.74f, z * 0.052f), 0.045f, 0.045f, 4);
                b.AddBox(0, new Vector3(0.96f, 0.98f, z > 0f ? 0.07f : -0.13f), new Vector3(1.04f, 1.06f, z > 0f ? 0.13f : -0.07f));
            }
            return b.Build("Door");
        }

        private static void Stick(FlatMeshBuilder b, float x0, float y0, float x1, float y1, float z, float radius) =>
            b.AddTube(0, new Vector3(x0, y0, z), new Vector3(x1, y1, z), radius, radius * 0.85f, 5);

        /// <summary>The flame of a torch or a campfire: two crossed tapering shapes, about 0.25 m tall.</summary>
        public static Mesh Flame()
        {
            var b = new FlatMeshBuilder(1);
            b.AddTube(0, Vector3.zero, new Vector3(0f, 0.1f, 0f), 0.035f, 0.045f, 6, 0);
            b.AddTube(0, new Vector3(0f, 0.1f, 0f), new Vector3(0.01f, 0.26f, 0f), 0.045f, 0.0f, 6, 0);
            return b.Build("Flame", 70f);
        }

        /// <summary>A rolled-up blueprint tied with string, along X. Submeshes: paper, string.</summary>
        public static Mesh BuildingPlan()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.3f };
            b.AddTube(0, new Vector3(-0.15f, 0f, 0f), new Vector3(0.15f, 0f, 0f), 0.03f, 0.03f, 10, 0);
            b.AddTube(1, new Vector3(-0.012f, 0f, 0f), new Vector3(0.012f, 0f, 0f), 0.032f, 0.032f, 10, 1);
            return b.Build("BuildingPlan", 50f);
        }

        /// <summary>A campfire about a metre across: a ring of stones round leaning logs on ash. Submeshes: stone, wood, char.</summary>
        public static Mesh Campfire()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.5f };
            var random = new System.Random(19);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f + Rand(random, -0.12f, 0.12f);
                var at = new Vector3(Mathf.Cos(a) * 0.45f, 0.04f, Mathf.Sin(a) * 0.45f);
                b.AddRock(0, at, new Vector3(Rand(random, 0.09f, 0.12f), Rand(random, 0.06f, 0.09f), Rand(random, 0.09f, 0.12f)), random);
            }
            b.AddTube(2, new Vector3(0f, -0.01f, 0f), new Vector3(0f, 0.015f, 0f), 0.34f, 0.32f, 12, 2);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f + 0.3f;
                var foot = new Vector3(Mathf.Cos(a) * 0.32f, 0.03f, Mathf.Sin(a) * 0.32f);
                var tip = new Vector3(Mathf.Cos(a) * 0.04f, 0.36f, Mathf.Sin(a) * 0.04f);
                b.AddTube(1, foot, tip, 0.042f, 0.028f, 6, 2);
            }
            return b.Build("Campfire", 50f);
        }

        /// <summary>Three logs stacked, along Z. Submeshes: bark, end grain.</summary>
        public static Mesh WoodPile()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.35f };
            var half = new Vector3(0f, 0f, 0.24f);
            foreach (var c in new[] { new Vector3(-0.075f, 0.07f, 0f), new Vector3(0.075f, 0.07f, 0.02f), new Vector3(0f, 0.198f, -0.02f) })
                b.AddTube(0, c - half, c + half, 0.07f, 0.068f, 8, 1);
            return b.Build("WoodPile", 55f);
        }

        /// <summary>A few loose stones (stone or ore, by material).</summary>
        public static Mesh StonePile(int seed)
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.3f };
            var random = new System.Random(seed);
            b.AddRock(0, new Vector3(-0.08f, 0.06f, 0f), new Vector3(0.1f, 0.075f, 0.09f), random);
            b.AddRock(0, new Vector3(0.08f, 0.055f, 0.03f), new Vector3(0.08f, 0.065f, 0.08f), random);
            b.AddRock(0, new Vector3(0f, 0.13f, -0.02f), new Vector3(0.07f, 0.06f, 0.07f), random);
            return b.Build("StonePile", 50f);
        }

        /// <summary>A roll of rough cloth tied with string, along X. Submeshes: cloth, string.</summary>
        public static Mesh ClothRoll()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.3f };
            b.AddTube(0, new Vector3(-0.12f, 0f, 0f), new Vector3(0.12f, 0f, 0f), 0.065f, 0.065f, 10, 0);
            b.AddTube(1, new Vector3(-0.07f, 0f, 0f), new Vector3(-0.05f, 0f, 0f), 0.068f, 0.068f, 10, 1);
            b.AddTube(1, new Vector3(0.05f, 0f, 0f), new Vector3(0.07f, 0f, 0f), 0.068f, 0.068f, 10, 1);
            return b.Build("ClothRoll", 50f);
        }

        /// <summary>One flat-shaded mesh made of boxes. Building pieces are made of these.</summary>
        public static Mesh Boxes(string name, params Bounds[] boxes)
        {
            // About eight planks per 1.6 m tile of the planks texture.
            var b = new FlatMeshBuilder(1) { UvScale = 1.6f };
            foreach (var box in boxes) b.AddBox(0, box.min, box.max);
            return b.Build(name);
        }

        /// <summary>A flat square grid centred on the origin, facing up, split into cells.</summary>
        public static Mesh Grid(float size, int cells)
        {
            var vertices = new List<Vector3>((cells + 1) * (cells + 1));
            var normals = new List<Vector3>(vertices.Capacity);
            var triangles = new List<int>(cells * cells * 6);
            float h = size * 0.5f, step = size / cells;
            for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
            {
                vertices.Add(new Vector3(-h + x * step, 0f, -h + z * step));
                normals.Add(Vector3.up);
            }
            for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                int i = z * (cells + 1) + x;
                // Clockwise seen from above, so the faces point up.
                triangles.Add(i); triangles.Add(i + cells + 1); triangles.Add(i + cells + 2);
                triangles.Add(i); triangles.Add(i + cells + 2); triangles.Add(i + 1);
            }
            var mesh = new Mesh { name = "Grid" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>A flat disc centred on the origin, facing up.</summary>
        public static Mesh Disc(float radius, int segments)
        {
            var vertices = new List<Vector3> { Vector3.zero };
            var normals = new List<Vector3> { Vector3.up };
            var triangles = new List<int>(segments * 3);
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                vertices.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                normals.Add(Vector3.up);
            }
            for (int i = 0; i < segments; i++)
            {
                // Clockwise seen from above, so the faces point up.
                int next = i + 1 < segments ? i + 2 : 1;
                triangles.Add(0); triangles.Add(next); triangles.Add(i + 1);
            }
            var mesh = new Mesh { name = "Disc" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
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
            private readonly List<float> _cardWind = new List<float>();
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

            /// <summary>
            /// A vertex for a card, with its own normal and UV, and how freely the wind moves it (0 to
            /// 1, kept in the second UV channel). Returns its index among card vertices.
            /// </summary>
            public int CardVertex(Vector3 position, Vector3 normal, Vector2 uv, float wind)
            {
                _cardVertices.Add(position);
                _cardNormals.Add(normal);
                _cardUvs.Add(uv);
                _cardWind.Add(wind);
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
            /// A tube from <paramref name="a"/> to <paramref name="b"/>, its radius going from
            /// <paramref name="radiusA"/> to <paramref name="radiusB"/>. With a cap submesh, both ends are closed.
            /// </summary>
            public void AddTube(int submesh, Vector3 a, Vector3 b, float radiusA, float radiusB, int sides, int capSubmesh = -1)
            {
                var axis = b - a;
                float length = axis.magnitude;
                if (length < 1e-5f) return;
                var dir = axis / length;
                var rotation = Quaternion.FromToRotation(Vector3.up, dir);
                for (int i = 0; i < sides; i++)
                {
                    var r0 = rotation * Ring(i, sides, 1f, 0f);
                    var r1 = rotation * Ring(i + 1, sides, 1f, 0f);
                    var outward = (r0 + r1).normalized;
                    Facing(submesh, a + r0 * radiusA, b + r0 * radiusB, b + r1 * radiusB, outward);
                    Facing(submesh, a + r0 * radiusA, b + r1 * radiusB, a + r1 * radiusA, outward);
                    if (capSubmesh < 0) continue;
                    if (radiusB > 1e-4f) Facing(capSubmesh, b, b + r0 * radiusB, b + r1 * radiusB, dir);
                    if (radiusA > 1e-4f) Facing(capSubmesh, a, a + r0 * radiusA, a + r1 * radiusA, -dir);
                }
            }

            /// <summary>A triangle wound so that its face points towards <paramref name="towards"/>.</summary>
            public void Facing(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 towards)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), towards) >= 0f) Triangle(submesh, a, b, c);
                else Triangle(submesh, a, c, b);
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
                List<Vector2> wind = null;
                if (_cardVertices.Count > 0)
                {
                    wind = new List<Vector2>(offset + _cardVertices.Count);
                    for (int i = 0; i < offset; i++) wind.Add(Vector2.zero);
                    foreach (float w in _cardWind) wind.Add(new Vector2(w, 0f));
                }
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
                if (wind != null) mesh.SetUVs(1, wind);
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
