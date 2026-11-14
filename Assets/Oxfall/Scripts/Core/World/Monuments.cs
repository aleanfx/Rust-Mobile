using System;
using System.Collections.Generic;
using Oxfall.Core.Gathering;

namespace Oxfall.Core.World
{
    public enum MonumentKind
    {
        /// <summary>A walled yard of broken concrete with a guard hut: crates of military supplies.</summary>
        Outpost,
        /// <summary>A roadside fuel stop: a canopy over the pumps and a little shop.</summary>
        GasStation,
    }

    /// <summary>A place on the island people once built: where it is, which way it faces, what it is called.</summary>
    public readonly struct Monument
    {
        public readonly MonumentKind Kind;
        public readonly string Name;
        public readonly float X, Y, Z;
        /// <summary>Which way it faces, in degrees (its front looks along +Z turned by this).</summary>
        public readonly float Yaw;

        public Monument(MonumentKind kind, string name, float x, float y, float z, float yaw)
        {
            Kind = kind;
            Name = name;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }
    }

    /// <summary>What a monument's block is made of, for its look.</summary>
    public enum PropLook
    {
        Concrete,
        /// <summary>Painted, rusting sheet metal (the fuel pumps, the shop's sign).</summary>
        PaintedMetal,
        /// <summary>Bare, dark steel (beams, pillars).</summary>
        Steel,
    }

    /// <summary>One solid block of a monument (a wall, a slab, a pillar): a box, turned about its middle.</summary>
    public readonly struct MonumentProp
    {
        public readonly PropLook Look;
        /// <summary>The middle of the box.</summary>
        public readonly float X, Y, Z;
        public readonly float Yaw;
        public readonly float SizeX, SizeY, SizeZ;

        public MonumentProp(PropLook look, float x, float y, float z, float yaw, float sizeX, float sizeY, float sizeZ)
        {
            Look = look;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
        }
    }

    /// <summary>
    /// The ruins on the island, from its seed, the same on every machine: two or three flat, open
    /// places well apart, each laid out as a military outpost or a gas station, with crates and
    /// barrels to loot. The resource layout leaves their ground clear and adds their crates and barrels.
    /// </summary>
    public static class Monuments
    {
        /// <summary>How far from its middle a monument reaches: nothing else grows inside.</summary>
        public const float Radius = 13f;

        /// <summary>The fewest metres between two monuments.</summary>
        public const float Spacing = 110f;

        public const int Max = 3;

        /// <summary>Node ids for monuments start above every resource cell's id.</summary>
        public static int FirstNodeId => ResourceLayout.Cells * ResourceLayout.Cells + 1000;

        private static readonly (MonumentKind Kind, string Name)[] Plan =
        {
            (MonumentKind.Outpost, "Puesto militar"),
            (MonumentKind.GasStation, "Gasolinera vieja"),
            (MonumentKind.Outpost, "Cuartel en ruinas"),
        };

        private static readonly Dictionary<int, List<Monument>> Cache = new Dictionary<int, List<Monument>>();

        /// <summary>The last island asked about, for the many quick questions while the world is laid out.</summary>
        private static (IslandGround Ground, List<Monument> Monuments) _last;

        /// <summary>Where the monuments stand on this island.</summary>
        public static IReadOnlyList<Monument> Find(IslandGround ground)
        {
            var last = _last;
            if (ReferenceEquals(last.Ground, ground)) return last.Monuments;
            lock (Cache)
            {
                if (!Cache.TryGetValue(ground.Seed, out var found))
                {
                    found = Search(ground);
                    Cache[ground.Seed] = found;
                }
                _last = (ground, found);
                return found;
            }
        }

        private static List<Monument> Search(IslandGround ground)
        {
            // Candidates on a 10 m grid, tried in an order shuffled by the seed.
            var candidates = new List<(uint Order, float X, float Z)>();
            const float step = 10f;
            float reach = IslandGround.IslandRadius - 25f;
            for (float z = -reach; z <= reach; z += step)
            for (float x = -reach; x <= reach; x += step)
            {
                if (x * x + z * z > reach * reach) continue;
                candidates.Add((Hash(ground.Seed, (int)Math.Round(x), (int)Math.Round(z)), x, z));
            }
            candidates.Sort((a, b) => a.Order.CompareTo(b.Order));

            var monuments = new List<Monument>(Max);
            foreach (var c in candidates)
            {
                if (monuments.Count >= Max) break;
                bool clear = true;
                foreach (var m in monuments)
                    if ((m.X - c.X) * (m.X - c.X) + (m.Z - c.Z) * (m.Z - c.Z) < Spacing * Spacing) { clear = false; break; }
                if (!clear || !Flat(ground, c.X, c.Z, out float top)) continue;
                var plan = Plan[monuments.Count];
                float yaw = (c.Order >> 8) % 4 * 90f;
                monuments.Add(new Monument(plan.Kind, plan.Name, c.X, top, c.Z, yaw));
            }
            return monuments;
        }

        /// <summary>Whether the ground round (x, z) is open, dry and nearly level; and the highest of it, where the monument's pad sits.</summary>
        private static bool Flat(IslandGround ground, float x, float z, out float top)
        {
            top = float.MinValue;
            float low = float.MaxValue;
            for (int ring = 0; ring <= 2; ring++)
            {
                int points = ring == 0 ? 1 : 8;
                float r = ring * Radius * 0.5f;
                for (int i = 0; i < points; i++)
                {
                    float a = i * (MathF.PI * 2f / points);
                    float px = x + MathF.Cos(a) * r, pz = z + MathF.Sin(a) * r;
                    float h = ground.Height(px, pz);
                    if (h < 3f || h > 16f || ground.PondBank(px, pz) > 0f) return false;
                    top = Math.Max(top, h);
                    low = Math.Min(low, h);
                }
            }
            return top - low < 1.4f;
        }

        /// <summary>The crates and barrels of every monument, with ids of their own.</summary>
        public static void AddNodes(IslandGround ground, List<NodeSpawn> nodes)
        {
            var monuments = Find(ground);
            for (int i = 0; i < monuments.Count; i++)
            {
                var m = monuments[i];
                int id = FirstNodeId + i * 64;
                foreach (var (type, lx, lz, yaw) in Loot(m.Kind))
                {
                    Place(m, lx, lz, out float x, out float z);
                    nodes.Add(new NodeSpawn(id++, type, x, m.Y + 0.15f, z, m.Yaw + yaw, 1f, id * 7, false));
                }
            }
        }

        /// <summary>How far past a monument's edge nobody may build or set anything down.</summary>
        public const float NoBuildMargin = 5f;

        /// <summary>Whether (x, z) is too near a monument to build on (the ruins are everyone's).</summary>
        public static bool NoBuilding(IslandGround ground, float x, float z) => Inside(ground, x, z, NoBuildMargin);

        /// <summary>Whether (x, z) is on a monument's ground, where nothing grows.</summary>
        public static bool Inside(IslandGround ground, float x, float z, float margin = 2f)
        {
            foreach (var m in Find(ground))
            {
                float r = Radius + margin;
                if ((m.X - x) * (m.X - x) + (m.Z - z) * (m.Z - z) < r * r) return true;
            }
            return false;
        }

        /// <summary>Where a monument's recycler stands, in world space, and which way it faces (its back to a wall).</summary>
        public static (float X, float Y, float Z, float Yaw) RecyclerSpot(Monument m)
        {
            // The outpost's against the yard's back wall; the gas station's outside the shop's side.
            bool outpost = m.Kind == MonumentKind.Outpost;
            Place(m, outpost ? 2.6f : 5.85f, outpost ? -9.65f : -6f, out float x, out float z);
            return (x, m.Y + 0.15f, z, m.Yaw + (outpost ? 0f : 90f));
        }

        /// <summary>The solid blocks a monument is built of, in world space.</summary>
        public static List<MonumentProp> Props(Monument m)
        {
            var props = new List<MonumentProp>(32);
            void Box(PropLook look, float lx, float ly, float lz, float sx, float sy, float sz, float yaw = 0f)
            {
                Place(m, lx, lz, out float x, out float z);
                props.Add(new MonumentProp(look, x, m.Y + ly, z, m.Yaw + yaw, sx, sy, sz));
            }

            const float pad = 0.15f;
            switch (m.Kind)
            {
                case MonumentKind.Outpost:
                {
                    // The yard: a cracked concrete pad, sunk deep so no slope shows under it.
                    Box(PropLook.Concrete, 0f, pad - 1.5f, 0f, 22f, 3f, 22f);
                    float w = 3f, t = 0.45f, e = 10.6f, y = pad + w * 0.5f;
                    // The front wall, broken by the gate; the back one whole; the sides with gaps fallen in.
                    Box(PropLook.Concrete, -6.4f, y, e, 8.4f, w, t);
                    Box(PropLook.Concrete, 6.4f, y, e, 8.4f, w, t);
                    Box(PropLook.Concrete, 0f, y, -e, 21.6f, w, t);
                    Box(PropLook.Concrete, -e, y, -5f, t, w, 11.6f);
                    Box(PropLook.Concrete, -e, pad + 0.6f, 6.5f, t, 1.2f, 7f);
                    Box(PropLook.Concrete, e, y, 4.8f, t, w, 11.6f);
                    Box(PropLook.Concrete, e, pad + 0.9f, -7f, t, 1.8f, 6.6f);
                    // The guard hut in the back corner: three walls, a doorway, a flat roof.
                    float hy = pad + 1.3f;
                    Box(PropLook.Concrete, -6.5f, hy, -10f, 7f, 2.6f, 0.3f);
                    Box(PropLook.Concrete, -10f, hy, -7f, 0.3f, 2.6f, 6f);
                    Box(PropLook.Concrete, -3f, hy, -7f, 0.3f, 2.6f, 6f);
                    Box(PropLook.Concrete, -8.6f, hy, -4f, 2.8f, 2.6f, 0.3f);
                    Box(PropLook.Concrete, -3.9f, hy, -4f, 1.8f, 2.6f, 0.3f);
                    Box(PropLook.Concrete, -6.0f, pad + 2.35f, -4f, 2.4f, 0.5f, 0.3f);
                    Box(PropLook.Concrete, -6.5f, pad + 2.75f, -7f, 7.6f, 0.3f, 6.6f);
                    // Concrete blocks and a fallen beam in the yard, cover from the gate.
                    Box(PropLook.Concrete, 2.5f, pad + 0.5f, 4.5f, 2.4f, 1f, 1f, 20f);
                    Box(PropLook.Concrete, -2.8f, pad + 0.5f, 6.8f, 1.8f, 1f, 1f, -8f);
                    Box(PropLook.Steel, 4.5f, pad + 0.25f, -3.5f, 6f, 0.5f, 0.35f, 35f);
                    // A watchtower's legs and platform by the gate.
                    for (int i = 0; i < 4; i++)
                        Box(PropLook.Steel, 6.5f + (i % 2) * 2.2f, pad + 2.2f, 6.5f + (i / 2) * 2.2f, 0.22f, 4.4f, 0.22f);
                    Box(PropLook.Steel, 7.6f, pad + 4.45f, 7.6f, 2.8f, 0.15f, 2.8f);
                    break;
                }
                case MonumentKind.GasStation:
                {
                    Box(PropLook.Concrete, 0f, pad - 1.5f, 0f, 24f, 3f, 20f);
                    // The canopy over two pumps.
                    for (int i = 0; i < 4; i++)
                        Box(PropLook.Steel, -3.5f + (i % 2) * 7f, pad + 2.1f, 2f + (i / 2) * 5f, 0.35f, 4.2f, 0.35f);
                    Box(PropLook.PaintedMetal, 0f, pad + 4.35f, 4.5f, 10f, 0.4f, 7.5f);
                    Box(PropLook.PaintedMetal, -1.8f, pad + 0.75f, 4.5f, 0.7f, 1.5f, 0.5f);
                    Box(PropLook.PaintedMetal, 1.8f, pad + 0.75f, 4.5f, 0.7f, 1.5f, 0.5f);
                    // The shop: four walls with a door and a window gap in front, a roof.
                    float sy = pad + 1.5f;
                    Box(PropLook.Concrete, 0f, sy, -8.5f, 10f, 3f, 0.3f);
                    Box(PropLook.Concrete, -5f, sy, -6f, 0.3f, 3f, 5f);
                    Box(PropLook.Concrete, 5f, sy, -6f, 0.3f, 3f, 5f);
                    Box(PropLook.Concrete, -3.9f, sy, -3.5f, 2.2f, 3f, 0.3f);
                    Box(PropLook.Concrete, 2.1f, sy, -3.5f, 5.8f, 3f, 0.3f);
                    Box(PropLook.Concrete, -1.8f, pad + 2.6f, -3.5f, 2f, 0.8f, 0.3f);
                    Box(PropLook.Concrete, 0f, pad + 3.15f, -6f, 10.4f, 0.3f, 5.4f);
                    // A counter inside and a rusted car wreck out front.
                    Box(PropLook.Concrete, 2.5f, pad + 0.5f, -7.4f, 3f, 1f, 0.7f);
                    Box(PropLook.PaintedMetal, -7.5f, pad + 0.55f, 3.5f, 1.9f, 1.1f, 4.2f, 12f);
                    Box(PropLook.PaintedMetal, -7.6f, pad + 1.35f, 3.3f, 1.6f, 0.6f, 2.2f, 12f);
                    break;
                }
            }
            return props;
        }

        /// <summary>A monument's crates and barrels: what, and where on its ground (before it is turned).</summary>
        private static IEnumerable<(ResourceNodeType Type, float X, float Z, float Yaw)> Loot(MonumentKind kind)
        {
            if (kind == MonumentKind.Outpost)
            {
                yield return (ResourceNodeType.Crate, -8.4f, -8.6f, 0f);
                yield return (ResourceNodeType.Crate, -4.4f, -8.6f, 90f);
                yield return (ResourceNodeType.Crate, 7.8f, -8.8f, 15f);
                yield return (ResourceNodeType.Barrel, 8.8f, -1f, 0f);
                yield return (ResourceNodeType.Barrel, -8.8f, 1.5f, 0f);
                yield return (ResourceNodeType.Barrel, 0.5f, -1.5f, 0f);
            }
            else
            {
                yield return (ResourceNodeType.Crate, -3.5f, -7.4f, 0f);
                yield return (ResourceNodeType.Crate, 4f, -5.2f, 90f);
                yield return (ResourceNodeType.Crate, 0f, 6.8f, 0f);
                yield return (ResourceNodeType.Barrel, 6.5f, 8.2f, 0f);
                yield return (ResourceNodeType.Barrel, 7.3f, 7.6f, 0f);
                yield return (ResourceNodeType.Barrel, -10.5f, -6f, 0f);
                yield return (ResourceNodeType.Barrel, 10.4f, -8f, 0f);
            }
        }

        /// <summary>A point on a monument's ground, from its own axes to the world's.</summary>
        private static void Place(Monument m, float lx, float lz, out float x, out float z)
        {
            float a = m.Yaw * (MathF.PI / 180f);
            float c = MathF.Cos(a), s = MathF.Sin(a);
            // Turned like Unity's yaw: clockwise seen from above.
            x = m.X + lx * c + lz * s;
            z = m.Z - lx * s + lz * c;
        }

        private static uint Hash(int seed, int x, int z)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)x * 0x85EBCA77u;
                h = (h << 13) | (h >> 19);
                h ^= (uint)z * 0xC2B2AE3Du;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                return h;
            }
        }
    }
}
