using System.Collections.Generic;
using Oxfall.Core.World;
using UnityEngine;

namespace Oxfall.Game.World
{
    public static partial class MeshFactory
    {
        // The submeshes of a monument's dressing, in the order of WorldBuilder's materials for it.
        public const int DressConcrete = 0, DressSteel = 1, DressRed = 2, DressWhite = 3, DressGlass = 4, DressRubber = 5,
            DressSandbag = 6, DressYellow = 7, DressRust = 8, DressOlive = 9, DressStain = 10;
        public const int DressSubmeshes = 11;

        /// <summary>
        /// The details that make a monument's bare blocks read as a place (see <see cref="Monuments.Props"/>
        /// for the blocks): the gas station's fuel pumps, canopy trim, price sign, shop windows, a car
        /// wreck's wheels and glass, lamp posts, parking lines; the outpost's barbed wire, sandbags,
        /// the watchtower's bracing, railing, ladder and roof, a radio mast, crates, barriers. In the
        /// monument's own frame (its front along +Z, its pad's top at y = 0.15). Nothing here is
        /// walked on; <paramref name="solids"/> gets the boxes of the few things big enough to bump
        /// into, in the same frame.
        /// </summary>
        public static Mesh MonumentDressing(MonumentKind kind, List<Bounds> solids)
        {
            var b = new FlatMeshBuilder(DressSubmeshes) { UvScale = 1.2f };
            var random = new System.Random(kind == MonumentKind.Outpost ? 4107 : 7303);
            if (kind == MonumentKind.GasStation) DressGasStation(b, random, solids);
            else DressOutpost(b, random, solids);
            return b.Build(kind + " dressing", 40f);
        }

        private const float Pad = 0.15f;

        private static void Box(FlatMeshBuilder b, int sub, float x0, float y0, float z0, float x1, float y1, float z1) =>
            b.AddBox(sub, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));

        /// <summary>A box turned by <paramref name="turn"/> about its middle.</summary>
        private static void TurnedBox(FlatMeshBuilder b, int sub, Vector3 centre, Vector3 size, Quaternion turn)
        {
            var h = size * 0.5f;
            b.AddHex(sub, (x, y, z) => centre + turn * new Vector3(x == 0 ? -h.x : h.x, y == 0 ? -h.y : h.y, z == 0 ? -h.z : h.z));
        }

        /// <summary>A flat round patch on the pad (an oil stain, a puddle's mark).</summary>
        private static void Stain(FlatMeshBuilder b, float x, float z, float radius, System.Random random) =>
            b.AddCylinder(DressStain, new Vector3(x, Pad, z), radius, radius * 0.92f, 0.006f, 9, Rand(random, 0f, 40f));

        private static void Solid(List<Bounds> solids, float x0, float y0, float z0, float x1, float y1, float z1) =>
            solids?.Add(new Bounds(new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, y1 - y0, z1 - z0)));

        private static void DressGasStation(FlatMeshBuilder b, System.Random random, List<Bounds> solids)
        {
            // The canopy: a white band round its red edge, light panels under it, and its pillars set
            // in concrete collars striped yellow where cars would scrape them.
            Box(b, DressWhite, -5.03f, 4.43f, 0.72f, 5.03f, 4.57f, 8.28f);
            foreach (float x in new[] { -2.6f, 2.6f })
            foreach (float z in new[] { 2.7f, 6.3f })
            {
                Box(b, DressWhite, x - 0.7f, 4.27f, z - 0.28f, x + 0.7f, 4.3f, z + 0.28f);
                Box(b, DressSteel, x - 0.76f, 4.28f, z - 0.34f, x + 0.76f, 4.3f, z + 0.34f);
            }
            foreach (float x in new[] { -3.5f, 3.5f })
            foreach (float z in new[] { 2f, 7f })
            {
                Box(b, DressConcrete, x - 0.32f, Pad, z - 0.32f, x + 0.32f, 0.5f, z + 0.32f);
                Box(b, DressYellow, x - 0.19f, 0.5f, z - 0.19f, x + 0.19f, 1.05f, z + 0.19f);
                Box(b, DressRubber, x - 0.195f, 0.72f, z - 0.195f, x + 0.195f, 0.83f, z + 0.195f);
            }

            // The pump island, and on each pump its white housing, a display on either face, a
            // header and a nozzle hanging on its hose at each side.
            Box(b, DressConcrete, -3.0f, Pad, 3.95f, 3.0f, 0.27f, 5.05f);
            Box(b, DressYellow, -3.12f, Pad, 4.1f, -3.0f, 0.271f, 4.9f);
            Box(b, DressYellow, 3.0f, Pad, 4.1f, 3.12f, 0.271f, 4.9f);
            foreach (float px in new[] { -1.8f, 1.8f })
            {
                Box(b, DressSteel, px - 0.42f, 0.27f, 4.18f, px + 0.42f, 0.36f, 4.82f);
                Box(b, DressWhite, px - 0.37f, 0.95f, 4.23f, px + 0.37f, 1.5f, 4.77f);
                Box(b, DressGlass, px - 0.22f, 1.1f, 4.765f, px + 0.22f, 1.38f, 4.785f);
                Box(b, DressGlass, px - 0.22f, 1.1f, 4.215f, px + 0.22f, 1.38f, 4.235f);
                Box(b, DressWhite, px - 0.42f, 1.65f, 4.2f, px + 0.42f, 1.82f, 4.8f);
                Box(b, DressRed, px - 0.43f, 1.73f, 4.19f, px + 0.43f, 1.79f, 4.81f);
                foreach (float side in new[] { -1f, 1f })
                {
                    float sx = px + side * 0.35f;
                    Box(b, DressSteel, Mathf.Min(sx, sx + side * 0.09f), 0.86f, 4.42f, Mathf.Max(sx, sx + side * 0.09f), 1.0f, 4.58f);
                    var top = new Vector3(px + side * 0.36f, 1.28f, 4.5f);
                    var low = new Vector3(px + side * 0.62f, 0.55f, 4.62f);
                    var holster = new Vector3(px + side * 0.45f, 0.93f, 4.5f);
                    b.AddTube(DressRubber, top, low, 0.03f, 0.03f, 5);
                    b.AddTube(DressRubber, low, holster, 0.03f, 0.03f, 5);
                    b.AddTube(DressSteel, holster, holster + new Vector3(side * 0.06f, 0.16f, 0f), 0.028f, 0.02f, 5);
                }
                Stain(b, px + 0.9f, 3.2f, 0.55f, random);
            }
            Stain(b, -0.4f, 6.4f, 0.8f, random);
            Stain(b, 2.6f, 1.2f, 0.45f, random);

            // The price sign on its posts by the road: white, a red head, three dark price rows.
            Box(b, DressConcrete, 8.55f, Pad, 8.3f, 10.65f, 0.45f, 8.9f);
            foreach (float x in new[] { 9.0f, 10.2f })
            {
                Box(b, DressSteel, x - 0.1f, 0.45f, 8.5f, x + 0.1f, 5.1f, 8.7f);
                Solid(solids, x - 0.1f, Pad, 8.5f, x + 0.1f, 5.1f, 8.7f);
            }
            Box(b, DressWhite, 8.7f, 5.0f, 8.45f, 10.5f, 7.4f, 8.75f);
            Box(b, DressRed, 8.68f, 6.8f, 8.43f, 10.52f, 7.42f, 8.77f);
            Box(b, DressYellow, 8.69f, 6.88f, 8.425f, 10.51f, 6.94f, 8.775f);
            for (int i = 0; i < 3; i++)
            {
                float y = 5.2f + i * 0.52f;
                Box(b, DressGlass, 8.9f, y, 8.43f, 10.3f, y + 0.36f, 8.77f);
                Box(b, DressYellow, 9.85f, y + 0.08f, 8.42f, 10.2f, y + 0.28f, 8.78f);
            }

            // The shop: windows (glass both sides of the wall, steel frames), a door frame, a red and
            // white fascia, a parapet round the roof, an air conditioner and a vent on it.
            foreach (var (x0, x1) in new[] { (0.1f, 2.1f), (2.8f, 4.7f) })
            {
                Box(b, DressGlass, x0, 0.95f, -3.345f, x1, 2.35f, -3.325f);
                Box(b, DressGlass, x0, 0.95f, -3.675f, x1, 2.35f, -3.655f);
                Box(b, DressSteel, x0 - 0.07f, 0.87f, -3.37f, x1 + 0.07f, 0.95f, -3.29f);
                Box(b, DressSteel, x0 - 0.07f, 2.35f, -3.37f, x1 + 0.07f, 2.42f, -3.31f);
                Box(b, DressSteel, x0 - 0.07f, 0.95f, -3.37f, x0, 2.35f, -3.31f);
                Box(b, DressSteel, x1, 0.95f, -3.37f, x1 + 0.07f, 2.35f, -3.31f);
                float mid = (x0 + x1) * 0.5f;
                Box(b, DressSteel, mid - 0.03f, 0.95f, -3.36f, mid + 0.03f, 2.35f, -3.315f);
            }
            Box(b, DressSteel, -2.84f, Pad, -3.69f, -2.74f, 2.35f, -3.31f);
            Box(b, DressSteel, -0.86f, Pad, -3.69f, -0.76f, 2.35f, -3.31f);
            Box(b, DressSteel, -2.84f, 2.25f, -3.69f, -0.76f, 2.35f, -3.31f);
            Box(b, DressRed, -5.25f, 2.62f, -3.34f, 5.25f, 3.18f, -3.2f);
            Box(b, DressWhite, -5.26f, 2.84f, -3.345f, 5.26f, 2.94f, -3.195f);
            Box(b, DressConcrete, -5.2f, 3.45f, -8.7f, 5.2f, 3.75f, -8.5f);
            Box(b, DressConcrete, -5.2f, 3.45f, -3.5f, 5.2f, 3.75f, -3.3f);
            Box(b, DressConcrete, -5.2f, 3.45f, -8.5f, -5.0f, 3.75f, -3.5f);
            Box(b, DressConcrete, 5.0f, 3.45f, -8.5f, 5.2f, 3.75f, -3.5f);
            Box(b, DressSteel, 1.4f, 3.45f, -7.6f, 2.8f, 4.15f, -6.5f);
            b.AddCylinder(DressRubber, new Vector3(2.1f, 4.15f, -7.05f), 0.4f, 0.4f, 0.025f, 12, 0f);
            b.AddCylinder(DressSteel, new Vector3(-3.0f, 3.45f, -5.2f), 0.12f, 0.12f, 0.75f, 8, 0f);
            b.AddCylinder(DressSteel, new Vector3(-3.0f, 4.2f, -5.2f), 0.2f, 0.05f, 0.12f, 8, 0f);
            Stain(b, -1.8f, -2.6f, 0.5f, random);

            // The wreck out front: flat tyres, rims, glass, bumpers, rust coming through.
            var turn = Quaternion.Euler(0f, 12f, 0f);
            var body = new Vector3(-7.5f, 0.7f, 3.5f);
            var cab = new Vector3(-7.6f, 1.5f, 3.3f);
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var hub = body + turn * new Vector3(sx * 0.9f, -0.25f, sz * 1.35f);
                var axis = turn * Vector3.right * sx;
                b.AddTube(DressRubber, hub - axis * 0.1f, hub + axis * 0.17f, 0.34f, 0.34f, 10, DressRubber);
                b.AddTube(DressSteel, hub + axis * 0.16f, hub + axis * 0.18f, 0.2f, 0.2f, 8, DressSteel);
            }
            foreach (float sx in new[] { -1f, 1f })
            {
                TurnedBox(b, DressGlass, cab + turn * new Vector3(sx * 0.805f, 0.02f, 0f), new Vector3(0.02f, 0.38f, 1.8f), turn);
                TurnedBox(b, DressRust, body + turn * new Vector3(sx * 0.955f, 0.05f, sx * 0.6f), new Vector3(0.02f, 0.45f, 1.1f), turn);
                TurnedBox(b, DressWhite, body + turn * new Vector3(sx * 0.6f, 0.25f, 2.105f), new Vector3(0.32f, 0.14f, 0.02f), turn);
            }
            foreach (float sz in new[] { -1f, 1f })
            {
                TurnedBox(b, DressGlass, cab + turn * new Vector3(0f, 0.02f, sz * 1.105f), new Vector3(1.4f, 0.38f, 0.02f), turn);
                TurnedBox(b, DressSteel, body + turn * new Vector3(0f, -0.3f, sz * 2.14f), new Vector3(1.96f, 0.16f, 0.1f), turn);
            }
            TurnedBox(b, DressRust, body + turn * new Vector3(0.1f, 0.555f, 1.4f), new Vector3(1.2f, 0.02f, 0.9f), turn);
            Stain(b, -7.3f, 3.4f, 1.3f, random);

            // Tyres stacked by the car, a dumpster behind the shop, lamp posts at two corners.
            for (int i = 0; i < 3; i++)
            {
                var at = new Vector3(-10.8f + (i == 2 ? 0.08f : 0f), Pad + i * 0.24f, 0.4f + (i == 1 ? -0.05f : 0f));
                b.AddCylinder(DressRubber, at, 0.36f, 0.36f, 0.23f, 12, i * 13f);
                b.AddCylinder(DressStain, at + new Vector3(0f, 0.231f, 0f), 0.2f, 0.2f, 0.005f, 10, 0f);
            }
            b.AddCylinder(DressRubber, new Vector3(-10.0f, Pad, 1.4f), 0.36f, 0.36f, 0.23f, 12, 0f);
            Solid(solids, -11.2f, Pad, 0f, -10.4f, 0.87f, 0.8f);
            Box(b, DressOlive, -8.6f, Pad + 0.12f, -9.6f, -6.4f, 1.35f, -8.25f);
            Box(b, DressRubber, -8.65f, 1.35f, -9.65f, -6.35f, 1.42f, -8.2f);
            Box(b, DressSteel, -8.7f, 1.0f, -9.66f, -6.3f, 1.08f, -8.19f);
            foreach (float x in new[] { -8.4f, -6.6f })
            foreach (float z in new[] { -9.4f, -8.45f })
                b.AddCylinder(DressRubber, new Vector3(x, Pad, z), 0.07f, 0.07f, 0.12f, 6, 0f);
            Solid(solids, -8.65f, Pad, -9.65f, -6.35f, 1.42f, -8.2f);
            foreach (var (x, z, arm) in new[] { (11.3f, -1.5f, -1f), (-11.3f, 8.6f, 1f) })
            {
                b.AddCylinder(DressConcrete, new Vector3(x, Pad, z), 0.22f, 0.2f, 0.35f, 8, 0f);
                b.AddCylinder(DressSteel, new Vector3(x, Pad, z), 0.09f, 0.07f, 6.6f, 8, 0f);
                var head = new Vector3(x + arm * 1.3f, 6.55f, z);
                b.AddTube(DressSteel, new Vector3(x, 6.5f, z), head, 0.05f, 0.05f, 6);
                Box(b, DressSteel, head.x - 0.35f, 6.42f, z - 0.2f, head.x + 0.35f, 6.6f, z + 0.2f);
                Box(b, DressWhite, head.x - 0.3f, 6.4f, z - 0.15f, head.x + 0.3f, 6.42f, z + 0.15f);
                Solid(solids, x - 0.15f, Pad, z - 0.15f, x + 0.15f, 6.7f, z + 0.15f);
            }

            // Parking bays west of the shop, and a broken lane line along the road side.
            foreach (float x in new[] { -6.2f, -8.7f, -11.2f })
                Box(b, DressWhite, x - 0.06f, Pad, -5.6f, x + 0.06f, Pad + 0.008f, -1.0f);
            Box(b, DressWhite, -11.2f, Pad, -5.66f, -6.14f, Pad + 0.008f, -5.54f);
            for (float x = -11f; x < 11f; x += 2.6f)
                Box(b, DressWhite, x, Pad, 9.35f, x + 1.4f, Pad + 0.008f, 9.47f);
        }

        private static void DressOutpost(FlatMeshBuilder b, System.Random random, List<Bounds> solids)
        {
            // Barbed wire coiled along the tops of the standing walls, on little steel stakes.
            void Wire(Vector3 from, Vector3 to)
            {
                var along = to - from;
                float length = along.magnitude;
                var dir = along / length;
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                const float radius = 0.24f, turn = 0.42f;
                const int perTurn = 7;
                int steps = Mathf.Max(2, Mathf.RoundToInt(length / turn * perTurn));
                Vector3 Point(int i)
                {
                    float t = i / (float)steps;
                    float a = i * Mathf.PI * 2f / perTurn;
                    return from + dir * (t * length) + side * (Mathf.Cos(a) * radius) + Vector3.up * (radius + 0.04f + Mathf.Sin(a) * radius);
                }
                var last = Point(0);
                for (int i = 1; i <= steps; i++)
                {
                    var next = Point(i);
                    b.AddTube(DressSteel, last, next, 0.016f, 0.016f, 3);
                    last = next;
                }
                for (float d = 0.6f; d < length; d += 2.4f)
                {
                    var foot = from + dir * d;
                    b.AddTube(DressRust, foot, foot + Vector3.up * 0.62f + side * 0.12f, 0.025f, 0.02f, 4);
                    b.AddTube(DressRust, foot, foot + Vector3.up * 0.62f - side * 0.12f, 0.025f, 0.02f, 4);
                }
            }
            // The walls as cast panels: a pilaster every few metres on both faces, a coping along the
            // top, and grime at the foot (flat walls this long read as cardboard).
            void Panels(float x0, float z0, float x1, float z1, float height)
            {
                bool alongX = Mathf.Abs(x1 - x0) > Mathf.Abs(z1 - z0);
                float a0 = alongX ? Mathf.Min(x0, x1) : Mathf.Min(z0, z1), a1 = alongX ? Mathf.Max(x0, x1) : Mathf.Max(z0, z1);
                float across = alongX ? z0 : x0, topY = Pad + height;
                const float half = 0.225f;
                void Slab(int sub, float from, float to, float y0, float y1, float out0, float out1)
                {
                    if (alongX) Box(b, sub, from, y0, across + out0, to, y1, across + out1);
                    else Box(b, sub, across + out0, y0, from, across + out1, y1, to);
                }
                Slab(DressConcrete, a0 - 0.04f, a1 + 0.04f, topY, topY + 0.1f, -half - 0.07f, half + 0.07f);
                Slab(DressStain, a0, a1, Pad, Pad + 0.42f, -half - 0.012f, -half + 0.01f);
                Slab(DressStain, a0, a1, Pad, Pad + 0.42f, half - 0.01f, half + 0.012f);
                int n = Mathf.Max(1, Mathf.RoundToInt((a1 - a0) / 3.4f));
                for (int i = 0; i <= n; i++)
                {
                    float c = Mathf.Lerp(a0 + 0.25f, a1 - 0.25f, i / (float)n);
                    Slab(DressConcrete, c - 0.24f, c + 0.24f, Pad, topY, -half - 0.11f, -half + 0.02f);
                    Slab(DressConcrete, c - 0.24f, c + 0.24f, Pad, topY, half - 0.02f, half + 0.11f);
                }
            }
            Panels(-10.6f, 10.6f, -2.2f, 10.6f, 3f);
            Panels(2.2f, 10.6f, 10.6f, 10.6f, 3f);
            Panels(-10.8f, -10.6f, 10.8f, -10.6f, 3f);
            Panels(-10.6f, -10.8f, -10.6f, 0.8f, 3f);
            Panels(10.6f, -1f, 10.6f, 10.6f, 3f);

            const float top = 3.25f, e = 10.6f;
            Wire(new Vector3(-10.4f, top, e), new Vector3(-2.4f, top, e));
            Wire(new Vector3(2.4f, top, e), new Vector3(10.4f, top, e));
            Wire(new Vector3(-10.6f, top, -e), new Vector3(10.6f, top, -e));
            Wire(new Vector3(-e, top, -10.4f), new Vector3(-e, top, 0.6f));
            Wire(new Vector3(e, top, -0.8f), new Vector3(e, top, 10.4f));

            // The breach in the left wall stopped up with sandbags; more on the tower and at its foot.
            void Bag(Vector3 at, float yaw) =>
                b.AddBlob(DressSandbag, at, new Vector3(0.3f, 0.12f, 0.19f), Quaternion.Euler(0f, yaw, 0f), random, 0.12f);
            for (float z = 3.4f; z < 9.8f; z += 0.58f)
            {
                Bag(new Vector3(-e + Rand(random, -0.04f, 0.04f), 1.35f + 0.11f, z), 90f);
                if (z > 4.5f && z < 8.6f) Bag(new Vector3(-e + Rand(random, -0.04f, 0.04f), 1.35f + 0.33f, z + 0.29f), 90f);
            }
            for (float x = 6.4f; x < 9.0f; x += 0.6f)
            {
                Bag(new Vector3(x, 4.675f + 0.11f, 8.82f), 0f);
                Bag(new Vector3(x + 0.3f, 4.675f + 0.33f, 8.82f), 0f);
            }
            // A low nest of bags by the tower's foot, facing the gate.
            for (int i = 0; i < 7; i++)
            {
                float a = (-70f + i * 23f) * Mathf.Deg2Rad;
                var at = new Vector3(4.6f + Mathf.Sin(a) * 1.4f, Pad + 0.11f, 6.4f + Mathf.Cos(a) * 1.4f);
                Bag(at, a * Mathf.Rad2Deg + 90f);
                if (i % 2 == 0) Bag(at + Vector3.up * 0.22f, a * Mathf.Rad2Deg + 90f);
            }
            Solid(solids, 3.2f, Pad, 6.6f, 5.9f, 0.6f, 7.9f);

            // The watchtower: crossed bracing, a railing, a ladder up its yard side, a sheet roof, a lamp.
            float[] legs = { 6.5f, 8.7f };
            const float deck = 4.675f;
            foreach (float z in legs)
            {
                b.AddTube(DressSteel, new Vector3(6.5f, 0.4f, z), new Vector3(8.7f, 4.3f, z), 0.04f, 0.04f, 4);
                b.AddTube(DressSteel, new Vector3(8.7f, 0.4f, z), new Vector3(6.5f, 4.3f, z), 0.04f, 0.04f, 4);
            }
            foreach (float x in legs)
            {
                b.AddTube(DressSteel, new Vector3(x, 0.4f, 6.5f), new Vector3(x, 4.3f, 8.7f), 0.04f, 0.04f, 4);
                b.AddTube(DressSteel, new Vector3(x, 0.4f, 8.7f), new Vector3(x, 4.3f, 6.5f), 0.04f, 0.04f, 4);
            }
            var corners = new[] { new Vector3(6.25f, 0f, 6.25f), new Vector3(8.95f, 0f, 6.25f), new Vector3(8.95f, 0f, 8.95f), new Vector3(6.25f, 0f, 8.95f) };
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i];
                var c = corners[(i + 1) % 4];
                b.AddTube(DressSteel, a + Vector3.up * deck, a + Vector3.up * 6.95f, 0.04f, 0.04f, 4);
                // The railing, open on the ladder's side.
                if (i == 3) continue;
                b.AddTube(DressSteel, a + Vector3.up * (deck + 1.0f), c + Vector3.up * (deck + 1.0f), 0.03f, 0.03f, 4);
                b.AddTube(DressSteel, a + Vector3.up * (deck + 0.5f), c + Vector3.up * (deck + 0.5f), 0.025f, 0.025f, 4);
            }
            Box(b, DressRust, 6.0f, 6.95f, 6.0f, 9.2f, 7.05f, 9.2f);
            Box(b, DressSteel, 5.95f, 6.9f, 5.95f, 9.25f, 6.95f, 9.25f);
            for (int rail = 0; rail < 2; rail++)
            {
                float z = 7.25f + rail * 0.7f;
                b.AddTube(DressSteel, new Vector3(6.2f, Pad, z), new Vector3(6.2f, deck + 0.9f, z), 0.03f, 0.03f, 4);
            }
            for (float y = 0.45f; y < deck; y += 0.32f)
                b.AddTube(DressSteel, new Vector3(6.2f, y, 7.25f), new Vector3(6.2f, y, 7.95f), 0.02f, 0.02f, 4);
            var lamp = new Vector3(8.7f, deck + 1.05f, 8.7f);
            b.AddTube(DressSteel, lamp, lamp + new Vector3(0f, 0.15f, 0f), 0.03f, 0.03f, 4);
            b.AddTube(DressSteel, lamp + new Vector3(0.1f, 0.25f, 0.1f), lamp + new Vector3(-0.2f, 0.35f, -0.2f), 0.13f, 0.17f, 8, DressWhite);

            // The guard hut: a door frame, a window slit, a radio mast with a red light on the roof.
            Box(b, DressSteel, -7.24f, Pad, -4.19f, -7.14f, 2.25f, -3.81f);
            Box(b, DressSteel, -4.86f, Pad, -4.19f, -4.76f, 2.25f, -3.81f);
            Box(b, DressSteel, -7.24f, 2.15f, -4.19f, -4.76f, 2.25f, -3.81f);
            Box(b, DressGlass, -9.5f, 1.5f, -3.845f, -8.0f, 1.95f, -3.825f);
            Box(b, DressGlass, -9.5f, 1.5f, -4.175f, -8.0f, 1.95f, -4.155f);
            Box(b, DressSteel, -9.58f, 1.42f, -3.87f, -7.92f, 1.5f, -3.8f);
            Box(b, DressSteel, -9.58f, 1.95f, -3.87f, -7.92f, 2.02f, -3.8f);
            var mast = new Vector3(-9.3f, 3.05f, -9.3f);
            b.AddTube(DressSteel, mast, mast + Vector3.up * 4.2f, 0.06f, 0.03f, 6);
            Box(b, DressRed, mast.x - 0.07f, 7.2f, mast.z - 0.07f, mast.x + 0.07f, 7.36f, mast.z + 0.07f);
            for (float y = 4.0f; y < 7f; y += 1.0f)
                b.AddTube(DressSteel, mast + new Vector3(-0.25f, y - 3.05f, 0f), mast + new Vector3(0.25f, y - 3.05f, 0f), 0.015f, 0.015f, 3);
            Box(b, DressOlive, -6.0f, 3.05f, -8.4f, -4.9f, 3.55f, -7.6f);
            Box(b, DressSteel, -5.9f, 3.55f, -8.3f, -5.0f, 3.58f, -7.7f);

            // Supply crates stacked against the back wall, a tarp half over them.
            Box(b, DressOlive, -2.6f, Pad, -10.3f, -1.3f, 0.85f, -9.5f);
            Box(b, DressOlive, -1.2f, Pad, -10.3f, 0.1f, 0.85f, -9.5f);
            Box(b, DressOlive, -2.1f, 0.85f, -10.25f, -0.6f, 1.5f, -9.55f);
            foreach (float x in new[] { -2.6f, -1.2f })
            {
                Box(b, DressYellow, x + 0.15f, 0.55f, -9.495f, x + 0.55f, 0.7f, -9.49f);
                Box(b, DressSteel, x - 0.01f, Pad, -10.31f, x + 0.05f, 0.85f, -9.49f);
            }
            // The tarp: a thin sheet from the top of the stack down over its front, sagging at one end.
            b.AddHex(DressSandbag, (x, y, z) => new Vector3(x == 0 ? -2.7f : -1.0f,
                (z == 0 ? 1.5f : 0.45f) + (y == 0 ? 0f : 0.05f) - (x == 1 ? 0.08f : 0f), z == 0 ? -10.32f : -9.42f));
            Solid(solids, -2.6f, Pad, -10.3f, 0.1f, 1.5f, -9.5f);

            // Barriers either side of the gate inside, hazard stripes across it, a flag by the wall.
            foreach (float x in new[] { -3.4f, 3.4f })
            {
                b.AddHex(DressConcrete, (i, j, k) => new Vector3(x + (i == 0 ? -1.0f : 1.0f), j == 0 ? Pad : Pad + 0.82f,
                    9.0f + (k == 0 ? -1f : 1f) * (j == 0 ? 0.3f : 0.12f)));
                Box(b, DressYellow, x - 1.01f, Pad + 0.5f, 8.83f, x - 0.6f, Pad + 0.62f, 9.17f);
                Box(b, DressYellow, x + 0.6f, Pad + 0.5f, 8.83f, x + 1.01f, Pad + 0.62f, 9.17f);
                Solid(solids, x - 1.0f, Pad, 8.7f, x + 1.0f, Pad + 0.82f, 9.3f);
            }
            for (int i = 0; i < 6; i++)
                TurnedBox(b, DressYellow, new Vector3(-1.9f + i * 0.76f, Pad + 0.004f, 9.95f), new Vector3(0.26f, 0.008f, 0.8f), Quaternion.Euler(0f, 40f, 0f));
            var pole = new Vector3(-9.6f, Pad, 9.6f);
            b.AddCylinder(DressConcrete, pole, 0.25f, 0.22f, 0.3f, 8, 0f);
            b.AddTube(DressSteel, pole, pole + Vector3.up * 7.2f, 0.06f, 0.04f, 6);
            Box(b, DressOlive, pole.x + 0.05f, 6.3f, pole.z - 0.01f, pole.x + 1.45f, 7.1f, pole.z + 0.01f);
            Box(b, DressWhite, pole.x + 0.05f, 6.62f, pole.z - 0.012f, pole.x + 1.45f, 6.78f, pole.z + 0.012f);

            // Broken concrete where the walls fell in, and stains on the yard.
            foreach (var (x, z, s) in new[] { (-10.1f, 1.6f, 0.45f), (-10.9f, 2.3f, 0.35f), (-9.7f, 2.9f, 0.3f), (10.2f, -5.5f, 0.4f), (10.9f, -8.2f, 0.32f), (9.9f, -2.4f, 0.28f) })
                b.AddBlob(DressConcrete, new Vector3(x, Pad + s * 0.4f, z), new Vector3(s, s * 0.6f, s * 0.9f), random, 0.3f);
            Stain(b, 1.5f, 1.2f, 0.9f, random);
            Stain(b, -4.0f, 3.5f, 0.6f, random);
            Stain(b, 6.0f, -1.8f, 0.7f, random);
        }
    }
}
