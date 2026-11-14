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

        /// <summary>
        /// A sulfur boulder: the same weathered stone, crusted on top and sides with lumps of yellow
        /// crystal. Submeshes: rock, crystal.
        /// </summary>
        public static Mesh SulfurRock(System.Random random)
        {
            var b = new FlatMeshBuilder(2) { UvScale = 1.1f };
            var centre = new Vector3(0f, 0.25f, 0f);
            b.AddRock(0, centre, new Vector3(1f, 0.7f, 1f), random);
            int lumps = 9 + random.Next(4);
            for (int i = 0; i < lumps; i++)
            {
                // Mostly on the upper half, where the light catches them.
                var d = RandomDirection(random);
                d.y = Mathf.Abs(d.y) * 0.9f + 0.1f;
                d.Normalize();
                var at = centre + new Vector3(d.x * 0.62f, d.y * 0.44f, d.z * 0.62f);
                float s = Rand(random, 0.09f, 0.2f);
                b.AddRock(1, at, new Vector3(s, s * Rand(random, 0.5f, 0.9f), s * Rand(random, 0.8f, 1.2f)), random);
            }
            return b.Build("SulfurRock", 50f);
        }

        /// <summary>A small heap of powder or grit, a few grains loose round it.</summary>
        public static Mesh PowderHeap()
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.2f };
            var random = new System.Random(41);
            b.AddBlob(0, new Vector3(0f, 0.035f, 0f), new Vector3(0.16f, 0.07f, 0.15f), random, 0.12f);
            b.AddBlob(0, new Vector3(0.03f, 0.09f, -0.01f), new Vector3(0.09f, 0.05f, 0.09f), random, 0.15f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.1f;
                b.AddRock(0, new Vector3(Mathf.Cos(a) * 0.19f, 0.012f, Mathf.Sin(a) * 0.17f), Vector3.one * Rand(random, 0.012f, 0.022f), random);
            }
            return b.Build("PowderHeap", 60f);
        }

        /// <summary>Where a satchel charge's fuse ends (on its mesh), for the sparks.</summary>
        public static readonly Vector3 SatchelFuseTip = new Vector3(0.105f, 0.19f, 0.07f);

        /// <summary>
        /// A satchel charge: a squat canvas bag with a flap, strapped shut, a rope handle on top and a
        /// short fuse out of a metal cap. Its back lies on the plane z = 0, its front towards +Z, up +Y
        /// (so it can be set against a wall facing out). Submeshes: canvas, strap and rope, metal, fuse.
        /// </summary>
        public static Mesh SatchelCharge()
        {
            var b = new FlatMeshBuilder(4) { UvScale = 0.3f };
            var random = new System.Random(77);
            // The bag, a little rounded, and its flap folded over the top front.
            b.AddBlob(0, new Vector3(0f, 0f, 0.05f), new Vector3(0.15f, 0.11f, 0.055f), random, 0.05f);
            b.AddBox(0, new Vector3(-0.13f, -0.09f, 0.005f), new Vector3(0.13f, 0.09f, 0.085f));
            b.AddBox(0, new Vector3(-0.14f, 0.015f, 0.082f), new Vector3(0.14f, 0.1f, 0.1f));
            // A strap round it, and its buckle.
            b.AddBox(1, new Vector3(-0.03f, -0.105f, 0.0f), new Vector3(0.03f, 0.105f, 0.106f));
            b.AddBox(2, new Vector3(-0.04f, -0.035f, 0.104f), new Vector3(0.04f, 0.015f, 0.114f));
            // A rope handle over the top.
            b.AddTube(1, new Vector3(-0.09f, 0.09f, 0.045f), new Vector3(-0.05f, 0.155f, 0.045f), 0.011f, 0.011f, 5);
            b.AddTube(1, new Vector3(-0.05f, 0.155f, 0.045f), new Vector3(0.03f, 0.155f, 0.045f), 0.011f, 0.011f, 5);
            b.AddTube(1, new Vector3(0.03f, 0.155f, 0.045f), new Vector3(0.06f, 0.09f, 0.045f), 0.011f, 0.011f, 5);
            // The detonator cap, and the fuse out of it.
            b.AddCylinder(2, new Vector3(0.1f, 0.085f, 0.06f), 0.018f, 0.016f, 0.045f, 8, 0f);
            b.AddTube(3, new Vector3(0.1f, 0.128f, 0.06f), new Vector3(0.108f, 0.165f, 0.068f), 0.006f, 0.006f, 5);
            b.AddTube(3, new Vector3(0.108f, 0.165f, 0.068f), SatchelFuseTip, 0.006f, 0.005f, 5);
            return b.Build("SatchelCharge", 45f);
        }

        /// <summary>Where the blunderbuss's muzzle is, and the revolver's (both point along +X).</summary>
        public static readonly Vector3 BlunderbussMuzzle = new Vector3(0.48f, 0.04f, 0f);
        public static readonly Vector3 RevolverMuzzle = new Vector3(0.205f, 0.048f, 0f);

        /// <summary>
        /// A pipe shotgun: a wooden stock, a steel pipe bound to it with rope that flares at the
        /// muzzle, a flint lock and a trigger. Points along +X, the grip at the origin. Submeshes:
        /// wood, metal, rope, the dark bore.
        /// </summary>
        public static Mesh Blunderbuss()
        {
            var b = new FlatMeshBuilder(4) { UvScale = 0.25f };
            const float w = 0.021f;
            // The stock: the butt, sloping down to the shoulder, then the wrist into the body.
            b.AddPrism(0, -w, w, new Vector2(-0.46f, -0.125f), new Vector2(-0.3f, -0.07f), new Vector2(-0.3f, 0.02f), new Vector2(-0.46f, -0.01f));
            b.AddPrism(0, -w, w, new Vector2(-0.3f, -0.07f), new Vector2(-0.08f, -0.05f), new Vector2(0.02f, -0.03f), new Vector2(0.02f, 0.03f), new Vector2(-0.3f, 0.02f));
            // The fore-end under the pipe.
            b.AddBox(0, new Vector3(0.02f, -0.026f, -0.019f), new Vector3(0.3f, 0.016f, 0.019f));
            // The pipe, flaring into a bell at the muzzle.
            b.AddTube(1, new Vector3(-0.06f, 0.04f, 0f), new Vector3(0.4f, 0.04f, 0f), 0.022f, 0.022f, 10, 3);
            b.AddTube(1, new Vector3(0.4f, 0.04f, 0f), BlunderbussMuzzle, 0.022f, 0.035f, 10, 3);
            // Rope bands holding pipe and stock together.
            b.AddBox(2, new Vector3(0.08f, -0.031f, -0.026f), new Vector3(0.11f, 0.067f, 0.026f));
            b.AddBox(2, new Vector3(0.24f, -0.031f, -0.026f), new Vector3(0.27f, 0.067f, 0.026f));
            // The lock plate, its hammer, the trigger and its guard.
            b.AddBox(1, new Vector3(-0.075f, 0.012f, -0.024f), new Vector3(0.0f, 0.048f, 0.024f));
            b.AddBox(1, new Vector3(-0.095f, 0.04f, -0.006f), new Vector3(-0.074f, 0.088f, 0.006f));
            b.AddBox(1, new Vector3(-0.125f, -0.078f, -0.005f), new Vector3(-0.045f, -0.068f, 0.005f));
            b.AddBox(1, new Vector3(-0.098f, -0.068f, -0.003f), new Vector3(-0.09f, -0.036f, 0.003f));
            return b.Build("Blunderbuss", 40f);
        }

        /// <summary>
        /// A six-shot revolver: a wooden grip, a steel frame, a fluted cylinder, a long barrel with a
        /// rib and a front sight, a hammer and a trigger guard. Points along +X. Submeshes: wood, metal,
        /// brass (the rounds in the cylinder), the dark bore.
        /// </summary>
        public static Mesh Revolver()
        {
            var b = new FlatMeshBuilder(4) { UvScale = 0.1f };
            b.AddPrism(0, -0.017f, 0.017f, new Vector2(-0.135f, -0.115f), new Vector2(-0.08f, -0.122f), new Vector2(-0.035f, -0.006f),
                new Vector2(-0.05f, 0.025f), new Vector2(-0.106f, 0.02f));
            b.AddPrism(1, -0.012f, 0.012f, new Vector2(-0.112f, 0.0f), new Vector2(0.03f, 0.0f), new Vector2(0.03f, 0.056f),
                new Vector2(-0.09f, 0.066f), new Vector2(-0.118f, 0.032f));
            // The cylinder, and the backs of the rounds in it.
            b.AddTube(1, new Vector3(-0.036f, 0.04f, 0f), new Vector3(0.026f, 0.04f, 0f), 0.026f, 0.026f, 12, 1);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f + Mathf.PI / 6f;
                var at = new Vector3(-0.0365f, 0.04f + Mathf.Cos(a) * 0.015f, Mathf.Sin(a) * 0.015f);
                b.AddTube(2, at, at + new Vector3(0.004f, 0f, 0f), 0.006f, 0.006f, 6, 2);
            }
            b.AddTube(1, new Vector3(0.026f, 0.048f, 0f), RevolverMuzzle, 0.011f, 0.011f, 10, 3);
            b.AddBox(1, new Vector3(0.026f, 0.05f, -0.005f), new Vector3(0.2f, 0.062f, 0.005f));
            b.AddBox(1, new Vector3(0.186f, 0.062f, -0.002f), new Vector3(0.196f, 0.074f, 0.002f));
            b.AddBox(1, new Vector3(-0.108f, 0.055f, -0.004f), new Vector3(-0.088f, 0.086f, 0.004f));
            b.AddBox(1, new Vector3(-0.062f, -0.036f, -0.004f), new Vector3(-0.004f, -0.029f, 0.004f));
            b.AddBox(1, new Vector3(-0.008f, -0.036f, -0.004f), new Vector3(-0.001f, 0.0f, 0.004f));
            b.AddBox(1, new Vector3(-0.042f, -0.03f, -0.003f), new Vector3(-0.035f, 0.0f, 0.003f));
            return b.Build("Revolver", 40f);
        }

        /// <summary>Three handmade shells standing together: red paper tubes on brass heads.</summary>
        public static Mesh Shells()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.05f };
            var at = new[] { new Vector3(-0.016f, 0f, 0f), new Vector3(0.016f, 0f, 0.004f), new Vector3(0f, 0f, -0.024f) };
            foreach (var p in at)
            {
                b.AddTube(1, p, p + new Vector3(0f, 0.016f, 0f), 0.0125f, 0.0125f, 12, 1);
                b.AddTube(0, p + new Vector3(0f, 0.016f, 0f), p + new Vector3(0f, 0.066f, 0f), 0.0118f, 0.0118f, 12, 0);
            }
            return b.Build("Shells", 40f);
        }

        /// <summary>A few pistol rounds: brass cases with grey lead tips.</summary>
        public static Mesh PistolRounds()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.05f };
            var at = new[] { new Vector3(-0.012f, 0f, 0f), new Vector3(0.012f, 0f, 0.003f), new Vector3(0f, 0f, -0.018f), new Vector3(0.026f, 0f, -0.016f) };
            foreach (var p in at)
            {
                b.AddTube(0, p, p + new Vector3(0f, 0.024f, 0f), 0.0058f, 0.0058f, 10, 0);
                b.AddTube(1, p + new Vector3(0f, 0.024f, 0f), p + new Vector3(0f, 0.036f, 0f), 0.0056f, 0.0012f, 10, 1);
            }
            return b.Build("PistolRounds", 40f);
        }

        /// <summary>
        /// A medical syringe along +X: a clear barrel showing the red dose, a white plunger with its
        /// thumb rest and finger flange, a steel needle. Submeshes: barrel, plastic, dose, needle.
        /// </summary>
        public static Mesh Syringe()
        {
            var b = new FlatMeshBuilder(4) { UvScale = 0.05f };
            b.AddTube(0, new Vector3(-0.055f, 0f, 0f), new Vector3(-0.02f, 0f, 0f), 0.0125f, 0.0125f, 10, 0);
            b.AddTube(2, new Vector3(-0.02f, 0f, 0f), new Vector3(0.045f, 0f, 0f), 0.0127f, 0.0127f, 10, 0);
            b.AddTube(1, new Vector3(0.045f, 0f, 0f), new Vector3(0.056f, 0f, 0f), 0.0127f, 0.004f, 10, 1);
            b.AddTube(3, new Vector3(0.056f, 0f, 0f), new Vector3(0.1f, 0f, 0f), 0.0016f, 0.0008f, 6, 3);
            // The plunger out the back, its thumb rest, and the flange to hold.
            b.AddTube(1, new Vector3(-0.1f, 0f, 0f), new Vector3(-0.055f, 0f, 0f), 0.0045f, 0.0045f, 6, 1);
            b.AddTube(1, new Vector3(-0.106f, 0f, 0f), new Vector3(-0.1f, 0f, 0f), 0.015f, 0.015f, 12, 1);
            b.AddBox(1, new Vector3(-0.06f, -0.005f, -0.026f), new Vector3(-0.054f, 0.005f, 0.026f));
            return b.Build("Syringe", 40f);
        }

        /// <summary>A broken lump about a metre across (scaled down for rubble), faceted like a fresh break.</summary>
        public static Mesh Rubble(int seed)
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.5f };
            b.AddRock(0, Vector3.zero, new Vector3(0.55f, 0.42f, 0.5f), new System.Random(seed));
            return b.Build("Rubble");
        }

        /// <summary>A quad in the XY plane, 1 m across, UVs 0 to 1: the card a camera-facing puff is drawn on.</summary>
        public static Mesh PuffCard()
        {
            var mesh = new Mesh { name = "PuffCard" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            });
            mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            // Billboarded in the shader: bounds big enough that it is never culled for facing away.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1.5f);
            return mesh;
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
            var centre = new Vector3(0f, 0.42f, 0f);
            // A leafy body, so the bush reads as one plant and the berries have something to hang on.
            // With leaf cards it is only the shaded heart of the bush, hidden by fronds all round.
            var body = cards ? new Vector3(0.3f, 0.24f, 0.3f) : new Vector3(0.42f, 0.33f, 0.42f);
            b.AddBlob(LeavesSubmesh, centre, body, random, 0.25f);
            if (cards)
            {
                for (int j = 0; j < 32; j++)
                {
                    // Even spread round the bush (golden angle), from level to nearly upright.
                    float yaw = j * 137.5f + Rand(random, -15f, 15f);
                    float rise = Rand(random, 0.05f, 0.85f);
                    var dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, rise, 1f);
                    dir.Normalize();
                    var flatSide = Vector3.Cross(Vector3.up, dir);
                    if (flatSide.sqrMagnitude < 1e-4f) flatSide = Vector3.right;
                    var side = Quaternion.AngleAxis(Rand(random, -40f, 40f), dir) * flatSide.normalized;
                    var frond = AllFronds[random.Next(AllFronds.Length)];
                    float length = Rand(random, 0.5f, 0.75f);
                    Card(b, centre + Vector3.Scale(dir, body) * 0.35f, dir, Rand(random, 10f, 30f), side,
                        length, length / frond.Aspect, frond, centre - Vector3.up * 0.3f, false, 0.4f);
                }
            }
            // Clusters of berries among the leaves, mostly on the upper half.
            var reach = cards ? new Vector3(0.4f, 0.32f, 0.4f) : body * 1.05f;
            for (int i = 0; i < 9; i++)
            {
                var d = RandomDirection(random);
                d.y = Mathf.Abs(d.y) * 0.9f + 0.05f;
                var spot = centre + Vector3.Scale(d.normalized, reach);
                for (int k = 0; k < 3; k++)
                    AddBerry(b, spot + RandomDirection(random) * 0.045f, random, 0.034f);
            }
            return b.Build("BerryBush", 70f);
        }

        // ---- Animals: a body (feet at the origin, facing +Z) and one leg that hangs from its hip, drawn four times.
        // Submeshes: coat, dark (nose, eyes, hooves), horn (tusks, antlers).

        /// <summary>The hips' places on the body, front left, front right, back left, back right.</summary>
        public static Vector3[] AnimalHips(Oxfall.Core.Wildlife.AnimalKind kind)
        {
            switch (kind)
            {
                case Oxfall.Core.Wildlife.AnimalKind.Boar:
                    return new[] { new Vector3(-0.13f, 0.34f, 0.3f), new Vector3(0.13f, 0.34f, 0.3f), new Vector3(-0.13f, 0.34f, -0.34f), new Vector3(0.13f, 0.34f, -0.34f) };
                case Oxfall.Core.Wildlife.AnimalKind.Wolf:
                    return new[] { new Vector3(-0.09f, 0.5f, 0.27f), new Vector3(0.09f, 0.5f, 0.27f), new Vector3(-0.09f, 0.5f, -0.32f), new Vector3(0.09f, 0.5f, -0.32f) };
                default:
                    return new[] { new Vector3(-0.1f, 0.75f, 0.33f), new Vector3(0.1f, 0.75f, 0.33f), new Vector3(-0.1f, 0.75f, -0.36f), new Vector3(0.1f, 0.75f, -0.36f) };
            }
        }

        public static Mesh AnimalBody(Oxfall.Core.Wildlife.AnimalKind kind)
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.5f };
            var random = new System.Random(71 + (int)kind);
            switch (kind)
            {
                case Oxfall.Core.Wildlife.AnimalKind.Boar:
                    // Heavy in front, a bristly ridge down the back, a long wedge of a head with tusks.
                    b.AddBlob(0, new Vector3(0f, 0.5f, -0.08f), new Vector3(0.25f, 0.23f, 0.44f), random, 0.12f);
                    b.AddBlob(0, new Vector3(0f, 0.55f, 0.2f), new Vector3(0.25f, 0.25f, 0.27f), random, 0.12f);
                    b.AddBlob(1, new Vector3(0f, 0.75f, 0.08f), new Vector3(0.05f, 0.07f, 0.38f), random, 0.35f);
                    b.AddBlob(0, new Vector3(0f, 0.5f, 0.55f), new Vector3(0.15f, 0.16f, 0.19f), random, 0.08f);
                    b.AddTube(0, new Vector3(0f, 0.47f, 0.68f), new Vector3(0f, 0.38f, 0.9f), 0.1f, 0.065f, 9, 1);
                    foreach (float x in new[] { -1f, 1f })
                    {
                        b.AddCone(0, new Vector3(x * 0.08f, 0.62f, 0.52f), 0.045f, 0.1f, 6, 0f);
                        b.AddBlob(1, new Vector3(x * 0.1f, 0.55f, 0.66f), new Vector3(0.018f, 0.018f, 0.018f), random, 0f);
                        b.AddTube(2, new Vector3(x * 0.06f, 0.39f, 0.84f), new Vector3(x * 0.1f, 0.47f, 0.82f), 0.016f, 0.006f, 5);
                    }
                    b.AddTube(0, new Vector3(0f, 0.6f, -0.5f), new Vector3(0f, 0.47f, -0.6f), 0.025f, 0.012f, 5);
                    break;
                case Oxfall.Core.Wildlife.AnimalKind.Wolf:
                    // Lean: a deep chest, a narrow waist, a long muzzle, ears up and a bushy tail low.
                    b.AddBlob(0, new Vector3(0f, 0.62f, -0.08f), new Vector3(0.14f, 0.15f, 0.38f), random, 0.1f);
                    b.AddBlob(0, new Vector3(0f, 0.64f, 0.22f), new Vector3(0.145f, 0.2f, 0.21f), random, 0.12f);
                    b.AddTube(0, new Vector3(0f, 0.68f, 0.33f), new Vector3(0f, 0.81f, 0.53f), 0.12f, 0.09f, 9);
                    // A broad skull with cheek ruffs, a short tapering muzzle and a dark nose.
                    b.AddBlob(0, new Vector3(0f, 0.83f, 0.6f), new Vector3(0.125f, 0.11f, 0.12f), random, 0.06f);
                    b.AddTube(0, new Vector3(0f, 0.8f, 0.68f), new Vector3(0f, 0.77f, 0.82f), 0.065f, 0.038f, 8, 1);
                    b.AddBlob(1, new Vector3(0f, 0.775f, 0.825f), new Vector3(0.024f, 0.02f, 0.018f), random, 0f);
                    foreach (float x in new[] { -1f, 1f })
                    {
                        b.AddBlob(0, new Vector3(x * 0.1f, 0.78f, 0.56f), new Vector3(0.05f, 0.07f, 0.07f), random, 0.15f);
                        b.AddCone(0, new Vector3(x * 0.07f, 0.9f, 0.56f), 0.042f, 0.11f, 5, 0f);
                        b.AddBlob(1, new Vector3(x * 0.062f, 0.86f, 0.685f), new Vector3(0.014f, 0.012f, 0.014f), random, 0f);
                    }
                    b.AddTube(0, new Vector3(0f, 0.66f, -0.42f), new Vector3(0f, 0.5f, -0.7f), 0.065f, 0.045f, 7);
                    b.AddTube(1, new Vector3(0f, 0.5f, -0.7f), new Vector3(0f, 0.38f, -0.8f), 0.045f, 0.012f, 7);
                    break;
                default:
                    // Long-legged and slender: a long neck, big ears, antlers, a short tail.
                    b.AddBlob(0, new Vector3(0f, 0.93f, 0f), new Vector3(0.18f, 0.21f, 0.46f), random, 0.08f);
                    b.AddTube(0, new Vector3(0f, 1.0f, 0.34f), new Vector3(0f, 1.33f, 0.55f), 0.11f, 0.065f, 9);
                    b.AddBlob(0, new Vector3(0f, 1.37f, 0.61f), new Vector3(0.075f, 0.085f, 0.12f), random, 0.05f);
                    b.AddTube(0, new Vector3(0f, 1.34f, 0.69f), new Vector3(0f, 1.28f, 0.83f), 0.05f, 0.032f, 8, 1);
                    b.AddBlob(0, new Vector3(0f, 1.02f, -0.48f), new Vector3(0.04f, 0.06f, 0.03f), random, 0.1f);
                    foreach (float x in new[] { -1f, 1f })
                    {
                        b.AddTube(0, new Vector3(x * 0.06f, 1.42f, 0.58f), new Vector3(x * 0.15f, 1.47f, 0.55f), 0.03f, 0.012f, 5);
                        b.AddBlob(1, new Vector3(x * 0.055f, 1.4f, 0.69f), new Vector3(0.014f, 0.014f, 0.014f), random, 0f);
                        b.AddTube(2, new Vector3(x * 0.035f, 1.44f, 0.6f), new Vector3(x * 0.13f, 1.66f, 0.52f), 0.02f, 0.013f, 5);
                        b.AddTube(2, new Vector3(x * 0.13f, 1.66f, 0.52f), new Vector3(x * 0.2f, 1.8f, 0.47f), 0.013f, 0.005f, 5);
                        b.AddTube(2, new Vector3(x * 0.1f, 1.58f, 0.55f), new Vector3(x * 0.07f, 1.73f, 0.63f), 0.011f, 0.004f, 4);
                        b.AddTube(2, new Vector3(x * 0.05f, 1.49f, 0.6f), new Vector3(x * 0.05f, 1.54f, 0.71f), 0.01f, 0.004f, 4);
                    }
                    break;
            }
            return b.Build("Animal " + kind, 55f);
        }

        /// <summary>One leg hanging from its hip at the origin, the hoof or paw at the ground.</summary>
        public static Mesh AnimalLeg(Oxfall.Core.Wildlife.AnimalKind kind)
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.5f };
            switch (kind)
            {
                case Oxfall.Core.Wildlife.AnimalKind.Boar:
                    b.AddTube(0, new Vector3(0f, 0.04f, 0f), new Vector3(0f, -0.26f, 0.01f), 0.065f, 0.042f, 6);
                    b.AddTube(1, new Vector3(0f, -0.26f, 0.01f), new Vector3(0f, -0.34f, 0.025f), 0.042f, 0.038f, 6, 1);
                    break;
                case Oxfall.Core.Wildlife.AnimalKind.Wolf:
                    b.AddTube(0, new Vector3(0f, 0.04f, 0f), new Vector3(0f, -0.25f, -0.025f), 0.055f, 0.034f, 6);
                    b.AddTube(0, new Vector3(0f, -0.25f, -0.025f), new Vector3(0f, -0.46f, 0f), 0.034f, 0.027f, 6);
                    b.AddBlob(1, new Vector3(0f, -0.48f, 0.02f), new Vector3(0.033f, 0.02f, 0.045f), new System.Random(5), 0.05f);
                    break;
                default:
                    b.AddTube(0, new Vector3(0f, 0.05f, 0f), new Vector3(0f, -0.34f, -0.035f), 0.06f, 0.033f, 6);
                    b.AddTube(0, new Vector3(0f, -0.34f, -0.035f), new Vector3(0f, -0.66f, 0f), 0.03f, 0.024f, 6);
                    b.AddTube(1, new Vector3(0f, -0.66f, 0f), new Vector3(0f, -0.75f, 0.02f), 0.028f, 0.03f, 6, 1);
                    break;
            }
            return b.Build("Animal leg " + kind, 55f);
        }

        /// <summary>A thick cut of meat with a bone through it, lying flat. Submeshes: meat, bone.</summary>
        /// <summary>
        /// An old oil drum, 0.88 m tall: a steel shell, rims and two rolling hoops, a sunken lid with
        /// its bung. Submeshes: paint, bare steel (rims and hoops), lid.
        /// </summary>
        public static Mesh Barrel()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 1f };
            const int sides = 16;
            const float r = 0.29f, h = 0.88f;
            b.AddCylinder(0, new Vector3(0f, 0.03f, 0f), r, r, h - 0.06f, sides, 0f);
            b.AddTube(1, new Vector3(0f, 0f, 0f), new Vector3(0f, 0.04f, 0f), r + 0.012f, r + 0.012f, sides);
            b.AddTube(1, new Vector3(0f, h - 0.04f, 0f), new Vector3(0f, h, 0f), r + 0.012f, r + 0.012f, sides);
            foreach (float y in new[] { h * 0.34f, h * 0.66f })
                b.AddTube(1, new Vector3(0f, y - 0.014f, 0f), new Vector3(0f, y + 0.014f, 0f), r + 0.016f, r + 0.016f, sides);
            // The lid a little below the rim, the rim's inside showing round it; a bung near the edge.
            b.AddTube(1, new Vector3(0f, h - 0.03f, 0f), new Vector3(0f, h, 0f), r - 0.004f, r - 0.004f, sides);
            b.AddCone(2, new Vector3(0f, h - 0.03f, 0f), r, 0.006f, sides, 0f);
            b.AddTube(1, new Vector3(0.16f, h - 0.03f, 0.06f), new Vector3(0.16f, h - 0.005f, 0.06f), 0.03f, 0.03f, 8, 1);
            return b.Build("Barrel", 50f);
        }

        public static Mesh MeatCut()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.3f };
            var random = new System.Random(83);
            b.AddBlob(0, new Vector3(0f, 0f, 0f), new Vector3(0.12f, 0.045f, 0.09f), random, 0.12f);
            b.AddBlob(0, new Vector3(0.06f, 0.005f, 0.03f), new Vector3(0.07f, 0.04f, 0.06f), random, 0.12f);
            b.AddTube(1, new Vector3(-0.17f, 0f, -0.02f), new Vector3(0.04f, 0f, 0.0f), 0.022f, 0.02f, 7, 1);
            b.AddBlob(1, new Vector3(-0.18f, 0f, -0.02f), new Vector3(0.03f, 0.03f, 0.035f), random, 0.1f);
            return b.Build("MeatCut", 60f);
        }

        /// <summary>A folded hide, flat, its edges uneven. Submeshes: leather, cord.</summary>
        public static Mesh FoldedHide()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.3f };
            var random = new System.Random(89);
            b.AddBlob(0, new Vector3(0f, 0f, 0f), new Vector3(0.16f, 0.025f, 0.12f), random, 0.25f);
            b.AddBlob(0, new Vector3(0.02f, 0.04f, -0.01f), new Vector3(0.14f, 0.022f, 0.1f), random, 0.25f);
            b.AddBlob(0, new Vector3(-0.01f, 0.075f, 0.01f), new Vector3(0.12f, 0.02f, 0.085f), random, 0.25f);
            b.AddTube(1, new Vector3(0f, -0.03f, -0.13f), new Vector3(0f, 0.1f, -0.06f), 0.012f, 0.012f, 4);
            b.AddTube(1, new Vector3(0f, 0.1f, -0.06f), new Vector3(0f, 0.1f, 0.08f), 0.012f, 0.012f, 4);
            b.AddTube(1, new Vector3(0f, 0.1f, 0.08f), new Vector3(0f, -0.03f, 0.13f), 0.012f, 0.012f, 4);
            return b.Build("FoldedHide", 60f);
        }

        // ---- Other players: a plain survivor in parts that swing (legs and arms hang from their tops).

        /// <summary>Torso, neck and head, feet at the origin. Submeshes: shirt, skin.</summary>
        public static Mesh SurvivorBody()
        {
            var b = new FlatMeshBuilder(2);
            var random = new System.Random(3);
            b.AddTube(0, new Vector3(0f, 0.9f, 0f), new Vector3(0f, 1.2f, 0f), 0.17f, 0.19f, 9, 0);
            b.AddTube(0, new Vector3(0f, 1.2f, 0f), new Vector3(0f, 1.44f, 0f), 0.19f, 0.15f, 9, 0);
            b.AddTube(1, new Vector3(0f, 1.42f, 0f), new Vector3(0f, 1.53f, 0f), 0.055f, 0.05f, 7, 1);
            b.AddBlob(1, new Vector3(0f, 1.64f, 0.01f), new Vector3(0.105f, 0.125f, 0.115f), random, 0.04f);
            return b.Build("Survivor body", 60f);
        }

        // ---- Clothing: laid flat for the inventory icons, and what is worn on the head.

        /// <summary>Corners in counter-clockwise order seen from +Z, whichever way they were given.</summary>
        private static Vector2[] Ccw(params Vector2[] points)
        {
            float area = 0f;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i, i++)
                area += (points[j].x - points[i].x) * (points[j].y + points[i].y);
            if (area < 0f) System.Array.Reverse(points);
            return points;
        }

        private static Vector2[] Mirror(Vector2[] points)
        {
            var mirrored = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) mirrored[i] = new Vector2(-points[i].x, points[i].y);
            return Ccw(mirrored);
        }

        /// <summary>
        /// A shirt laid flat, collar up (with <paramref name="sleeves"/>), or a vest open down the front
        /// and laced. Submeshes: cloth or hide, stitching and lacing.
        /// </summary>
        public static Mesh Garment(bool sleeves, bool vest)
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.35f };
            const float z0 = -0.018f, z1 = 0.018f;
            if (vest)
            {
                var half = Ccw(new Vector2(0.025f, -0.31f), new Vector2(0.21f, -0.31f), new Vector2(0.22f, 0.2f),
                    new Vector2(0.16f, 0.29f), new Vector2(0.08f, 0.29f), new Vector2(0.025f, 0.04f));
                b.AddPrism(0, z0, z1, half);
                b.AddPrism(0, z0, z1, Mirror(half));
                for (int k = 0; k < 4; k++)
                {
                    float y = -0.2f + k * 0.075f;
                    b.AddTube(1, new Vector3(-0.045f, y, z0 - 0.004f), new Vector3(0.045f, y + 0.03f, z0 - 0.004f), 0.006f, 0.006f, 5);
                }
            }
            else
            {
                b.AddPrism(0, z0, z1, Ccw(new Vector2(-0.2f, -0.31f), new Vector2(0.2f, -0.31f), new Vector2(0.21f, 0.23f),
                    new Vector2(0.07f, 0.28f), new Vector2(0f, 0.24f), new Vector2(-0.07f, 0.28f), new Vector2(-0.21f, 0.23f)));
                if (sleeves)
                {
                    var sleeve = Ccw(new Vector2(0.19f, 0.03f), new Vector2(0.37f, 0.09f), new Vector2(0.31f, 0.27f), new Vector2(0.17f, 0.25f));
                    b.AddPrism(0, z0 * 0.9f, z1 * 0.9f, sleeve);
                    b.AddPrism(0, z0 * 0.9f, z1 * 0.9f, Mirror(sleeve));
                }
                // The neckline and hem stitched in a darker thread.
                b.AddTube(1, new Vector3(-0.07f, 0.275f, z0 - 0.003f), new Vector3(0f, 0.235f, z0 - 0.003f), 0.007f, 0.007f, 5);
                b.AddTube(1, new Vector3(0f, 0.235f, z0 - 0.003f), new Vector3(0.07f, 0.275f, z0 - 0.003f), 0.007f, 0.007f, 5);
                b.AddTube(1, new Vector3(-0.19f, -0.29f, z0 - 0.003f), new Vector3(0.19f, -0.29f, z0 - 0.003f), 0.006f, 0.006f, 5);
            }
            return b.Build(vest ? "Vest" : sleeves ? "Shirt" : "Tunic", 20f);
        }

        /// <summary>Trousers laid flat, a cord through the waist. Submeshes: cloth or hide, cord.</summary>
        public static Mesh Trousers()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.35f };
            const float z0 = -0.018f, z1 = 0.018f;
            b.AddPrism(0, z0, z1, Ccw(new Vector2(-0.18f, 0.24f), new Vector2(0.18f, 0.24f), new Vector2(0.18f, 0.31f), new Vector2(-0.18f, 0.31f)));
            var leg = Ccw(new Vector2(0.005f, 0.25f), new Vector2(0.18f, 0.25f), new Vector2(0.19f, -0.36f), new Vector2(0.03f, -0.36f));
            b.AddPrism(0, z0, z1, leg);
            b.AddPrism(0, z0, z1, Mirror(leg));
            b.AddTube(1, new Vector3(-0.17f, 0.275f, z0 - 0.004f), new Vector3(0.17f, 0.275f, z0 - 0.004f), 0.008f, 0.008f, 5);
            b.AddTube(1, new Vector3(0.02f, 0.275f, z0 - 0.004f), new Vector3(0.05f, 0.17f, z0 - 0.004f), 0.006f, 0.005f, 5);
            return b.Build("Trousers", 20f);
        }

        /// <summary>A pair of hide boots side by side. Submeshes: hide, sole.</summary>
        public static Mesh Boots()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.3f };
            foreach (float x in new[] { -0.09f, 0.1f })
            {
                float y = x < 0f ? 0.02f : 0f;
                b.AddBox(0, new Vector3(x - 0.055f, y - 0.02f, -0.045f), new Vector3(x + 0.045f, y + 0.2f, 0.045f));
                b.AddBox(0, new Vector3(x - 0.055f, y - 0.09f, -0.045f), new Vector3(x + 0.13f, y - 0.01f, 0.045f));
                b.AddBox(1, new Vector3(x - 0.06f, y - 0.11f, -0.05f), new Vector3(x + 0.14f, y - 0.085f, 0.05f));
                b.AddTube(1, new Vector3(x - 0.06f, y + 0.17f, -0.05f), new Vector3(x + 0.05f, y + 0.17f, -0.05f), 0.007f, 0.007f, 5);
            }
            return b.Build("Boots", 30f);
        }

        /// <summary>
        /// Worn on a survivor's head (its centre about 1.64 m up): cloth wound round it, or a hide hood
        /// over the head and neck, open at the face. Submeshes: cloth or hide, stitching.
        /// </summary>
        public static Mesh Headwear(bool hood)
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.3f };
            var random = new System.Random(hood ? 41 : 43);
            if (hood)
            {
                b.AddBlob(0, new Vector3(0f, 1.67f, -0.025f), new Vector3(0.13f, 0.15f, 0.125f), random, 0.05f);
                b.AddTube(0, new Vector3(0f, 1.45f, -0.01f), new Vector3(0f, 1.58f, -0.015f), 0.13f, 0.11f, 9);
                b.AddTube(1, new Vector3(-0.1f, 1.76f, 0.06f), new Vector3(0.1f, 1.76f, 0.06f), 0.012f, 0.012f, 5);
            }
            else
            {
                for (int k = 0; k < 3; k++)
                {
                    float y = 1.62f + k * 0.05f;
                    b.AddTube(0, new Vector3(0f, y, 0.005f), new Vector3(0f, y + 0.055f, 0.0f), 0.122f - k * 0.008f, 0.118f - k * 0.012f, 10);
                }
                b.AddBlob(0, new Vector3(0f, 1.765f, -0.005f), new Vector3(0.095f, 0.035f, 0.1f), random, 0.08f);
                b.AddBlob(1, new Vector3(0.08f, 1.68f, -0.09f), new Vector3(0.035f, 0.03f, 0.03f), random, 0.1f);
            }
            return b.Build(hood ? "Hood" : "Headwrap", 40f);
        }

        /// <summary>One leg hanging from the hip at the origin. Submeshes: trousers, boot.</summary>
        public static Mesh SurvivorLeg()
        {
            var b = new FlatMeshBuilder(2);
            b.AddTube(0, Vector3.zero, new Vector3(0f, -0.45f, 0.01f), 0.085f, 0.07f, 7, 0);
            b.AddTube(0, new Vector3(0f, -0.45f, 0.01f), new Vector3(0f, -0.82f, 0f), 0.07f, 0.06f, 7, 0);
            b.AddTube(1, new Vector3(0f, -0.82f, -0.02f), new Vector3(0f, -0.9f, 0.08f), 0.065f, 0.06f, 7, 1);
            return b.Build("Survivor leg", 60f);
        }

        /// <summary>One arm hanging from the shoulder at the origin. Submeshes: sleeve, hand.</summary>
        public static Mesh SurvivorArm()
        {
            var b = new FlatMeshBuilder(2);
            b.AddTube(0, Vector3.zero, new Vector3(0f, -0.3f, 0f), 0.06f, 0.05f, 7, 0);
            b.AddTube(1, new Vector3(0f, -0.3f, 0f), new Vector3(0f, -0.56f, 0.02f), 0.045f, 0.038f, 7, 1);
            b.AddBlob(1, new Vector3(0f, -0.61f, 0.02f), new Vector3(0.04f, 0.055f, 0.03f), new System.Random(9), 0.05f);
            return b.Build("Survivor arm", 60f);
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

        /// <summary>
        /// A metal hatchet: a forged head with a flared blade and a squared poll on a wooden haft, the
        /// grip wrapped. Submeshes: wood, steel, wrap.
        /// </summary>
        public static Mesh MetalHatchet()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.25f };
            b.AddTube(0, new Vector3(0f, -0.04f, 0f), new Vector3(0f, 0.53f, 0f), 0.017f, 0.015f, 8, 0);
            b.AddBox(1, new Vector3(-0.028f, 0.405f, -0.022f), new Vector3(0.03f, 0.49f, 0.022f));
            b.AddBox(1, new Vector3(-0.07f, 0.42f, -0.02f), new Vector3(-0.028f, 0.475f, 0.02f));
            b.AddPrism(1, -0.012f, 0.012f, new Vector2(0.03f, 0.42f), new Vector2(0.11f, 0.39f), new Vector2(0.152f, 0.372f),
                new Vector2(0.158f, 0.505f), new Vector2(0.11f, 0.488f), new Vector2(0.03f, 0.476f));
            b.AddTube(2, new Vector3(0f, -0.03f, 0f), new Vector3(0f, 0.12f, 0f), 0.021f, 0.021f, 8, 2);
            return b.Build("MetalHatchet", 40f);
        }

        /// <summary>
        /// A metal pickaxe: a long curved steel pick, pointed both ways, through an eye on a wooden
        /// haft, the grip wrapped. Submeshes: wood, steel, wrap.
        /// </summary>
        public static Mesh MetalPickaxe()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.25f };
            b.AddTube(0, new Vector3(0f, -0.04f, 0f), new Vector3(0f, 0.56f, 0f), 0.017f, 0.015f, 8, 0);
            b.AddBox(1, new Vector3(-0.03f, 0.46f, -0.022f), new Vector3(0.03f, 0.53f, 0.022f));
            // Each arm in two pieces, bending down towards its point.
            b.AddTube(1, new Vector3(0.025f, 0.5f, 0f), new Vector3(0.12f, 0.485f, 0f), 0.019f, 0.013f, 6, 1);
            b.AddTube(1, new Vector3(0.12f, 0.485f, 0f), new Vector3(0.215f, 0.445f, 0f), 0.013f, 0.003f, 6, 1);
            b.AddTube(1, new Vector3(-0.025f, 0.5f, 0f), new Vector3(-0.11f, 0.487f, 0f), 0.019f, 0.014f, 6, 1);
            b.AddTube(1, new Vector3(-0.11f, 0.487f, 0f), new Vector3(-0.19f, 0.455f, 0f), 0.014f, 0.006f, 6, 1);
            b.AddTube(2, new Vector3(0f, -0.03f, 0f), new Vector3(0f, 0.12f, 0f), 0.021f, 0.021f, 8, 2);
            return b.Build("MetalPickaxe", 40f);
        }

        /// <summary>A torch: a stick with a wrap of charred cloth at the top. Submeshes: wood, cloth.</summary>
        /// <summary>
        /// A wooden spear 2.2 m long, gripped at the origin: a straight shaft along +Y with its point
        /// whittled and hardened black in the fire, and a cord wrap where the hand goes. Submeshes:
        /// wood, charred point, cord.
        /// </summary>
        public static Mesh WoodenSpear()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.25f };
            b.AddTube(0, new Vector3(0f, -0.95f, 0f), new Vector3(0f, 1.02f, 0f), 0.019f, 0.017f, 7, 0);
            b.AddTube(1, new Vector3(0f, 1.02f, 0f), new Vector3(0f, 1.12f, 0f), 0.017f, 0.012f, 7);
            b.AddCone(1, new Vector3(0f, 1.12f, 0f), 0.012f, 0.13f, 7, 0f);
            b.AddTube(2, new Vector3(0f, -0.09f, 0f), new Vector3(0f, 0.09f, 0f), 0.023f, 0.023f, 8, 2);
            b.AddTube(2, new Vector3(0f, 0.62f, 0f), new Vector3(0f, 0.68f, 0f), 0.021f, 0.021f, 8, 2);
            return b.Build("WoodenSpear", 55f);
        }

        /// <summary>Where a bow's string is tied, in the bow's own space (the tips of its limbs).</summary>
        public static readonly Vector3 BowTipTop = new Vector3(0f, 0.56f, -0.11f), BowTipBottom = new Vector3(0f, -0.56f, -0.11f);

        /// <summary>
        /// A short bow held upright, its grip at the origin, the limbs curving back towards the archer
        /// (-Z) and tipped with notches. With <paramref name="strung"/>, the string straight between the
        /// tips (for its icon); in hand the string is drawn apart, so it can be pulled. Submeshes: wood, grip, string.
        /// </summary>
        public static Mesh WoodenBow(bool strung)
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.25f };
            const int steps = 7;
            for (int side = -1; side <= 1; side += 2)
            {
                var previous = new Vector3(0f, side * 0.07f, 0f);
                for (int i = 1; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float y = side * (0.07f + 0.49f * t);
                    // Bends back more towards the tip, with a slight recurve at the very end.
                    float z = -0.13f * t * t + (t > 0.85f ? (t - 0.85f) * 0.25f : 0f);
                    var next = new Vector3(0f, y, z);
                    float r0 = Mathf.Lerp(0.02f, 0.009f, (i - 1) / (float)steps), r1 = Mathf.Lerp(0.02f, 0.009f, t);
                    b.AddTube(0, previous, next, r0, r1, 6, 0);
                    previous = next;
                }
            }
            // The grip, wrapped in cloth.
            b.AddTube(1, new Vector3(0f, -0.09f, 0f), new Vector3(0f, 0.09f, 0f), 0.025f, 0.025f, 8, 1);
            if (strung) b.AddTube(2, BowTipTop, BowTipBottom, 0.004f, 0.004f, 4);
            return b.Build(strung ? "WoodenBow" : "WoodenBowLimbs", 50f);
        }

        /// <summary>An arrow along +Z, the nock at the origin: a thin shaft, three feathers, a knapped stone head. Submeshes: shaft, head, feathers.</summary>
        public static Mesh Arrow()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.2f };
            b.AddTube(0, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0.66f), 0.0065f, 0.0065f, 5, 0);
            b.AddTube(1, new Vector3(0f, 0f, 0.64f), new Vector3(0f, 0f, 0.68f), 0.012f, 0.018f, 4);
            b.AddTube(1, new Vector3(0f, 0f, 0.68f), new Vector3(0f, 0f, 0.76f), 0.018f, 0.0008f, 4);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                var out1 = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                var root0 = new Vector3(0f, 0f, 0.03f) + out1 * 0.005f;
                var root1 = new Vector3(0f, 0f, 0.15f) + out1 * 0.005f;
                var tip0 = new Vector3(0f, 0f, 0.04f) + out1 * 0.026f;
                var tip1 = new Vector3(0f, 0f, 0.12f) + out1 * 0.012f;
                b.Triangle(2, root0, tip0, tip1);
                b.Triangle(2, root0, tip1, root1);
                b.Triangle(2, root0, tip1, tip0);
                b.Triangle(2, root0, root1, tip1);
            }
            return b.Build("Arrow", 40f);
        }

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

        /// <summary>
        /// A twig window: the twig wall's frame with a square hole (see <see cref="Oxfall.Core.Building.Openings"/>),
        /// sticks woven below the sill, beside it and above the lintel.
        /// </summary>
        public static Mesh TwigWindow()
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.6f };
            var random = new System.Random(43);
            const float post = 0.07f, stick = 0.035f;
            float half = Oxfall.Core.Building.Openings.WindowHalfWidth + 0.02f;
            float sill = Oxfall.Core.Building.Openings.WindowBottom, lintel = Oxfall.Core.Building.Openings.WindowTop;
            Stick(b, -1.42f, 0f, -1.42f, 3f, 0f, post);
            Stick(b, 1.42f, 0f, 1.42f, 3f, 0f, post);
            Stick(b, -1.5f, 0.1f, 1.5f, 0.1f, 0f, post);
            Stick(b, -1.5f, 2.9f, 1.5f, 2.9f, 0f, post);
            Stick(b, -half, 0.1f, -half, 2.9f, 0f, post);
            Stick(b, half, 0.1f, half, 2.9f, 0f, post);
            Stick(b, -1.5f, sill, 1.5f, sill, 0f, post);
            Stick(b, -1.5f, lintel, 1.5f, lintel, 0f, post);
            foreach (float side in new[] { -1f, 1f })
            {
                for (int i = 0; i < 3; i++)
                {
                    float x = side * (0.84f + i * 0.22f);
                    Stick(b, x, 0.1f, x + Rand(random, -0.04f, 0.04f), 2.9f, i % 2 == 0 ? 0.035f : -0.035f, stick);
                }
                Stick(b, side * 0.66f, 0.15f, side * 1.38f, 2.85f, 0.075f, 0.045f);
            }
            for (int i = 0; i < 4; i++)
            {
                float x = -0.45f + i * 0.3f;
                Stick(b, x, 0.1f, x + Rand(random, -0.03f, 0.03f), sill, i % 2 == 0 ? 0.035f : -0.035f, stick);
                Stick(b, x, lintel, x + Rand(random, -0.03f, 0.03f), 2.9f, i % 2 == 0 ? -0.035f : 0.035f, stick);
            }
            return b.Build("TwigWindow", 50f);
        }

        /// <summary>Steps in a full turn of spiral stairs.</summary>
        public const int SpiralSteps = 16;

        /// <summary>
        /// Spiral stairs round a post in the middle of a 3 m square, rising <paramref name="rise"/> in
        /// one full turn: the first step just clockwise of +Z (seen from above), the last just before it,
        /// so they are entered and left on the +Z side. A rail runs along the middle steps. Twig: rough
        /// sticks laid across a pole; otherwise solid wedge steps.
        /// </summary>
        /// <summary>
        /// What the player walks on up a spiral stair: its steps and the pole, nothing to snag on
        /// (no rail). Kept readable, for a MeshCollider.
        /// </summary>
        public static Mesh SpiralStairsCollider(float rise)
        {
            var b = new FlatMeshBuilder(1) { KeepReadable = true };
            const float inner = 0.13f, outer = 1.42f, thick = 0.14f;
            int n = SpiralSteps;
            float step = 360f / n;
            Vector3 At(float degrees, float radius, float y)
            {
                float a = degrees * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);
            }
            b.AddBox(0, new Vector3(-0.13f, 0f, -0.13f), new Vector3(0.13f, rise + 1.0f, 0.13f));
            for (int i = 0; i < n; i++)
            {
                float top = (i + 1) * rise / n, a0 = i * step, a1 = (i + 1) * step, lo = top - thick;
                b.AddHex(0, (ix, iy, iz) => At(ix == 0 ? a0 : a1, iz == 0 ? inner : outer, iy == 0 ? lo : top));
            }
            return b.Build("SpiralStairsCollider");
        }

        public static Mesh SpiralStairs(float rise, bool twig)
        {
            var b = new FlatMeshBuilder(1) { UvScale = twig ? 0.6f : 1.6f };
            var random = new System.Random(59);
            const float inner = 0.13f, outer = 1.42f, thick = 0.14f;
            int n = SpiralSteps;
            float step = 360f / n;
            Vector3 At(float degrees, float radius, float y)
            {
                float a = degrees * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius);
            }
            if (twig) b.AddTube(0, new Vector3(0f, 0f, 0f), new Vector3(0f, rise + 1.0f, 0f), 0.1f, 0.085f, 7);
            else b.AddBox(0, new Vector3(-0.13f, 0f, -0.13f), new Vector3(0.13f, rise + 1.0f, 0.13f));
            for (int i = 0; i < n; i++)
            {
                float top = (i + 1) * rise / n, a0 = i * step, a1 = (i + 1) * step;
                if (twig)
                {
                    // Three sticks fanned across the step, lashed to the pole.
                    for (int k = 0; k < 3; k++)
                    {
                        float a = Mathf.Lerp(a0 + 3f, a1 - 3f, k / 2f) + Rand(random, -1.5f, 1.5f);
                        b.AddTube(0, At(a, inner, top - 0.05f), At(a + Rand(random, -2f, 2f), outer, top - 0.05f + Rand(random, -0.02f, 0.02f)),
                            0.05f, 0.04f, 5);
                    }
                    b.AddTube(0, At(a0 + step * 0.5f, outer - 0.1f, top - 0.12f), At(a0 + step * 0.5f, inner, top - 0.12f - rise / n), 0.035f, 0.03f, 5);
                }
                else
                {
                    var lo = top - thick;
                    b.AddHex(0, (ix, iy, iz) => At(ix == 0 ? a0 : a1, iz == 0 ? inner : outer, iy == 0 ? lo : top));
                }
            }
            // The rail: posts on the outer ends of the middle steps (the first and last few stay open to step on and off).
            const int railFrom = 3, railTo = 12;
            Vector3 railPrev = default;
            for (int i = railFrom; i <= railTo; i++)
            {
                float top = (i + 1) * rise / n, a = (i + 0.5f) * step;
                var foot = At(a, outer - 0.08f, top);
                var head = foot + new Vector3(0f, 0.9f, 0f);
                b.AddTube(0, foot, head, twig ? 0.035f : 0.03f, twig ? 0.03f : 0.03f, 5);
                if (i > railFrom) b.AddTube(0, railPrev, head, twig ? 0.04f : 0.035f, twig ? 0.04f : 0.035f, 5);
                railPrev = head;
            }
            return b.Build(twig ? "TwigSpiralStairs" : "SpiralStairs", twig ? 50f : 0f);
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

        /// <summary>
        /// A sheet metal door, hinged at the origin like the plank one (1.16 m wide along +X, 2.17 m
        /// tall): a corrugated panel in an iron frame with a brace, three hinges and a pull handle
        /// each side. Submeshes: sheet metal, iron.
        /// </summary>
        public static Mesh MetalDoor()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 1.6f };
            b.AddBox(0, new Vector3(0.03f, 0.02f, -0.03f), new Vector3(1.13f, 2.14f, 0.03f));
            // The frame round the edge, standing proud of the panel on both faces.
            const float z = 0.05f;
            b.AddBox(1, new Vector3(0.0f, 0f, -z), new Vector3(1.16f, 0.08f, z));
            b.AddBox(1, new Vector3(0.0f, 2.09f, -z), new Vector3(1.16f, 2.17f, z));
            b.AddBox(1, new Vector3(0.0f, 0f, -z), new Vector3(0.08f, 2.17f, z));
            b.AddBox(1, new Vector3(1.08f, 0f, -z), new Vector3(1.16f, 2.17f, z));
            b.AddBox(1, new Vector3(0.08f, 1.02f, -0.045f), new Vector3(1.08f, 1.1f, 0.045f));
            foreach (float y in new[] { 0.25f, 1.06f, 1.88f })
                b.AddBox(1, new Vector3(-0.025f, y - 0.07f, -0.035f), new Vector3(0.18f, y + 0.07f, 0.035f));
            foreach (float side in new[] { 1f, -1f })
            {
                // A pull handle: two stand-offs and a bar.
                float z0 = side * 0.05f, z1 = side * 0.11f;
                b.AddBox(1, new Vector3(0.92f, 0.86f, Mathf.Min(z0, z1)), new Vector3(0.96f, 0.9f, Mathf.Max(z0, z1)));
                b.AddBox(1, new Vector3(0.92f, 1.22f, Mathf.Min(z0, z1)), new Vector3(0.96f, 1.26f, Mathf.Max(z0, z1)));
                b.AddTube(1, new Vector3(0.94f, 0.86f, side * 0.115f), new Vector3(0.94f, 1.26f, side * 0.115f), 0.018f, 0.018f, 6);
            }
            return b.Build("MetalDoor");
        }

        /// <summary>
        /// A code lock: a small dark keypad with twelve keys, a light above them and a latch on its
        /// side, its back on the origin and its face towards +Z. Submeshes: casing, keys, light.
        /// </summary>
        public static Mesh CodeLock()
        {
            var b = new FlatMeshBuilder(3);
            b.AddBox(0, new Vector3(-0.055f, -0.085f, 0f), new Vector3(0.055f, 0.085f, 0.032f));
            b.AddBox(0, new Vector3(-0.05f, -0.08f, 0.032f), new Vector3(0.05f, 0.05f, 0.036f));
            for (int row = 0; row < 4; row++)
            for (int col = 0; col < 3; col++)
            {
                float x = (col - 1) * 0.03f, y = 0.028f - row * 0.03f;
                b.AddBox(1, new Vector3(x - 0.011f, y - 0.011f, 0.036f), new Vector3(x + 0.011f, y + 0.011f, 0.043f));
            }
            b.AddBox(2, new Vector3(-0.03f, 0.058f, 0.032f), new Vector3(0.03f, 0.072f, 0.04f));
            b.AddBox(1, new Vector3(0.055f, -0.03f, 0.006f), new Vector3(0.078f, 0.03f, 0.026f));
            return b.Build("CodeLock");
        }

        private static void Stick(FlatMeshBuilder b, float x0, float y0, float x1, float y1, float z, float radius) =>
            b.AddTube(0, new Vector3(x0, y0, z), new Vector3(x1, y1, z), radius, radius * 0.85f, 5);

        /// <summary>
        /// The flame of a torch or a campfire: a quad 0.2 m wide and 0.32 m tall standing on the
        /// origin. Oxfall/Flame turns it to the camera and draws the fire on it, so its bounds are
        /// widened to cover every way it can turn.
        /// </summary>
        public static Mesh Flame()
        {
            var mesh = new Mesh { name = "Flame" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.1f, 0f, 0f), new Vector3(-0.1f, 0.32f, 0f), new Vector3(0.1f, 0.32f, 0f), new Vector3(0.1f, 0f, 0f),
            });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
            mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.bounds = new Bounds(new Vector3(0f, 0.16f, 0f), new Vector3(0.3f, 0.36f, 0.3f));
            return mesh;
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

        /// <summary>
        /// A wooden storage box about 0.9 m wide, its front towards +Z: planked sides, dark corner
        /// posts and bands, an iron latch and side handles. Submeshes: planks, frame, metal.
        /// </summary>
        public static Mesh StorageBox()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.8f };
            b.AddBox(0, new Vector3(-0.45f, 0.02f, -0.28f), new Vector3(0.45f, 0.5f, 0.28f));
            b.AddBox(0, new Vector3(-0.47f, 0.5f, -0.3f), new Vector3(0.47f, 0.58f, 0.3f));
            foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                b.AddBox(1, new Vector3(x * 0.48f - (x > 0 ? 0.06f : 0f), 0f, z * 0.31f - (z > 0 ? 0.06f : 0f)),
                    new Vector3(x * 0.48f + (x < 0 ? 0.06f : 0f), 0.5f, z * 0.31f + (z < 0 ? 0.06f : 0f)));
            foreach (float y in new[] { 0.06f, 0.4f })
            {
                b.AddBox(1, new Vector3(-0.44f, y, 0.28f), new Vector3(0.44f, y + 0.06f, 0.31f));
                b.AddBox(1, new Vector3(-0.44f, y, -0.31f), new Vector3(0.44f, y + 0.06f, -0.28f));
            }
            b.AddBox(1, new Vector3(-0.48f, 0.5f, 0.27f), new Vector3(0.48f, 0.6f, 0.32f));
            b.AddBox(2, new Vector3(-0.05f, 0.4f, 0.31f), new Vector3(0.05f, 0.55f, 0.335f));
            b.AddBox(2, new Vector3(-0.025f, 0.43f, 0.335f), new Vector3(0.025f, 0.47f, 0.35f));
            foreach (float x in new[] { -1f, 1f })
                b.AddTube(2, new Vector3(x * 0.49f, 0.33f, -0.1f), new Vector3(x * 0.49f, 0.33f, 0.1f), 0.018f, 0.018f, 5);
            return b.Build("StorageBox");
        }

        /// <summary>
        /// A small stone furnace about a metre tall, its mouth towards +Z: a tapering stone body
        /// under a dome, a chimney, and an iron-framed opening. Submeshes: stone, char, metal.
        /// </summary>
        public static Mesh Furnace()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.6f };
            var random = new System.Random(31);
            b.AddCylinder(0, Vector3.zero, 0.6f, 0.46f, 0.8f, 10, 9f);
            b.AddCone(0, new Vector3(0f, 0.8f, 0f), 0.46f, 0.3f, 10, 9f);
            b.AddTube(0, new Vector3(0.12f, 0.92f, -0.08f), new Vector3(0.12f, 1.42f, -0.08f), 0.13f, 0.11f, 7, 1);
            // Rough stones round the foot.
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f + Rand(random, -0.1f, 0.1f);
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f)) < 26f) continue;
                b.AddRock(0, new Vector3(Mathf.Cos(a) * 0.6f, 0.05f, Mathf.Sin(a) * 0.6f),
                    new Vector3(Rand(random, 0.1f, 0.14f), Rand(random, 0.07f, 0.1f), Rand(random, 0.1f, 0.14f)), random);
            }
            // The mouth: dark inside, an iron frame round it, a grate under it.
            b.AddBox(1, new Vector3(-0.2f, 0.12f, 0.36f), new Vector3(0.2f, 0.46f, 0.58f));
            b.AddBox(2, new Vector3(-0.25f, 0.46f, 0.5f), new Vector3(0.25f, 0.52f, 0.61f));
            b.AddBox(2, new Vector3(-0.25f, 0.08f, 0.5f), new Vector3(0.25f, 0.13f, 0.61f));
            b.AddBox(2, new Vector3(-0.25f, 0.08f, 0.5f), new Vector3(-0.2f, 0.52f, 0.61f));
            b.AddBox(2, new Vector3(0.2f, 0.08f, 0.5f), new Vector3(0.25f, 0.52f, 0.61f));
            for (int i = 0; i < 4; i++)
            {
                float x = -0.13f + i * 0.087f;
                b.AddTube(2, new Vector3(x, 0.15f, 0.36f), new Vector3(x, 0.15f, 0.6f), 0.012f, 0.012f, 4);
            }
            return b.Build("Furnace", 35f);
        }

        /// <summary>
        /// A sleeping bag about two metres long along Z, quilted, with a pillow at +Z and a strap
        /// at the foot. Submeshes: bag, lining, rope.
        /// </summary>
        public static Mesh SleepingBag()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.5f };
            const int quilts = 7;
            for (int i = 0; i < quilts; i++)
            {
                float z0 = -0.95f + i * 0.26f, z1 = z0 + 0.25f;
                float h = 0.1f + 0.025f * Mathf.Sin(i * 1.7f);
                float w = 0.37f - 0.012f * Mathf.Abs(i - 3);
                b.AddBox(0, new Vector3(-w, 0f, z0), new Vector3(w, h, z1));
            }
            // The top folded back a little, showing the lining.
            b.AddBox(1, new Vector3(-0.35f, 0.11f, 0.4f), new Vector3(0.35f, 0.14f, 0.85f));
            b.AddBox(1, new Vector3(-0.28f, 0.02f, 0.88f), new Vector3(0.28f, 0.17f, 1.08f));
            b.AddTube(2, new Vector3(-0.38f, 0.07f, -0.8f), new Vector3(0.38f, 0.07f, -0.8f), 0.075f, 0.075f, 6);
            return b.Build("SleepingBag", 30f);
        }

        /// <summary>
        /// What a dead player leaves: a lumpy sack of coarse cloth about 0.6 m long, lying on its side
        /// with its gathered neck tied off towards +Z. Submeshes: cloth, cord.
        /// </summary>
        public static Mesh LootBag()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.6f };
            var random = new System.Random(61);
            b.AddBlob(0, new Vector3(0f, 0.16f, -0.06f), new Vector3(0.25f, 0.16f, 0.27f), random, 0.16f);
            b.AddBlob(0, new Vector3(0.04f, 0.12f, 0.12f), new Vector3(0.19f, 0.12f, 0.16f), random, 0.14f);
            // The neck, gathered and tied, and the cloth above the knot splayed open.
            b.AddTube(0, new Vector3(0.02f, 0.15f, 0.22f), new Vector3(0.03f, 0.12f, 0.33f), 0.07f, 0.035f, 7);
            b.AddTube(0, new Vector3(0.03f, 0.12f, 0.33f), new Vector3(0.04f, 0.1f, 0.42f), 0.035f, 0.075f, 7);
            b.AddTube(1, new Vector3(0.025f, 0.13f, 0.315f), new Vector3(0.03f, 0.125f, 0.345f), 0.045f, 0.045f, 8);
            // A loose end of the cord.
            b.AddTube(1, new Vector3(0.06f, 0.12f, 0.34f), new Vector3(0.16f, 0.02f, 0.4f), 0.012f, 0.01f, 4);
            return b.Build("LootBag", 50f);
        }

        /// <summary>
        /// A tool cupboard about 1.5 m tall, its doors towards +Z: a planked cabinet in a dark frame
        /// with iron hinges and handles, a hammer and a saw hung on the side. Submeshes: planks, frame, metal.
        /// </summary>
        public static Mesh ToolCupboard()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.9f };
            b.AddBox(0, new Vector3(-0.38f, 0.06f, -0.2f), new Vector3(0.38f, 1.42f, 0.2f));
            foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                b.AddBox(1, new Vector3(x * 0.42f - (x > 0 ? 0.06f : 0f), 0f, z * 0.23f - (z > 0 ? 0.06f : 0f)),
                    new Vector3(x * 0.42f + (x < 0 ? 0.06f : 0f), 1.52f, z * 0.23f + (z < 0 ? 0.06f : 0f)));
            b.AddBox(1, new Vector3(-0.45f, 1.42f, -0.25f), new Vector3(0.45f, 1.52f, 0.25f));
            b.AddBox(1, new Vector3(-0.4f, 0.05f, -0.22f), new Vector3(0.4f, 0.14f, 0.22f));
            // Two doors with a gap between them.
            b.AddBox(0, new Vector3(-0.36f, 0.16f, 0.2f), new Vector3(-0.01f, 1.38f, 0.235f));
            b.AddBox(0, new Vector3(0.01f, 0.16f, 0.2f), new Vector3(0.36f, 1.38f, 0.235f));
            b.AddBox(1, new Vector3(-0.36f, 0.74f, 0.235f), new Vector3(-0.01f, 0.8f, 0.25f));
            b.AddBox(1, new Vector3(0.01f, 0.74f, 0.235f), new Vector3(0.36f, 0.8f, 0.25f));
            foreach (float x in new[] { -0.07f, 0.07f })
                b.AddBox(2, new Vector3(x - 0.015f, 0.82f, 0.235f), new Vector3(x + 0.015f, 1.0f, 0.26f));
            foreach (float y in new[] { 0.3f, 1.2f })
            {
                b.AddBox(2, new Vector3(-0.37f, y, 0.23f), new Vector3(-0.3f, y + 0.06f, 0.245f));
                b.AddBox(2, new Vector3(0.3f, y, 0.23f), new Vector3(0.37f, y + 0.06f, 0.245f));
            }
            // A hammer and a saw hung on the right side.
            b.AddTube(1, new Vector3(0.465f, 0.7f, 0.05f), new Vector3(0.465f, 1.15f, 0.05f), 0.018f, 0.018f, 5);
            b.AddBox(2, new Vector3(0.45f, 1.1f, -0.02f), new Vector3(0.49f, 1.17f, 0.15f));
            b.AddBox(2, new Vector3(0.445f, 0.45f, -0.17f), new Vector3(0.455f, 0.95f, -0.05f));
            b.AddBox(1, new Vector3(0.44f, 0.92f, -0.19f), new Vector3(0.47f, 1.04f, -0.03f));
            return b.Build("ToolCupboard");
        }

        /// <summary>A few jagged scraps of rusty metal.</summary>
        public static Mesh MetalScraps()
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.2f };
            var random = new System.Random(12);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f;
                var at = new Vector3(Mathf.Cos(a) * 0.07f * (i % 3), 0.03f + i * 0.018f, Mathf.Sin(a) * 0.07f * (i % 3));
                b.AddRock(0, at, new Vector3(Rand(random, 0.06f, 0.1f), Rand(random, 0.015f, 0.028f), Rand(random, 0.04f, 0.07f)), random);
            }
            return b.Build("MetalScraps");
        }

        /// <summary>
        /// A workbench: a thick plank top on steel legs with a shelf under it, a vise on one corner,
        /// a pegboard of tools along the back. 1.8 m wide (along X), its front towards +Z, the top at
        /// 0.9 m. Submeshes: 0 planks, 1 steel, 2 dark metal.
        /// </summary>
        public static Mesh Workbench()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.9f };
            const float w = 0.9f, d = 0.42f, top = 0.9f;
            b.AddBox(0, new Vector3(-w, top - 0.08f, -d), new Vector3(w, top, d));
            b.AddBox(0, new Vector3(-w + 0.08f, 0.22f, -d + 0.06f), new Vector3(w - 0.08f, 0.26f, d - 0.06f));
            foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                b.AddBox(1, new Vector3(x * (w - 0.05f) - 0.03f, 0f, z * (d - 0.05f) - 0.03f), new Vector3(x * (w - 0.05f) + 0.03f, top - 0.08f, z * (d - 0.05f) + 0.03f));
            // Rails under the top, front and back.
            b.AddBox(1, new Vector3(-w + 0.05f, top - 0.16f, d - 0.08f), new Vector3(w - 0.05f, top - 0.08f, d - 0.03f));
            b.AddBox(1, new Vector3(-w + 0.05f, top - 0.16f, -d + 0.03f), new Vector3(w - 0.05f, top - 0.08f, -d + 0.08f));
            // The pegboard and the tools on it.
            b.AddBox(0, new Vector3(-w + 0.02f, top, -d), new Vector3(w - 0.02f, top + 0.62f, -d + 0.04f));
            b.AddBox(1, new Vector3(-w, top, -d - 0.01f), new Vector3(-w + 0.04f, top + 0.66f, -d + 0.05f));
            b.AddBox(1, new Vector3(w - 0.04f, top, -d - 0.01f), new Vector3(w, top + 0.66f, -d + 0.05f));
            b.AddTube(1, new Vector3(-0.55f, top + 0.18f, -d + 0.07f), new Vector3(-0.55f, top + 0.5f, -d + 0.07f), 0.016f, 0.016f, 6);
            b.AddBox(2, new Vector3(-0.6f, top + 0.46f, -d + 0.05f), new Vector3(-0.5f, top + 0.52f, -d + 0.1f));
            b.AddBox(2, new Vector3(-0.3f, top + 0.12f, -d + 0.05f), new Vector3(-0.29f, top + 0.5f, -d + 0.07f));
            b.AddBox(0, new Vector3(-0.33f, top + 0.46f, -d + 0.04f), new Vector3(-0.26f, top + 0.56f, -d + 0.09f));
            b.AddTube(2, new Vector3(0.1f, top + 0.42f, -d + 0.07f), new Vector3(0.3f, top + 0.3f, -d + 0.07f), 0.012f, 0.012f, 6);
            b.AddTube(2, new Vector3(0.45f, top + 0.15f, -d + 0.07f), new Vector3(0.45f, top + 0.48f, -d + 0.07f), 0.02f, 0.012f, 6);
            // The vise on the front right corner, and a box of parts on the top.
            b.AddBox(2, new Vector3(0.55f, top, d - 0.18f), new Vector3(0.75f, top + 0.06f, d + 0.04f));
            b.AddBox(2, new Vector3(0.55f, top + 0.06f, d - 0.06f), new Vector3(0.75f, top + 0.18f, d - 0.02f));
            b.AddBox(2, new Vector3(0.55f, top + 0.06f, d + 0.0f), new Vector3(0.75f, top + 0.18f, d + 0.04f));
            b.AddTube(1, new Vector3(0.65f, top + 0.1f, d + 0.04f), new Vector3(0.65f, top + 0.1f, d + 0.2f), 0.012f, 0.012f, 6);
            b.AddBox(1, new Vector3(0.58f, top + 0.08f, d + 0.18f), new Vector3(0.72f, top + 0.12f, d + 0.21f));
            b.AddBox(2, new Vector3(-0.7f, top, -0.1f), new Vector3(-0.38f, top + 0.12f, 0.16f));
            return b.Build("Workbench");
        }

        /// <summary>
        /// A supply crate: a long olive box with dark corner bands, two latches and handles at the
        /// ends, 1.1 x 0.62 x 0.66 m on the origin. Submeshes: 0 paint, 1 bands and latches.
        /// </summary>
        public static Mesh SupplyCrate()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.8f };
            const float x = 0.55f, h = 0.62f, z = 0.33f, t = 0.035f;
            b.AddBox(0, new Vector3(-x + t, 0.02f, -z + t), new Vector3(x - t, h - 0.02f, z - t));
            // The lid's seam and its overhang.
            b.AddBox(0, new Vector3(-x + 0.01f, h - 0.14f, -z + 0.01f), new Vector3(x - 0.01f, h, z - 0.01f));
            b.AddBox(1, new Vector3(-x + 0.012f, h - 0.155f, -z + 0.012f), new Vector3(x - 0.012f, h - 0.14f, z - 0.012f));
            // Corner bands.
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                b.AddBox(1, new Vector3(sx * x - (sx > 0 ? t * 2f : 0f), 0f, sz * z - (sz > 0 ? t * 2f : 0f)),
                    new Vector3(sx * x + (sx < 0 ? t * 2f : 0f), h, sz * z + (sz < 0 ? t * 2f : 0f)));
            b.AddBox(1, new Vector3(-x, 0f, -z), new Vector3(x, 0.04f, z));
            // Latches on the front, handles at the ends.
            foreach (float lx in new[] { -0.3f, 0.3f })
            {
                b.AddBox(1, new Vector3(lx - 0.045f, h - 0.22f, z - 0.005f), new Vector3(lx + 0.045f, h - 0.08f, z + 0.02f));
                b.AddBox(1, new Vector3(lx - 0.02f, h - 0.26f, z), new Vector3(lx + 0.02f, h - 0.21f, z + 0.03f));
            }
            foreach (float sx in new[] { -1f, 1f })
                b.AddTube(1, new Vector3(sx * (x + 0.035f), h * 0.62f, -0.13f), new Vector3(sx * (x + 0.035f), h * 0.62f, 0.13f), 0.015f, 0.015f, 6);
            // A stencilled band across the lid.
            b.AddBox(1, new Vector3(-0.12f, h, -z + 0.03f), new Vector3(0.12f, h + 0.004f, z - 0.03f));
            return b.Build("SupplyCrate");
        }

        /// <summary>
        /// The ruins' recycler: a squat machine with a hopper on top, a chute out of its front, a
        /// motor on its left side and a lamp on its panel. Its front looks along +Z; 1.8 m wide.
        /// Submeshes: 0 paint, 1 steel, 2 rust, 3 the lamp (red while it stands, green while it runs).
        /// </summary>
        public static Mesh Recycler()
        {
            var b = new FlatMeshBuilder(4) { UvScale = 0.9f };
            // The skid it stands on, its body, the steel bands round it and its corner posts.
            b.AddBox(1, new Vector3(-0.9f, 0f, -0.62f), new Vector3(0.9f, 0.12f, 0.62f));
            b.AddBox(0, new Vector3(-0.84f, 0.12f, -0.56f), new Vector3(0.84f, 1.22f, 0.52f));
            foreach (float y in new[] { 0.42f, 0.95f })
                b.AddBox(1, new Vector3(-0.86f, y, -0.58f), new Vector3(0.86f, y + 0.05f, 0.54f));
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                float cx = sx * 0.83f, cz = sz > 0f ? 0.5f : -0.54f;
                b.AddBox(1, new Vector3(cx - 0.05f, 0.12f, cz - 0.05f), new Vector3(cx + 0.05f, 1.26f, cz + 0.05f));
            }
            // The hopper, wider at the top, dark inside, with a steel rim.
            b.AddHex(0, (ix, iy, iz) =>
            {
                float y = iy == 0 ? 1.22f : 1.62f, hx = iy == 0 ? 0.5f : 0.7f;
                float z = iz == 0 ? (iy == 0 ? -0.36f : -0.5f) : (iy == 0 ? 0.24f : 0.4f);
                return new Vector3(ix == 0 ? -hx : hx, y, z);
            });
            b.AddBox(2, new Vector3(-0.63f, 1.62f, -0.44f), new Vector3(0.63f, 1.632f, 0.34f));
            b.AddBox(1, new Vector3(-0.72f, 1.6f, 0.37f), new Vector3(0.72f, 1.66f, 0.42f));
            b.AddBox(1, new Vector3(-0.72f, 1.6f, -0.52f), new Vector3(0.72f, 1.66f, -0.47f));
            b.AddBox(1, new Vector3(-0.72f, 1.6f, -0.52f), new Vector3(-0.67f, 1.66f, 0.42f));
            b.AddBox(1, new Vector3(0.67f, 1.6f, -0.52f), new Vector3(0.72f, 1.66f, 0.42f));
            // Its mouth at the front, and the chute sloping out of it between two steel lips.
            b.AddBox(2, new Vector3(-0.4f, 0.58f, 0.52f), new Vector3(0.4f, 0.88f, 0.535f));
            b.AddHex(2, (ix, iy, iz) =>
            {
                float y = (iz == 0 ? 0.56f : 0.4f) + (iy == 0 ? 0f : 0.04f);
                return new Vector3(ix == 0 ? -0.42f : 0.42f, y, iz == 0 ? 0.5f : 0.9f);
            });
            foreach (float sx in new[] { -1f, 1f })
                b.AddHex(1, (ix, iy, iz) =>
                {
                    float y = (iz == 0 ? 0.56f : 0.4f) + (iy == 0 ? 0f : 0.14f);
                    return new Vector3(sx * 0.42f + (ix == 0 ? -0.03f : 0.03f), y, iz == 0 ? 0.5f : 0.9f);
                });
            // The panel on the front right: its lamp, and a lever.
            b.AddBox(1, new Vector3(0.5f, 0.62f, 0.52f), new Vector3(0.8f, 1.12f, 0.6f));
            b.AddBox(3, new Vector3(0.59f, 0.97f, 0.6f), new Vector3(0.71f, 1.06f, 0.645f));
            b.AddTube(2, new Vector3(0.72f, 0.76f, 0.6f), new Vector3(0.72f, 0.76f, 0.76f), 0.02f, 0.02f, 6);
            b.AddCylinder(2, new Vector3(0.72f, 0.735f, 0.78f), 0.035f, 0.035f, 0.05f, 6, 0f);
            // The motor on its left side (its flywheel is a mesh of its own, so it can turn).
            b.AddTube(1, new Vector3(-0.84f, 0.62f, -0.12f), new Vector3(-0.99f, 0.62f, -0.12f), 0.24f, 0.24f, 12, 1);
            b.AddTube(2, new Vector3(-0.84f, 0.62f, -0.12f), new Vector3(-0.9f, 0.62f, -0.12f), 0.27f, 0.27f, 12, 2);
            // An exhaust up the back.
            b.AddTube(1, new Vector3(0.55f, 1.22f, -0.45f), new Vector3(0.55f, 1.95f, -0.45f), 0.055f, 0.055f, 8);
            b.AddTube(2, new Vector3(0.55f, 1.93f, -0.45f), new Vector3(0.55f, 2.0f, -0.45f), 0.075f, 0.075f, 8, 2);
            return b.Build("Recycler");
        }

        /// <summary>
        /// The recycler's flywheel: a disc with four spokes and a hub, facing -X from its middle
        /// (it turns about X while the machine runs). Submeshes: 0 steel, 1 rust.
        /// </summary>
        public static Mesh RecyclerWheel()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.4f };
            b.AddTube(0, Vector3.zero, new Vector3(-0.04f, 0f, 0f), 0.2f, 0.2f, 16, 0);
            b.AddBox(1, new Vector3(-0.065f, -0.18f, -0.025f), new Vector3(-0.04f, 0.18f, 0.025f));
            b.AddBox(1, new Vector3(-0.065f, -0.025f, -0.18f), new Vector3(-0.04f, 0.025f, 0.18f));
            b.AddTube(1, new Vector3(-0.04f, 0f, 0f), new Vector3(-0.09f, 0f, 0f), 0.05f, 0.04f, 8, 1);
            return b.Build("RecyclerWheel");
        }

        /// <summary>Scrap: bent plates, a bolt and a cog's worth of rusty odds and ends.</summary>
        public static Mesh ScrapPile()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.2f };
            var random = new System.Random(77);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.3f;
                var at = new Vector3(Mathf.Cos(a) * 0.06f * (1 + i % 2), 0.02f + i * 0.016f, Mathf.Sin(a) * 0.06f * (1 + i % 2));
                b.AddRock(i % 2, at, new Vector3(Rand(random, 0.07f, 0.11f), Rand(random, 0.01f, 0.02f), Rand(random, 0.05f, 0.08f)), random);
            }
            b.AddTube(1, new Vector3(-0.09f, 0.05f, 0.05f), new Vector3(0.05f, 0.07f, 0.1f), 0.014f, 0.014f, 6);
            b.AddCylinder(1, new Vector3(-0.09f, 0.04f, 0.05f), 0.026f, 0.026f, 0.025f, 6, 0f);
            return b.Build("ScrapPile");
        }

        /// <summary>Gears: a big cog and a small one meshing, lying flat.</summary>
        public static Mesh Gears()
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.2f };
            void Cog(Vector3 centre, float radius, int teeth, float spin)
            {
                const float thick = 0.03f;
                b.AddCylinder(0, centre, radius, radius, thick, 18, 0f);
                for (int i = 0; i < teeth; i++)
                {
                    float a0 = (i + spin) * Mathf.PI * 2f / teeth, half = Mathf.PI / teeth * 0.45f;
                    float r0 = radius - 0.004f, r1 = radius + 0.022f;
                    b.AddHex(0, (ix, iy, iz) =>
                    {
                        float a = a0 + (ix == 0 ? -half : half) * (iz == 0 ? 1f : 0.7f);
                        float r = iz == 0 ? r0 : r1;
                        return centre + new Vector3(Mathf.Sin(a) * r, iy == 0 ? 0f : thick, Mathf.Cos(a) * r);
                    });
                }
                b.AddCylinder(0, centre + new Vector3(0f, thick, 0f), radius * 0.3f, radius * 0.3f, 0.012f, 10, 0f);
            }
            Cog(new Vector3(-0.04f, 0f, 0f), 0.11f, 14, 0f);
            Cog(new Vector3(0.115f, 0f, 0.05f), 0.055f, 8, 0.5f);
            return b.Build("Gears");
        }

        /// <summary>A coil spring standing on its end.</summary>
        public static Mesh MetalSpring()
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.2f };
            const int turns = 6, steps = 10;
            const float radius = 0.055f, height = 0.26f, wire = 0.009f;
            Vector3 At(int i)
            {
                float t = i / (float)(turns * steps);
                float a = t * turns * Mathf.PI * 2f;
                return new Vector3(Mathf.Cos(a) * radius, wire + t * (height - 2f * wire), Mathf.Sin(a) * radius);
            }
            for (int i = 0; i < turns * steps; i++) b.AddTube(0, At(i), At(i + 1), wire, wire, 5);
            return b.Build("MetalSpring", 60f);
        }

        /// <summary>A length of metal pipe with a threaded collar at each end.</summary>
        public static Mesh MetalPipe()
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.3f };
            const float half = 0.2f, r = 0.032f;
            b.AddTube(0, new Vector3(-half, r, 0f), new Vector3(half, r, 0f), r, r, 12);
            foreach (float x in new[] { -half, half - 0.05f })
                b.AddTube(0, new Vector3(x, r, 0f), new Vector3(x + 0.05f, r, 0f), r + 0.008f, r + 0.008f, 12);
            return b.Build("MetalPipe", 40f);
        }

        /// <summary>Lumps of charcoal.</summary>
        public static Mesh CharcoalLumps()
        {
            var b = new FlatMeshBuilder(1) { UvScale = 0.2f };
            var random = new System.Random(27);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.3f;
                var at = new Vector3(Mathf.Cos(a) * 0.08f, 0.04f + (i == 4 ? 0.06f : 0f), Mathf.Sin(a) * 0.08f);
                b.AddRock(0, at, new Vector3(Rand(random, 0.05f, 0.07f), Rand(random, 0.04f, 0.055f), Rand(random, 0.05f, 0.07f)), random);
            }
            return b.Build("CharcoalLumps", 50f);
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

        /// <summary>
        /// One flat-shaded mesh of boxes each turned about its own middle (a monument's walls and
        /// slabs). Textures tile by the metre, the same on every block.
        /// </summary>
        public static Mesh Blocks(string name, IEnumerable<(Vector3 Centre, Quaternion Rotation, Vector3 Size)> blocks)
        {
            var b = new FlatMeshBuilder(1) { UvScale = 2.4f };
            foreach (var (centre, rotation, size) in blocks)
            {
                var half = size * 0.5f;
                b.AddHex(0, (ix, iy, iz) => centre + rotation * new Vector3(ix == 0 ? -half.x : half.x, iy == 0 ? -half.y : half.y, iz == 0 ? -half.z : half.z));
            }
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

        /// <summary>
        /// The surface of a pond: flat at the origin where the ground under it is lower, and tucked
        /// under the ground wherever the ground rises above the water, so no edge of it ever floats
        /// over the bank. The shoreline is wherever the ground meets the water.
        /// </summary>
        /// <param name="groundAbove">Height of the drawn ground above the water at a point (x, z) of the surface.</param>
        public static Mesh PondSurface(float radius, int rings, int segments, System.Func<float, float, float> groundAbove)
        {
            var vertices = new List<Vector3>(1 + rings * segments);
            var triangles = new List<int>(segments * 3 + (rings - 1) * segments * 6);
            float Surface(float x, float z)
            {
                // Flat until the bank is well above the water, then tucked under it: the shore line
                // is where the drawn ground crosses the flat water, not where the rings happen to fall.
                float ground = groundAbove(x, z);
                return ground < 0.25f ? 0f : -0.3f;
            }
            vertices.Add(new Vector3(0f, Surface(0f, 0f), 0f));
            for (int r = 1; r <= rings; r++)
            {
                float d = radius * r / rings;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    float x = Mathf.Cos(a) * d, z = Mathf.Sin(a) * d;
                    vertices.Add(new Vector3(x, Surface(x, z), z));
                }
            }
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                triangles.Add(0); triangles.Add(1 + next); triangles.Add(1 + i);
            }
            for (int r = 1; r < rings; r++)
            {
                int inner = 1 + (r - 1) * segments, outer = 1 + r * segments;
                for (int i = 0; i < segments; i++)
                {
                    int next = (i + 1) % segments;
                    // Clockwise seen from above, so the faces point up.
                    triangles.Add(inner + i); triangles.Add(inner + next); triangles.Add(outer + next);
                    triangles.Add(inner + i); triangles.Add(outer + next); triangles.Add(outer + i);
                }
            }
            var normals = new List<Vector3>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++) normals.Add(Vector3.up);
            var mesh = new Mesh { name = "Pond" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
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

            /// <summary>Keep the mesh's data on the CPU too (a mesh a MeshCollider is made from must).</summary>
            public bool KeepReadable;

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

            /// <summary>
            /// A flat shape in the XY plane (a convex outline, its corners counter-clockwise seen
            /// from +Z) given thickness from z0 to z1.
            /// </summary>
            public void AddPrism(int submesh, float z0, float z1, params Vector2[] outline)
            {
                int n = outline.Length;
                for (int i = 1; i < n - 1; i++)
                {
                    Triangle(submesh, V(outline[0], z1), V(outline[i], z1), V(outline[i + 1], z1));
                    Triangle(submesh, V(outline[0], z0), V(outline[i + 1], z0), V(outline[i], z0));
                }
                for (int i = 0; i < n; i++)
                {
                    var a = outline[i];
                    var c = outline[(i + 1) % n];
                    Quad(submesh, V(a, z0), V(c, z0), V(c, z1), V(a, z1));
                }
            }

            private static Vector3 V(Vector2 p, float z) => new Vector3(p.x, p.y, z);

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

            /// <summary>
            /// A box bent to any eight corners, <paramref name="corner"/>(x, y, z) giving the corner a box
            /// would have at its low (0) or high (1) side along each axis; faces are wound as
            /// <see cref="AddBox"/> winds them, so the corners must keep the box's handedness.
            /// </summary>
            public void AddHex(int submesh, System.Func<int, int, int, Vector3> corner)
            {
                Vector3 P(int x, int y, int z) => corner(x, y, z);
                Quad(submesh, P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0)); // top
                Quad(submesh, P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)); // bottom
                Quad(submesh, P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)); // +X
                Quad(submesh, P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0)); // -X
                Quad(submesh, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)); // +Z
                Quad(submesh, P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0)); // -Z
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
                mesh.UploadMeshData(!KeepReadable);
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
