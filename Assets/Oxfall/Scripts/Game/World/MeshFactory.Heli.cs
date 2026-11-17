using UnityEngine;

namespace Oxfall.Game.World
{
    /// <summary>The patrol helicopter, its rotors and rockets, and the crates it drops: nose towards +Z.</summary>
    public static partial class MeshFactory
    {
        /// <summary>The helicopter's middle (its origin) sits this high over the bottom of its skids.</summary>
        private const float HeliLift = 1.6f;

        /// <summary>Where the main rotor's hub sits on <see cref="PatrolHeli"/>, and the tail rotor's.</summary>
        public static readonly Vector3 HeliRotorHub = new Vector3(0f, 3.5f - HeliLift, 0.1f);
        public static readonly Vector3 HeliTailHub = new Vector3(0.3f, 2.6f - HeliLift, -8.65f);
        /// <summary>The end of its gun's barrel, under the nose.</summary>
        public static readonly Vector3 HeliMuzzle = new Vector3(0f, 0.42f - HeliLift, 4.45f);

        /// <summary>
        /// An armed patrol helicopter about 13 m long: a slab-sided cabin with a glazed nose, a long tail
        /// boom with its fin and stabiliser, an engine housing on the roof with two exhausts, stub wings
        /// with a rocket pod each, a gun turret under the chin and skids. Its middle at the origin.
        /// Submeshes: paint, dark metal, glass, warning yellow, black.
        /// </summary>
        public static Mesh PatrolHeli()
        {
            var b = new FlatMeshBuilder(5) { UvScale = 0.5f };
            var lift = new Vector3(0f, HeliLift, 0f);

            // A tapering section of the body from z0 to z1: half width, bottom and top at each end.
            void Section(int sub, float z0, float w0, float b0, float t0, float z1, float w1, float b1, float t1) =>
                b.AddHex(sub, (x, y, z) =>
                {
                    float w = z == 0 ? w0 : w1, lo = z == 0 ? b0 : b1, hi = z == 0 ? t0 : t1;
                    return new Vector3(x == 0 ? -w : w, y == 0 ? lo : hi, z == 0 ? z0 : z1) - lift;
                });
            void Box(int sub, Vector3 lo, Vector3 hi) => b.AddBox(sub, lo - lift, hi - lift);
            void Tube(int sub, Vector3 a, Vector3 c, float ra, float rc, int sides, int cap = -1) => b.AddTube(sub, a - lift, c - lift, ra, rc, sides, cap);

            // The body, tail to nose.
            Section(0, -8.9f, 0.26f, 1.7f, 2.1f, -3.0f, 0.5f, 1.25f, 2.3f);
            Section(0, -3.0f, 0.5f, 1.25f, 2.3f, -1.2f, 1.12f, 0.62f, 2.5f);
            Section(0, -1.2f, 1.12f, 0.62f, 2.5f, 2.0f, 1.12f, 0.62f, 2.5f);
            // The nose: a painted chin, the canopy over it, a blunt tip.
            Section(0, 2.0f, 1.12f, 0.62f, 1.45f, 3.6f, 0.78f, 0.78f, 1.2f);
            Section(2, 2.0f, 1.08f, 1.45f, 2.45f, 3.6f, 0.74f, 1.2f, 1.9f);
            Section(0, 3.6f, 0.78f, 0.78f, 1.9f, 4.15f, 0.36f, 1.05f, 1.5f);
            // A frame bar down the middle of the canopy.
            Section(1, 2.0f, 0.04f, 2.44f, 2.5f, 3.6f, 0.04f, 1.88f, 1.95f);
            // Side windows and the door seam.
            foreach (float sx in new[] { -1f, 1f })
            {
                float x0 = sx * 1.12f, x1 = sx * 1.135f;
                Box(2, new Vector3(Mathf.Min(x0, x1), 1.5f, 0.25f), new Vector3(Mathf.Max(x0, x1), 2.25f, 1.85f));
                Box(2, new Vector3(Mathf.Min(x0, x1), 1.55f, -1.0f), new Vector3(Mathf.Max(x0, x1), 2.2f, 0.0f));
                Box(1, new Vector3(Mathf.Min(x0, x1) - 0.005f, 0.65f, 0.08f), new Vector3(Mathf.Max(x0, x1) + 0.005f, 2.4f, 0.14f));
            }
            // The engine housing on the roof, its intakes and two exhausts bent out to the sides.
            Section(0, -1.9f, 0.62f, 2.5f, 2.95f, 1.3f, 0.7f, 2.5f, 3.1f);
            foreach (float sx in new[] { -1f, 1f })
            {
                Box(4, new Vector3(sx > 0 ? 0.68f : -0.74f, 2.6f, 0.6f), new Vector3(sx > 0 ? 0.74f : -0.68f, 2.95f, 1.15f));
                Tube(1, new Vector3(sx * 0.45f, 2.75f, -1.85f), new Vector3(sx * 0.85f, 2.85f, -2.5f), 0.17f, 0.15f, 8, 4);
            }
            // The mast up to the hub.
            Tube(1, new Vector3(0f, 3.05f, 0.1f), HeliRotorHub + lift - new Vector3(0f, 0.12f, 0f), 0.16f, 0.12f, 8);
            // The fin, swept back, and the stabiliser across the boom; yellow tips.
            b.AddHex(0, (x, y, z) => new Vector3(x == 0 ? -0.05f : 0.05f, y == 0 ? 1.9f : 3.45f, (z == 0 ? -9.0f : -8.15f) - (y == 1 ? 0.55f : 0f)) - lift);
            b.AddHex(3, (x, y, z) => new Vector3(x == 0 ? -0.055f : 0.055f, y == 0 ? 3.2f : 3.47f, (z == 0 ? -9.47f : -8.62f)) - lift);
            Box(0, new Vector3(-1.35f, 1.78f, -7.7f), new Vector3(1.35f, 1.86f, -7.05f));
            Box(3, new Vector3(-1.4f, 1.775f, -7.7f), new Vector3(-1.2f, 1.865f, -7.05f));
            Box(3, new Vector3(1.2f, 1.775f, -7.7f), new Vector3(1.4f, 1.865f, -7.05f));
            // The tail rotor's gearbox.
            Tube(1, new Vector3(0f, 2.6f, -8.65f), HeliTailHub + lift - new Vector3(0.1f, 0f, 0f), 0.11f, 0.09f, 8, 1);
            // Stub wings, a rocket pod under each: a drum of tubes with dark mouths.
            foreach (float sx in new[] { -1f, 1f })
            {
                b.AddHex(0, (x, y, z) =>
                {
                    // x 0 is always the lower X (so the faces stay outward on both sides); the outer end droops and sweeps back.
                    bool outer = sx > 0 ? x == 1 : x == 0;
                    return new Vector3(sx * (outer ? 2.45f : 1.1f), (y == 0 ? 1.3f : 1.42f) - (outer ? 0.06f : 0f), (z == 0 ? -0.35f : 0.65f) - (outer ? 0.15f : 0f)) - lift;
                });
                Box(1, new Vector3(sx * 2.1f - 0.05f, 1.15f, -0.1f), new Vector3(sx * 2.1f + 0.05f, 1.32f, 0.3f));
                Tube(1, new Vector3(sx * 2.1f, 0.95f, -0.75f), new Vector3(sx * 2.1f, 0.95f, 0.95f), 0.24f, 0.24f, 10, 1);
                Tube(4, new Vector3(sx * 2.1f, 0.95f, 0.95f), new Vector3(sx * 2.1f, 0.95f, 1.0f), 0.2f, 0.2f, 10, 4);
                Tube(1, new Vector3(sx * 2.1f, 0.95f, -0.75f), new Vector3(sx * 2.1f, 0.95f, -1.05f), 0.24f, 0.06f, 10);
            }
            // The chin turret and its gun.
            b.AddCylinder(1, new Vector3(0f, 0.42f, 3.15f) - lift, 0.3f, 0.36f, 0.28f, 10, 0f);
            Tube(1, new Vector3(0f, 0.45f, 3.3f), new Vector3(0f, 0.43f, 3.95f), 0.13f, 0.12f, 8, 1);
            foreach (var o in new[] { new Vector2(0.045f, 0.03f), new Vector2(-0.045f, 0.03f), new Vector2(0f, -0.045f) })
                Tube(4, new Vector3(o.x, 0.43f + o.y, 3.9f), new Vector3(o.x, 0.42f + o.y, 4.45f), 0.03f, 0.03f, 6, 4);
            // Skids on struts.
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * 1.25f;
                Tube(1, new Vector3(x, 0.06f, -1.9f), new Vector3(x, 0.06f, 2.1f), 0.07f, 0.07f, 6);
                Tube(1, new Vector3(x, 0.06f, 2.1f), new Vector3(x, 0.3f, 2.5f), 0.07f, 0.06f, 6);
                Tube(1, new Vector3(x, 0.06f, -1.0f), new Vector3(sx * 0.9f, 0.66f, -0.9f), 0.06f, 0.06f, 6);
                Tube(1, new Vector3(x, 0.06f, 1.3f), new Vector3(sx * 0.9f, 0.66f, 1.2f), 0.06f, 0.06f, 6);
            }
            // A warning stripe round the boom and a beacon under the belly.
            Section(3, -6.3f, 0.355f, 1.555f, 2.155f, -5.9f, 0.385f, 1.505f, 2.175f);
            b.AddCylinder(4, new Vector3(0f, 0.58f, -0.4f) - lift, 0.09f, 0.07f, 0.08f, 8, 0f);
            return b.Build("Patrol helicopter");
        }

        /// <summary>The main rotor: four long blades about a hub at the origin. Submeshes: dark metal, yellow tips.</summary>
        public static Mesh HeliMainRotor()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.6f };
            b.AddCylinder(0, new Vector3(0f, -0.12f, 0f), 0.24f, 0.18f, 0.24f, 10, 0f);
            for (int i = 0; i < 4; i++)
            {
                var turn = Quaternion.Euler(0f, i * 90f, 0f);
                // A slight twist: a hex instead of a box, the leading edge a little higher.
                b.AddHex(0, (x, y, z) => turn * new Vector3((x == 0 ? -0.17f : 0.17f) * (z == 1 ? 0.85f : 1f), (y == 0 ? 0f : 0.05f) + (x == 1 ? 0.025f : 0f), z == 0 ? 0.25f : 6.0f));
                b.AddHex(1, (x, y, z) => turn * new Vector3((x == 0 ? -0.146f : 0.146f), (y == 0 ? -0.001f : 0.051f) + (x == 1 ? 0.025f : 0f), z == 0 ? 6.0f : 6.35f));
            }
            return b.Build("HeliMainRotor");
        }

        /// <summary>The tail rotor: two blades turning about X at the origin. Submeshes: dark metal, yellow tips.</summary>
        public static Mesh HeliTailRotor()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.6f };
            b.AddTube(0, new Vector3(-0.05f, 0f, 0f), new Vector3(0.1f, 0f, 0f), 0.1f, 0.08f, 8, 0);
            foreach (float s in new[] { -1f, 1f })
            {
                b.AddBox(0, new Vector3(0f, Mathf.Min(0f, s * 0.95f), -0.07f), new Vector3(0.04f, Mathf.Max(0f, s * 0.95f), 0.07f));
                b.AddBox(1, new Vector3(-0.001f, Mathf.Min(s * 0.95f, s * 1.15f), -0.071f), new Vector3(0.041f, Mathf.Max(s * 0.95f, s * 1.15f), 0.071f));
            }
            return b.Build("HeliTailRotor");
        }

        /// <summary>A rocket, 0.9 m, nose towards +Z, with four fins. Submeshes: body, warhead.</summary>
        public static Mesh HeliRocket()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.4f };
            b.AddTube(0, new Vector3(0f, 0f, -0.45f), new Vector3(0f, 0f, 0.3f), 0.055f, 0.055f, 8, 0);
            b.AddTube(1, new Vector3(0f, 0f, 0.3f), new Vector3(0f, 0f, 0.48f), 0.055f, 0.005f, 8);
            for (int i = 0; i < 4; i++)
            {
                var turn = Quaternion.Euler(0f, 0f, i * 90f + 45f);
                b.AddHex(0, (x, y, z) => turn * new Vector3(x == 0 ? -0.006f : 0.006f, y == 0 ? 0.05f : 0.14f, z == 0 ? -0.45f : -0.3f + (y == 1 ? -0.08f : 0f)));
            }
            return b.Build("HeliRocket");
        }

        /// <summary>
        /// A crate thrown from a downed helicopter: an olive military case on skids with steel bands,
        /// a stencilled yellow band and latches, scorched along its foot. Submeshes: paint, steel, yellow, soot.
        /// </summary>
        public static Mesh HeliCrate()
        {
            var b = new FlatMeshBuilder(4) { UvScale = 0.7f };
            const float hx = 0.7f, hz = 0.42f, top = 0.62f;
            foreach (float x in new[] { -0.5f, 0.5f })
                b.AddBox(1, new Vector3(x - 0.07f, 0f, -hz + 0.02f), new Vector3(x + 0.07f, 0.07f, hz - 0.02f));
            b.AddBox(3, new Vector3(-hx, 0.07f, -hz), new Vector3(hx, 0.2f, hz));
            b.AddBox(0, new Vector3(-hx, 0.2f, -hz), new Vector3(hx, top - 0.08f, hz));
            b.AddBox(1, new Vector3(-hx - 0.01f, top - 0.11f, -hz - 0.01f), new Vector3(hx + 0.01f, top - 0.08f, hz + 0.01f));
            b.AddBox(0, new Vector3(-hx - 0.015f, top - 0.08f, -hz - 0.015f), new Vector3(hx + 0.015f, top, hz + 0.015f));
            foreach (float x in new[] { -0.42f, 0.42f })
                b.AddBox(1, new Vector3(x - 0.03f, 0.07f, -hz - 0.02f), new Vector3(x + 0.03f, top + 0.005f, hz + 0.02f));
            // The yellow band across the lid and down the front, the latches over it.
            b.AddBox(2, new Vector3(-0.1f, top, -hz - 0.016f), new Vector3(0.1f, top + 0.004f, hz + 0.016f));
            b.AddBox(2, new Vector3(-0.1f, 0.25f, hz), new Vector3(0.1f, top - 0.12f, hz + 0.012f));
            foreach (float x in new[] { -0.25f, 0.25f })
                b.AddBox(1, new Vector3(x - 0.05f, top - 0.18f, hz + 0.01f), new Vector3(x + 0.05f, top - 0.04f, hz + 0.05f));
            foreach (float sx in new[] { -1f, 1f })
                b.AddTube(1, new Vector3(sx * (hx + 0.03f), 0.42f, -0.15f), new Vector3(sx * (hx + 0.03f), 0.42f, 0.15f), 0.022f, 0.022f, 6);
            return b.Build("Heli crate");
        }
    }
}
