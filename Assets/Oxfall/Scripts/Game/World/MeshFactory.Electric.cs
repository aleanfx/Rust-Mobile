using Oxfall.Core.Rules;
using UnityEngine;

namespace Oxfall.Game.World
{
    /// <summary>The electric things: generator, solar panel, battery, switch and lamp, all facing +Z.</summary>
    public static partial class MeshFactory
    {
        /// <summary>
        /// A small generator: an engine in a tube cage, a fuel tank on top, an exhaust and a front panel
        /// with its socket and lamp. Submeshes: paint, steel, rubber, lamp.
        /// </summary>
        public static Mesh Generator()
        {
            var b = new FlatMeshBuilder(4) { UvScale = 0.6f };
            // The cage: two bent tubes each side, joined front and back.
            foreach (float x in new[] { -0.36f, 0.36f })
            {
                b.AddTube(1, new Vector3(x, 0.03f, -0.24f), new Vector3(x, 0.03f, 0.24f), 0.022f, 0.022f, 6);
                b.AddTube(1, new Vector3(x, 0.03f, -0.24f), new Vector3(x, 0.52f, -0.24f), 0.022f, 0.022f, 6);
                b.AddTube(1, new Vector3(x, 0.03f, 0.24f), new Vector3(x, 0.52f, 0.24f), 0.022f, 0.022f, 6);
                b.AddTube(1, new Vector3(x, 0.52f, -0.24f), new Vector3(x, 0.52f, 0.24f), 0.022f, 0.022f, 6);
            }
            b.AddTube(1, new Vector3(-0.36f, 0.52f, 0.24f), new Vector3(0.36f, 0.52f, 0.24f), 0.022f, 0.022f, 6);
            b.AddTube(1, new Vector3(-0.36f, 0.52f, -0.24f), new Vector3(0.36f, 0.52f, -0.24f), 0.022f, 0.022f, 6);
            // Rubber feet.
            foreach (float x in new[] { -0.3f, 0.3f })
            foreach (float z in new[] { -0.2f, 0.2f })
                b.AddBox(2, new Vector3(x - 0.04f, 0f, z - 0.04f), new Vector3(x + 0.04f, 0.03f, z + 0.04f));
            // The engine block, its cylinder fins and a pull-start drum.
            b.AddBox(0, new Vector3(-0.28f, 0.05f, -0.18f), new Vector3(0.12f, 0.36f, 0.18f));
            for (int i = 0; i < 4; i++)
            {
                float y = 0.14f + i * 0.05f;
                b.AddBox(1, new Vector3(0.12f, y, -0.12f), new Vector3(0.24f, y + 0.02f, 0.12f));
            }
            b.AddTube(1, new Vector3(-0.28f, 0.22f, 0f), new Vector3(-0.33f, 0.22f, 0f), 0.09f, 0.09f, 10, 1);
            // The tank, rounded at its top edge, and its cap.
            b.AddBox(0, new Vector3(-0.3f, 0.37f, -0.2f), new Vector3(0.3f, 0.47f, 0.2f));
            b.AddBox(0, new Vector3(-0.27f, 0.47f, -0.17f), new Vector3(0.27f, 0.5f, 0.17f));
            b.AddTube(2, new Vector3(0.14f, 0.5f, -0.06f), new Vector3(0.14f, 0.54f, -0.06f), 0.035f, 0.035f, 8, 2);
            // The exhaust out of the back.
            b.AddTube(1, new Vector3(0.18f, 0.25f, -0.18f), new Vector3(0.18f, 0.25f, -0.3f), 0.03f, 0.03f, 6, 2);
            // The front panel: socket and lamp.
            b.AddBox(1, new Vector3(0.12f, 0.08f, 0.18f), new Vector3(0.34f, 0.34f, 0.2f));
            b.AddBox(2, new Vector3(0.2f, 0.16f, 0.2f), new Vector3(0.28f, 0.24f, 0.215f));
            b.AddBox(3, new Vector3(0.16f, 0.28f, 0.2f), new Vector3(0.2f, 0.31f, 0.215f));
            return b.Build("Generator");
        }

        /// <summary>
        /// A solar panel: a dark glass sheet in a steel frame, tilted back to the sky on a post and two
        /// struts. Submeshes: steel, glass, cell lines.
        /// </summary>
        public static Mesh SolarPanel()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.6f };
            // A cross of feet, the post and its braces.
            b.AddBox(0, new Vector3(-0.45f, 0f, -0.05f), new Vector3(0.45f, 0.05f, 0.05f));
            b.AddBox(0, new Vector3(-0.05f, 0f, -0.4f), new Vector3(0.05f, 0.05f, 0.4f));
            b.AddTube(0, new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0.85f, 0f), 0.04f, 0.04f, 8);
            b.AddTube(0, new Vector3(-0.35f, 0.05f, 0f), new Vector3(0f, 0.6f, 0f), 0.018f, 0.018f, 5);
            b.AddTube(0, new Vector3(0.35f, 0.05f, 0f), new Vector3(0f, 0.6f, 0f), 0.018f, 0.018f, 5);
            // The panel, its face turned 35 degrees from the sky towards the front, about its hinge on the post.
            var tilt = Quaternion.Euler(35f, 0f, 0f);
            var hinge = new Vector3(0f, 0.88f, 0f);
            Vector3 P(float x, float y, float z) => hinge + tilt * new Vector3(x, y, z);
            void Slab(int sub, float x0, float x1, float z0, float z1, float y0, float y1)
            {
                // A box in the panel's frame (x across, z up the slope, y out of the face).
                var c = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    c[i] = P((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1);
                Vector3 centre = Vector3.zero;
                foreach (var v in c) centre += v;
                centre /= 8f;
                int[][] faces =
                {
                    new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 },
                };
                foreach (var f in faces)
                {
                    var mid = (c[f[0]] + c[f[1]] + c[f[2]] + c[f[3]]) * 0.25f;
                    var outward = mid - centre;
                    b.Facing(sub, c[f[0]], c[f[1]], c[f[2]], outward);
                    b.Facing(sub, c[f[0]], c[f[2]], c[f[3]], outward);
                }
            }
            Slab(0, -0.62f, 0.62f, -0.42f, 0.42f, -0.03f, 0f);
            Slab(1, -0.58f, 0.58f, -0.38f, 0.38f, 0f, 0.012f);
            // Cell lines across and along.
            for (int i = 1; i < 6; i++)
            {
                float x = -0.58f + i * (1.16f / 6f);
                Slab(2, x - 0.006f, x + 0.006f, -0.38f, 0.38f, 0.012f, 0.016f);
            }
            for (int j = 1; j < 4; j++)
            {
                float z = -0.38f + j * (0.76f / 4f);
                Slab(2, -0.58f, 0.58f, z - 0.006f, z + 0.006f, 0.012f, 0.016f);
            }
            // The junction box under it.
            b.AddBox(0, new Vector3(-0.07f, 0.68f, -0.12f), new Vector3(0.07f, 0.8f, -0.04f));
            return b.Build("Solar panel");
        }

        /// <summary>
        /// A battery: a squat dark case with a yellow band, two terminals, and three charge lamps on
        /// its front. Submeshes: case, steel, band, lamp 1, lamp 2, lamp 3.
        /// </summary>
        public static Mesh Battery()
        {
            var b = new FlatMeshBuilder(6) { UvScale = 0.6f };
            b.AddBox(0, new Vector3(-0.24f, 0f, -0.17f), new Vector3(0.24f, 0.48f, 0.17f));
            b.AddBox(0, new Vector3(-0.22f, 0.48f, -0.15f), new Vector3(0.22f, 0.51f, 0.15f));
            b.AddBox(2, new Vector3(-0.245f, 0.34f, -0.175f), new Vector3(0.245f, 0.4f, 0.175f));
            // Carry handle.
            b.AddBox(1, new Vector3(-0.1f, 0.51f, -0.02f), new Vector3(-0.08f, 0.58f, 0.02f));
            b.AddBox(1, new Vector3(0.08f, 0.51f, -0.02f), new Vector3(0.1f, 0.58f, 0.02f));
            b.AddBox(1, new Vector3(-0.1f, 0.58f, -0.02f), new Vector3(0.1f, 0.6f, 0.02f));
            // Terminals: in on the left, out on the right.
            b.AddTube(1, new Vector3(-0.16f, 0.51f, 0.06f), new Vector3(-0.16f, 0.56f, 0.06f), 0.025f, 0.025f, 8, 1);
            b.AddTube(1, new Vector3(0.16f, 0.51f, 0.06f), new Vector3(0.16f, 0.56f, 0.06f), 0.025f, 0.025f, 8, 1);
            for (int i = 0; i < 3; i++)
            {
                float x = -0.12f + i * 0.09f;
                b.AddBox(3 + i, new Vector3(x, 0.2f, 0.17f), new Vector3(x + 0.06f, 0.26f, 0.18f));
            }
            return b.Build("Battery");
        }

        /// <summary>A switch: a post with a steel box on top. Submeshes: steel, paint. (The lever is <see cref="SwitchLever"/>.)</summary>
        public static Mesh ElectricSwitch()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.6f };
            b.AddBox(0, new Vector3(-0.12f, 0f, -0.12f), new Vector3(0.12f, 0.03f, 0.12f));
            b.AddTube(0, new Vector3(0f, 0.03f, 0f), new Vector3(0f, 0.78f, 0f), 0.03f, 0.03f, 8);
            b.AddBox(1, new Vector3(-0.1f, 0.78f, -0.06f), new Vector3(0.1f, 1.02f, 0.06f));
            b.AddBox(0, new Vector3(-0.07f, 0.84f, 0.06f), new Vector3(0.07f, 0.96f, 0.07f));
            return b.Build("Switch");
        }

        /// <summary>Where the switch's lever turns, on its box's face.</summary>
        public static readonly Vector3 SwitchPivot = new Vector3(0f, 0.9f, 0.07f);

        /// <summary>The switch's lever, turning about its root (up is on). Submeshes: steel, red grip.</summary>
        public static Mesh SwitchLever()
        {
            var b = new FlatMeshBuilder(2) { UvScale = 0.6f };
            b.AddTube(0, Vector3.zero, new Vector3(0f, 0.11f, 0.04f), 0.012f, 0.012f, 6);
            b.AddTube(1, new Vector3(0f, 0.1f, 0.036f), new Vector3(0f, 0.15f, 0.055f), 0.02f, 0.02f, 8, 1);
            return b.Build("Switch lever");
        }

        /// <summary>A whole switch (lever up), for its icon and ghost. Submeshes: steel, paint, grip.</summary>
        public static Mesh ElectricSwitchWhole()
        {
            var post = ElectricSwitch();
            var lever = SwitchLever();
            return Merge("SwitchWhole", 3,
                (post, 0, 0, Matrix4x4.identity), (post, 1, 1, Matrix4x4.identity),
                (lever, 0, 0, Matrix4x4.Translate(SwitchPivot)), (lever, 1, 2, Matrix4x4.Translate(SwitchPivot)));
        }

        /// <summary>
        /// A standing lamp: a heavy foot, a post, an arm and a caged shade over the bulb. Submeshes:
        /// steel, shade paint, bulb.
        /// </summary>
        public static Mesh Lamp()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.6f };
            b.AddTube(0, Vector3.zero, new Vector3(0f, 0.05f, 0f), 0.16f, 0.14f, 10, 0);
            b.AddTube(0, new Vector3(0f, 0.05f, 0f), new Vector3(0f, 1.62f, 0f), 0.025f, 0.02f, 8);
            b.AddTube(0, new Vector3(0f, 1.6f, 0f), new Vector3(0f, 1.66f, 0.2f), 0.018f, 0.018f, 6);
            // The shade: a cone opening down, then the bulb under it.
            b.AddTube(1, new Vector3(0f, 1.67f, 0.22f), new Vector3(0f, 1.55f, 0.22f), 0.04f, 0.15f, 10);
            b.AddTube(1, new Vector3(0f, 1.67f, 0.22f), new Vector3(0f, 1.68f, 0.22f), 0.04f, 0.04f, 10, 1);
            b.AddTube(2, new Vector3(0f, 1.6f, 0.22f), new Vector3(0f, 1.52f, 0.22f), 0.045f, 0.035f, 8, 2);
            // A wire cage round the bulb.
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                var top = new Vector3(Mathf.Sin(a) * 0.13f, 1.56f, 0.22f + Mathf.Cos(a) * 0.13f);
                b.AddTube(0, top, new Vector3(0f, 1.47f, 0.22f), 0.006f, 0.006f, 4);
            }
            return b.Build("Lamp");
        }

        /// <summary>
        /// A presence sensor: a post, a small box and a faceted white eye looking out the front, with
        /// a lamp that lights while it sees someone. Submeshes: steel, paint, lens, lamp.
        /// </summary>
        public static Mesh MotionSensor()
        {
            var b = new FlatMeshBuilder(4) { UvScale = 0.6f };
            b.AddBox(0, new Vector3(-0.11f, 0f, -0.11f), new Vector3(0.11f, 0.03f, 0.11f));
            b.AddTube(0, new Vector3(0f, 0.03f, 0f), new Vector3(0f, 1.05f, 0f), 0.025f, 0.025f, 8);
            b.AddBox(1, new Vector3(-0.08f, 1.05f, -0.06f), new Vector3(0.08f, 1.21f, 0.06f));
            // The eye: a faceted dome, and the lamp under it.
            b.AddTube(2, new Vector3(0f, 1.13f, 0.06f), new Vector3(0f, 1.13f, 0.1f), 0.055f, 0.045f, 10, 0);
            b.AddTube(2, new Vector3(0f, 1.13f, 0.1f), new Vector3(0f, 1.13f, 0.12f), 0.045f, 0.012f, 10, 2);
            b.AddBox(3, new Vector3(0.035f, 1.07f, 0.06f), new Vector3(0.06f, 1.085f, 0.07f));
            return b.Build("Motion sensor");
        }

        /// <summary>
        /// A siren light: a heavy foot, a short post and a round housing under an orange dome. The
        /// beams that turn inside it are lights of the view. Submeshes: steel, paint, dome.
        /// </summary>
        public static Mesh SirenLight()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.6f };
            b.AddTube(0, Vector3.zero, new Vector3(0f, 0.05f, 0f), 0.16f, 0.14f, 10, 0);
            b.AddTube(0, new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0.95f, 0f), 0.025f, 0.025f, 8);
            b.AddTube(1, new Vector3(0f, 0.95f, 0f), new Vector3(0f, 1.03f, 0f), 0.1f, 0.1f, 12, 1);
            b.AddTube(2, new Vector3(0f, 1.03f, 0f), new Vector3(0f, 1.17f, 0f), 0.085f, 0.08f, 12);
            b.AddTube(2, new Vector3(0f, 1.17f, 0f), new Vector3(0f, 1.22f, 0f), 0.08f, 0.03f, 12, 2);
            // The horn under the housing.
            b.AddTube(1, new Vector3(0f, 0.86f, 0.03f), new Vector3(0f, 0.86f, 0.13f), 0.025f, 0.06f, 10, 0);
            return b.Build("Siren light");
        }

        /// <summary>Where the siren's light turns.</summary>
        public static readonly Vector3 SirenHead = new Vector3(0f, 1.1f, 0f);

        /// <summary>
        /// A door controller: a grey box on a short post with a lamp that shows when it holds the door
        /// open. Submeshes: steel, paint, lamp.
        /// </summary>
        public static Mesh DoorController()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.6f };
            b.AddBox(0, new Vector3(-0.12f, 0f, -0.1f), new Vector3(0.12f, 0.03f, 0.1f));
            b.AddTube(0, new Vector3(0f, 0.03f, 0f), new Vector3(0f, 0.8f, 0f), 0.03f, 0.03f, 8);
            b.AddBox(1, new Vector3(-0.11f, 0.8f, -0.07f), new Vector3(0.11f, 1.08f, 0.07f));
            // A cable gland on top, two screws and the lamp on the face.
            b.AddTube(0, new Vector3(0f, 1.08f, 0f), new Vector3(0f, 1.13f, 0f), 0.02f, 0.02f, 8, 1);
            b.AddBox(0, new Vector3(-0.09f, 1.04f, 0.07f), new Vector3(-0.07f, 1.06f, 0.075f));
            b.AddBox(0, new Vector3(0.07f, 0.82f, 0.07f), new Vector3(0.09f, 0.84f, 0.075f));
            b.AddBox(2, new Vector3(-0.03f, 0.95f, 0.07f), new Vector3(0.03f, 0.99f, 0.08f));
            return b.Build("Door controller");
        }

        /// <summary>Where the bulb is, for its light.</summary>
        public static readonly Vector3 LampBulb = new Vector3(0f, 1.52f, 0.22f);

        /// <summary>
        /// The cable in hand: a reel wound with black cable, a loose end ending in a plug. Submeshes:
        /// cable, reel paint, plug steel.
        /// </summary>
        public static Mesh WireTool()
        {
            var b = new FlatMeshBuilder(3) { UvScale = 0.6f };
            // The reel: two cheeks and a hub, its axis along X.
            var side = Quaternion.Euler(0f, 0f, 90f);
            b.AddTube(1, new Vector3(-0.09f, 0f, 0f), new Vector3(-0.08f, 0f, 0f), 0.13f, 0.13f, 14, 1);
            b.AddTube(1, new Vector3(0.08f, 0f, 0f), new Vector3(0.09f, 0f, 0f), 0.13f, 0.13f, 14, 1);
            // Turns of cable, wound side by side.
            for (int i = 0; i < 6; i++)
            {
                float x = -0.07f + i * 0.028f;
                b.AddTube(0, new Vector3(x, 0f, 0f), new Vector3(x + 0.026f, 0f, 0f), 0.095f + (i % 2) * 0.004f, 0.095f + (i % 2) * 0.004f, 12);
            }
            // A handle through the hub.
            b.AddTube(2, new Vector3(-0.15f, 0f, 0f), new Vector3(0.15f, 0f, 0f), 0.015f, 0.015f, 6, 2);
            // The loose end hanging off the front, and its plug.
            b.AddTube(0, new Vector3(0.02f, -0.09f, 0.03f), new Vector3(0.03f, -0.16f, 0.14f), 0.012f, 0.012f, 6);
            b.AddTube(0, new Vector3(0.03f, -0.16f, 0.14f), new Vector3(0.03f, -0.13f, 0.24f), 0.012f, 0.012f, 6);
            b.AddBox(2, new Vector3(0.0f, -0.15f, 0.24f), new Vector3(0.06f, -0.11f, 0.3f));
            b.AddBox(2, new Vector3(0.012f, -0.135f, 0.3f), new Vector3(0.018f, -0.125f, 0.33f));
            b.AddBox(2, new Vector3(0.042f, -0.135f, 0.3f), new Vector3(0.048f, -0.125f, 0.33f));
            return b.Build("Wire tool");
        }

        /// <summary>Where a cable comes into a device (local, before its yaw).</summary>
        public static Vector3 PowerIn(DeployableKind kind)
        {
            switch (kind)
            {
                case DeployableKind.Battery: return new Vector3(-0.16f, 0.56f, 0.06f);
                case DeployableKind.ElectricSwitch: return new Vector3(-0.1f, 0.84f, 0f);
                case DeployableKind.Lamp: return new Vector3(0f, 0.06f, -0.12f);
                case DeployableKind.AutoTurret: return new Vector3(0f, 0.5f, -0.2f);
                case DeployableKind.MotionSensor: return new Vector3(-0.08f, 1.1f, 0f);
                case DeployableKind.SirenLight: return new Vector3(0f, 0.06f, -0.12f);
                case DeployableKind.DoorController: return new Vector3(0f, 1.13f, 0f);
                default: return new Vector3(0f, 0.3f, 0f);
            }
        }

        /// <summary>Where a cable leaves a device (local, before its yaw).</summary>
        public static Vector3 PowerOut(DeployableKind kind)
        {
            switch (kind)
            {
                case DeployableKind.Generator: return new Vector3(0.24f, 0.2f, 0.215f);
                case DeployableKind.SolarPanel: return new Vector3(0f, 0.7f, -0.12f);
                case DeployableKind.Battery: return new Vector3(0.16f, 0.56f, 0.06f);
                case DeployableKind.ElectricSwitch: return new Vector3(0.1f, 0.84f, 0f);
                case DeployableKind.MotionSensor: return new Vector3(0.08f, 1.1f, 0f);
                default: return new Vector3(0f, 0.3f, 0f);
            }
        }

        /// <summary>Several submeshes of several meshes, placed, into one mesh of <paramref name="submeshes"/>.</summary>
        private static Mesh Merge(string name, int submeshes, params (Mesh Mesh, int From, int To, Matrix4x4 At)[] parts)
        {
            var merged = new Mesh { name = name };
            var lists = new System.Collections.Generic.List<CombineInstance>[submeshes];
            for (int i = 0; i < submeshes; i++) lists[i] = new System.Collections.Generic.List<CombineInstance>();
            foreach (var p in parts) lists[p.To].Add(new CombineInstance { mesh = p.Mesh, subMeshIndex = p.From, transform = p.At });
            var all = new CombineInstance[submeshes];
            for (int i = 0; i < submeshes; i++)
            {
                var part = new Mesh();
                part.CombineMeshes(lists[i].ToArray(), true, true);
                all[i] = new CombineInstance { mesh = part, subMeshIndex = 0, transform = Matrix4x4.identity };
            }
            merged.CombineMeshes(all, false, false);
            merged.RecalculateBounds();
            return merged;
        }
    }
}
