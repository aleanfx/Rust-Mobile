using System.Collections.Generic;
using Oxfall.Core.Gathering;
using Oxfall.Game.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>Everything <see cref="WorldBuilder"/> made, for the rest of the game to use.</summary>
    public sealed class WorldInstance
    {
        public IslandShape Shape;
        public Transform Root;
        public Light Sun;
        public readonly List<ResourceNodeView> Nodes = new List<ResourceNodeView>();
        public Vector3 SpawnPosition;
        public float SpawnYaw;
    }

    /// <summary>
    /// Builds the island from a seed: light and atmosphere, ground, sea, and the trees, rocks and
    /// plants that can be gathered. The same seed always gives the same island.
    /// </summary>
    public static class WorldBuilder
    {
        // Afternoon with a light, neutral haze, closer to Rust than to a sunset: blue sky, pale grey
        // horizon, slightly warm sun. The horizon and the fog are the same colour so the far land
        // melts into the sky.
        public static readonly Color FogColour = new Color(0.75f, 0.79f, 0.82f);
        public static readonly Color SkyTop = new Color(0.30f, 0.50f, 0.78f);
        public static readonly Color SkyBelowHorizon = new Color(0.62f, 0.65f, 0.66f);
        public static readonly Color SunColour = new Color(1f, 0.93f, 0.82f);

        public static WorldInstance Build(int seed, MaterialLibrary materials)
        {
            var world = new WorldInstance { Shape = new IslandShape(seed) };
            world.Root = new GameObject("World").transform;

            world.Sun = BuildAtmosphere(world.Root);

            var colourMap = TerrainBuilder.BuildColourMap(world.Shape);
            var splatMap = materials.HasTexturedGround ? TerrainBuilder.BuildSplatMap(world.Shape) : null;
            TerrainBuilder.Build(world.Shape, materials.MakeGround(colourMap, splatMap), world.Root);
            BuildSea(materials, world.Root);
            BuildPonds(world.Shape, materials, world.Root);
            materials.SetSeaDepth(TerrainBuilder.BuildDepthMap(world.Shape), IslandShape.WorldSize,
                TerrainBuilder.DepthMapRange);
            PlaceResources(world, materials, seed);
            FindSpawn(world, seed);
            return world;
        }

        private static Light BuildAtmosphere(Transform parent)
        {
            var sunObject = new GameObject("Sun");
            sunObject.transform.SetParent(parent, false);
            // Low enough for long shadows and warm light, high enough that the ground stays readable.
            sunObject.transform.rotation = Quaternion.Euler(31f, -52f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = SunColour;
            sun.intensity = 1.8f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.7f;
            RenderSettings.sun = sun;

            // Three-colour ambient: cheap, needs no baking, and keeps shadows blue-ish rather than black.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.55f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.27f, 0.25f, 0.21f);

            // Exponential-squared haze: clear near the player, thick far away, hides the far clip.
            // GraphicsQuality sets the density per level.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColour;
            RenderSettings.fogDensity = 0.009f;

            // The sea reflects these.
            Shader.SetGlobalVector("_OxfallSkyTop", SkyTop.linear);
            Shader.SetGlobalVector("_OxfallSkyHorizon", FogColour.linear);

            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_HorizonColor"))
            {
                sky.SetColor("_TopColor", SkyTop);
                sky.SetColor("_HorizonColor", FogColour);
                sky.SetColor("_BottomColor", SkyBelowHorizon);
                sky.SetColor("_SunColor", SunColour);
                sky.SetVector("_SunDirection", -sunObject.transform.forward);
                sky.SetFloat("_Exponent", 0.45f);
            }
            return sun;
        }

        private static void BuildSea(MaterialLibrary materials, Transform parent)
        {
            var sea = new GameObject("Sea");
            sea.transform.SetParent(parent, false);
            sea.transform.position = new Vector3(0f, IslandShape.WaterLevel, 0f);
            // A grid rather than one huge quad: triangles kilometres wide, reaching far behind the
            // camera, can be dropped by some GPUs (the sea vanished in the browser build). 1200 m
            // covers the island plus the farthest view distance from its edge.
            sea.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Grid(1200f, 24);
            var renderer = sea.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = materials.Water;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A disc of fresh water in each pond, a little wider than the hole so its edge hides in the bank.</summary>
        private static void BuildPonds(IslandShape shape, MaterialLibrary materials, Transform parent)
        {
            foreach (var pond in shape.Ponds)
            {
                var go = new GameObject("Pond");
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(pond.Centre.x, pond.Level, pond.Centre.y);
                var p = pond;
                go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.PondSurface(pond.Radius + 3f, 26, 96,
                    (x, z) => TerrainBuilder.MeshHeight(shape, p.Centre.x + x, p.Centre.y + z) - p.Level);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials.Pond;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            Debug.Log($"[Oxfall] {shape.Ponds.Count} ponds.");
        }

        private static void PlaceResources(WorldInstance world, MaterialLibrary materials, int seed)
        {
            var random = new System.Random(seed * 31 + 7);
            var shape = world.Shape;

            // Meshes draw from their own random numbers, so where things stand (which every player
            // must agree on) never depends on how they are drawn.
            var shapes = new System.Random(seed * 17 + 3);
            bool needleCards = materials.HasNeedleCards, leafCards = materials.HasLeafCards;
            var pines = new Mesh[4];
            for (int i = 0; i < pines.Length; i++) pines[i] = MeshFactory.Pine(shapes, needleCards);
            var broadleaves = new Mesh[3];
            for (int i = 0; i < broadleaves.Length; i++) broadleaves[i] = MeshFactory.Broadleaf(shapes, leafCards);
            var rocks = new Mesh[4];
            for (int i = 0; i < rocks.Length; i++) rocks[i] = MeshFactory.Rock(shapes);
            var hemps = new Mesh[2];
            for (int i = 0; i < hemps.Length; i++) hemps[i] = MeshFactory.Hemp(shapes);
            var bushes = new Mesh[2];
            for (int i = 0; i < bushes.Length; i++) bushes[i] = MeshFactory.BerryBush(shapes, leafCards);

            var parent = new GameObject("Resources").transform;
            parent.SetParent(world.Root, false);

            const float cell = 6f;
            int cells = Mathf.RoundToInt(IslandShape.WorldSize / cell);
            int nextId = 1;

            for (int gz = 0; gz < cells; gz++)
            for (int gx = 0; gx < cells; gx++)
            {
                float x = -IslandShape.HalfSize + (gx + 0.15f + 0.7f * (float)random.NextDouble()) * cell;
                float z = -IslandShape.HalfSize + (gz + 0.15f + 0.7f * (float)random.NextDouble()) * cell;
                float h = shape.Height(x, z);
                if (h < 1.3f || shape.PondBank(x, z) > 0f) continue;

                float flatness = shape.Normal(x, z).y;
                float forest = shape.Forest(x, z);
                float roll = (float)random.NextDouble();
                bool grassy = h > 2.4f && h < 17f && flatness > 0.88f;

                float treeChance = grassy ? Mathf.Clamp01((forest - 0.44f) * 3.2f) * 0.55f + 0.02f : 0f;
                float stoneChance = 0.035f + (flatness < 0.9f ? 0.08f : 0f);
                float oreChance = h > 5f ? 0.022f : 0f;
                float hempChance = grassy && forest < 0.5f ? 0.05f : 0f;
                float berryChance = grassy && forest > 0.3f && forest < 0.7f ? 0.035f : 0f;

                var position = new Vector3(x, h, z);
                float yaw = (float)random.NextDouble() * 360f;

                if (roll < treeChance)
                {
                    bool pine = forest > 0.55f || random.NextDouble() < 0.5;
                    var mesh = pine ? pines[random.Next(pines.Length)] : broadleaves[random.Next(broadleaves.Length)];
                    var leaves = pine ? materials.Needles[random.Next(materials.Needles.Length)]
                                      : materials.Leaves[random.Next(materials.Leaves.Length)];
                    float scale = 0.8f + (float)random.NextDouble() * 0.5f;
                    Material[] treeMaterials;
                    if (pine && needleCards)
                        treeMaterials = new[] { materials.Bark, leaves, materials.NeedleCards[nextId % materials.NeedleCards.Length] };
                    else if (!pine && leafCards)
                        treeMaterials = new[] { materials.Bark, leaves, materials.LeafCards[nextId % materials.LeafCards.Length] };
                    else
                        treeMaterials = new[] { materials.Bark, leaves };
                    var go = MakeNode(pine ? "Pine" : "Tree", mesh, treeMaterials,
                        position - new Vector3(0f, 0.2f, 0f), yaw, Vector3.one * scale, parent);
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.center = new Vector3(0f, 2.5f, 0f);
                    capsule.radius = 0.4f;
                    capsule.height = 5f;
                    AddView(world, go, nextId++, ResourceNodeType.Tree);
                }
                else if (roll < treeChance + stoneChance)
                {
                    float s = 1.3f + (float)random.NextDouble() * 1.0f;
                    var go = MakeNode("Stone", rocks[random.Next(rocks.Length)], new[] { materials.Stone },
                        position - new Vector3(0f, 0.25f * s, 0f), yaw, new Vector3(s, s * 0.85f, s), parent);
                    AddSphere(go, new Vector3(0f, 0.25f, 0f), 0.85f, false);
                    AddView(world, go, nextId++, ResourceNodeType.Stone);
                }
                else if (roll < treeChance + stoneChance + oreChance)
                {
                    float s = 1.1f + (float)random.NextDouble() * 0.7f;
                    var go = MakeNode("Metal ore", rocks[random.Next(rocks.Length)], new[] { materials.MetalOre },
                        position - new Vector3(0f, 0.2f * s, 0f), yaw, new Vector3(s, s, s), parent);
                    AddSphere(go, new Vector3(0f, 0.25f, 0f), 0.85f, false);
                    AddView(world, go, nextId++, ResourceNodeType.MetalOre);
                }
                else if (roll < treeChance + stoneChance + oreChance + hempChance)
                {
                    var go = MakeNode("Hemp", hemps[random.Next(hemps.Length)], new[] { materials.Hemp },
                        position, yaw, Vector3.one, parent);
                    // A trigger: the player walks through plants but can still aim at them.
                    AddSphere(go, new Vector3(0f, 0.6f, 0f), 0.6f, true);
                    AddView(world, go, nextId++, ResourceNodeType.Hemp);
                }
                else if (roll < treeChance + stoneChance + oreChance + hempChance + berryChance)
                {
                    var bushMaterials = leafCards
                        ? new[] { materials.Berries, materials.Leaves[0], materials.LeafCards[nextId % materials.LeafCards.Length] }
                        : new[] { materials.Berries, materials.Leaves[0] };
                    float s = 0.85f + (float)random.NextDouble() * 0.4f;
                    var go = MakeNode("Berry bush", bushes[random.Next(bushes.Length)], bushMaterials,
                        position - new Vector3(0f, 0.05f, 0f), yaw, Vector3.one * s, parent);
                    AddSphere(go, new Vector3(0f, 0.45f, 0f), 0.6f, true);
                    AddView(world, go, nextId++, ResourceNodeType.BerryBush);
                }
            }

            Debug.Log($"[Oxfall] Placed {world.Nodes.Count} resource nodes.");
        }

        private static GameObject MakeNode(string name, Mesh mesh, Material[] materials, Vector3 position, float yaw,
            Vector3 scale, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go;
        }

        private static void AddSphere(GameObject go, Vector3 centre, float radius, bool trigger)
        {
            var sphere = go.AddComponent<SphereCollider>();
            sphere.center = centre;
            sphere.radius = radius;
            sphere.isTrigger = trigger;
        }

        private static void AddView(WorldInstance world, GameObject go, int id, ResourceNodeType type)
        {
            var view = go.AddComponent<ResourceNodeView>();
            view.Init(new ResourceNode(id, type));
            world.Nodes.Add(view);
        }

        /// <summary>A spot on the beach, facing inland. Walks in from the sea until it finds sand.</summary>
        private static void FindSpawn(WorldInstance world, int seed)
        {
            var shape = world.Shape;
            var random = new System.Random(seed ^ 0x5f3759df);
            float startAngle = (float)random.NextDouble() * 360f;

            for (int attempt = 0; attempt < 36; attempt++)
            {
                float angle = (startAngle + attempt * 10f) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                for (float r = IslandShape.HalfSize - 5f; r > 10f; r -= 2f)
                {
                    float x = dir.x * r, z = dir.y * r;
                    if (shape.Height(x, z) < 0.6f) continue;

                    // A few steps further in, so the player starts dry.
                    x -= dir.x * 6f;
                    z -= dir.y * 6f;
                    if (shape.Normal(x, z).y < 0.9f) break;
                    world.SpawnPosition = new Vector3(x, shape.Height(x, z) + 0.2f, z);
                    world.SpawnYaw = Mathf.Atan2(-dir.x, -dir.y) * Mathf.Rad2Deg;
                    return;
                }
            }

            world.SpawnPosition = new Vector3(0f, shape.Height(0f, 0f) + 1f, 0f);
            world.SpawnYaw = 0f;
        }
    }
}
