using UnityEngine;

namespace Oxfall.Game.Rendering
{
    /// <summary>
    /// Every material the game uses, made at start-up from one URP Lit material kept in Resources.
    /// </summary>
    /// <remarks>
    /// Shader.Find only works in a player build for shaders that something in the build references,
    /// so the editor setup saves <c>Resources/OxfallLit.mat</c> and everything here is a copy of it
    /// with a different colour or texture. Only colour, texture and smoothness change: none of those
    /// are shader keywords, so every copy uses variants the build already contains.
    /// </remarks>
    public sealed class MaterialLibrary
    {
        public const string BaseMaterialResource = "OxfallLit";
        public const string GroundMaterialResource = "OxfallGround";
        public const string TexturedGroundResource = "OxfallTerrain";
        public const string FoliageResource = "OxfallFoliage";
        public const string NeedlesResource = "OxfallNeedles";
        public const string GrassCardsResource = "OxfallGrassCards";

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int Cull = Shader.PropertyToID("_Cull");
        private static readonly int SplatMap = Shader.PropertyToID("_SplatMap");
        private static readonly int RippleMap = Shader.PropertyToID("_RippleMap");
        private static readonly int DetailScale = Shader.PropertyToID("_DetailAlbedoMapScale");

        private readonly Material _base;

        public Material Ground { get; private set; }
        public Material Water { get; }
        public Material Bark { get; }
        public Material[] Needles { get; }
        public Material[] Leaves { get; }
        /// <summary>Photo-leaf card materials for pines and broadleaf trees; null when not in the build.</summary>
        public Material[] NeedleCards { get; }
        public Material[] LeafCards { get; }
        public bool HasNeedleCards => NeedleCards != null;
        public bool HasLeafCards => LeafCards != null;
        /// <summary>Whether <see cref="Grass"/> is the photo grass atlas (cut-out tufts) or plain blades.</summary>
        public bool GrassIsCards { get; }
        public Material Stone { get; }
        public Material MetalOre { get; }
        public Material Hemp { get; }
        public Material HandRock { get; }
        public Material Planks { get; }
        /// <summary>Twig building pieces: pale, freshly cut sticks.</summary>
        public Material Twig { get; }
        /// <summary>Wood building pieces: the planks, lighter, so a wall in shade still reads as wood.</summary>
        public Material WoodWall { get; }
        /// <summary>Stone building pieces: cut blocks.</summary>
        public Material StoneWall { get; }
        /// <summary>Doors: darker, older planks than the walls, so a door stands out.</summary>
        public Material Door { get; }
        public Material Grass { get; }
        public Material GhostOk { get; }
        public Material GhostBad { get; }
        /// <summary>For items: rough cloth, string and lashing, blueprint paper, burnt wood and ash.</summary>
        public Material Cloth { get; }
        public Material Rope { get; }
        public Material Blueprint { get; }
        public Material Char { get; }
        /// <summary>The glow of fire (Oxfall/Flame, additive); a plain orange if it isn't in the build.</summary>
        public Material Flame { get; }
        public Material Berries { get; }
        /// <summary>Fresh water in the ponds: the sea's material, greener, without the shore and its foam.</summary>
        public Material Pond { get; }

        public MaterialLibrary()
        {
            _base = Resources.Load<Material>(BaseMaterialResource);
            if (_base == null)
            {
                Debug.LogError($"[Oxfall] Resources/{BaseMaterialResource}.mat is missing; falling back to Shader.Find, which may render pink in a build.");
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _base = new Material(shader);
            }

            // The muted, rusty palette of the style mock-up ("realistic stylised").
            // Photo-textured materials (made by the editor setup from CC0 textures) where they exist;
            // the plain colours otherwise.
            Water = Textured("OxfallWater") ?? Make("Water", new Color(0.20f, 0.31f, 0.33f), 0.82f);
            if (Water.HasProperty(RippleMap)) Water.SetTexture(RippleMap, MakeRippleTexture());
            Bark = Textured("OxfallBark") ?? Make("Bark", new Color(0.34f, 0.27f, 0.20f), 0.04f);
            var foliage = Resources.Load<Material>(FoliageResource);
            var needles = Resources.Load<Material>(NeedlesResource);
            if (needles != null)
            {
                // Fir branches made of twig photos, a little varied from tree to tree.
                NeedleCards = new[]
                {
                    Tinted(needles, "Needle cards A", new Color(1.00f, 1.05f, 1.00f)),
                    Tinted(needles, "Needle cards B", new Color(1.08f, 1.08f, 0.95f)),
                    Tinted(needles, "Needle cards C", new Color(0.92f, 1.00f, 0.98f)),
                };
            }
            if (foliage != null)
            {
                LeafCards = new[]
                {
                    Tinted(foliage, "Leaf cards A", new Color(0.95f, 1.00f, 0.85f)),
                    Tinted(foliage, "Leaf cards B", new Color(1.05f, 1.00f, 0.80f)),
                };
            }
            // With cards, these are the dark cores inside the crowns: the shade between the leaves.
            Needles = needles != null
                ? new[]
                {
                    Make("Needles A", new Color(0.12f, 0.17f, 0.10f), 0.04f),
                    Make("Needles B", new Color(0.14f, 0.19f, 0.11f), 0.04f),
                    Make("Needles C", new Color(0.11f, 0.16f, 0.10f), 0.04f),
                }
                : new[]
                {
                    Make("Needles A", new Color(0.22f, 0.30f, 0.19f), 0.06f),
                    Make("Needles B", new Color(0.28f, 0.36f, 0.22f), 0.06f),
                    Make("Needles C", new Color(0.25f, 0.32f, 0.18f), 0.06f),
                };
            Leaves = foliage != null
                ? new[]
                {
                    Make("Leaves A", new Color(0.21f, 0.27f, 0.13f), 0.04f),
                    Make("Leaves B", new Color(0.24f, 0.30f, 0.14f), 0.04f),
                }
                : new[]
                {
                    Make("Leaves A", new Color(0.33f, 0.40f, 0.22f), 0.06f),
                    Make("Leaves B", new Color(0.40f, 0.45f, 0.24f), 0.06f),
                };
            Stone = Textured("OxfallStone") ?? Make("Stone", new Color(0.47f, 0.45f, 0.42f), 0.10f);
            MetalOre = Textured("OxfallOre") ?? Make("Metal ore", new Color(0.50f, 0.33f, 0.22f), 0.22f);
            Hemp = Make("Hemp", new Color(0.36f, 0.43f, 0.22f), 0.05f);
            HandRock = Textured("OxfallStone") ?? Make("Hand rock", new Color(0.50f, 0.48f, 0.45f), 0.12f);
            Planks = Textured("OxfallPlanks") ?? Make("Planks", new Color(0.49f, 0.36f, 0.25f), 0.06f);
            Twig = Tinted(Bark, "Twig", Bark.HasProperty(BaseMap) && Bark.GetTexture(BaseMap) != null
                ? new Color(1.25f, 1.12f, 0.88f) : new Color(0.62f, 0.52f, 0.36f));
            var planks = Planks.HasProperty(BaseColor) ? Planks.GetColor(BaseColor) : Color.white;
            WoodWall = Tinted(Planks, "Wood wall", new Color(planks.r * 1.35f, planks.g * 1.3f, planks.b * 1.25f, 1f));
            StoneWall = Textured("OxfallStoneWall") ?? Tinted(Stone, "Stone wall", new Color(0.95f, 0.93f, 0.90f));
            Door = Textured("OxfallDoor") ?? Tinted(Planks, "Door", new Color(0.72f, 0.62f, 0.52f));
            var grassCards = Textured(GrassCardsResource);
            GrassIsCards = grassCards != null;
            Grass = grassCards ?? MakeGrass();
            GhostOk = Make("Ghost ok", new Color(0.35f, 0.85f, 0.45f), 0.1f);
            GhostBad = Make("Ghost bad", new Color(0.90f, 0.30f, 0.25f), 0.1f);
            Cloth = Make("Cloth", new Color(0.62f, 0.55f, 0.42f), 0.04f);
            Rope = Make("Rope", new Color(0.42f, 0.33f, 0.22f), 0.04f);
            Blueprint = Make("Blueprint", new Color(0.27f, 0.42f, 0.62f), 0.12f);
            Char = Make("Char", new Color(0.13f, 0.11f, 0.10f), 0.05f);
            Flame = Resources.Load<Material>("OxfallFlame") ?? Make("Flame", new Color(1f, 0.55f, 0.15f), 0f);
            Berries = Make("Berries", new Color(0.50f, 0.06f, 0.20f), 0.55f);
            Pond = new Material(Water) { name = "Pond" };
            if (Pond.HasProperty("_DepthMap"))
            {
                // The same depth everywhere: no sand showing through and no foam.
                Pond.SetTexture("_DepthMap", Texture2D.whiteTexture);
                Pond.SetVector("_DepthMapScale", new Vector4(0f, 0.5f, 1.8f, 0f));
                Pond.SetColor("_ShallowColor", new Color(0.13f, 0.22f, 0.17f));
                Pond.SetColor("_DeepColor", new Color(0.06f, 0.10f, 0.08f));
                // Small, slow ripples and a dull reflection: still, murky fresh water, not the sea.
                Pond.SetVector("_RippleTiling", new Vector4(3f, 7f, 0.01f, 0f));
                Pond.SetFloat("_RippleStrength", 0.18f);
                Pond.SetFloat("_Reflection", 0.3f);
                Pond.SetFloat("_Glint", 1.2f);
            }
        }

        /// <summary>
        /// Gives the sea the island's depth map (centred on the origin, worldSize across, red =
        /// depth / range), for shallow colour and shore foam.
        /// </summary>
        public void SetSeaDepth(Texture2D depthMap, float worldSize, float range)
        {
            if (!Water.HasProperty("_DepthMap")) return;
            Water.SetTexture("_DepthMap", depthMap);
            Water.SetVector("_DepthMapScale", new Vector4(1f / worldSize, 0.5f, range, 0f));
        }

        /// <summary>Whether the photo-textured ground (Oxfall/Ground) is in the build.</summary>
        public bool HasTexturedGround => Resources.Load<Material>(TexturedGroundResource) != null;

        /// <summary>
        /// The ground material: photo textures blended by the splat map and tinted by the colour map
        /// when the textured ground exists, otherwise the colour map with a close-up grain.
        /// </summary>
        public Material MakeGround(Texture2D colourMap, Texture2D splatMap)
        {
            var textured = splatMap != null ? Resources.Load<Material>(TexturedGroundResource) : null;
            if (textured != null)
            {
                Ground = new Material(textured) { name = "Ground" };
                Ground.SetTexture(BaseMap, colourMap);
                Ground.SetTexture(SplatMap, splatMap);
                // The grass photo is dry and yellow; this greens it, and the colour map only nudges.
                Ground.SetColor("_GrassColor", new Color(0.82f, 0.95f, 0.74f));
                Ground.SetFloat("_TintStrength", 0.15f);
                return Ground;
            }

            // A copy of the ground material the editor setup saved, which adds close-up detail; the
            // plain material still works if it is missing.
            var detailed = Resources.Load<Material>(GroundMaterialResource);
            Ground = detailed != null ? new Material(detailed) { name = "Ground" } : Make("Ground", Color.white, 0.04f);
            Ground.SetTexture(BaseMap, colourMap);
            // Softer close-up grain: at full strength it read as muddy blotches.
            if (Ground.HasProperty(DetailScale)) Ground.SetFloat(DetailScale, 0.65f);
            return Ground;
        }

        /// <summary>
        /// Grass blades: both faces drawn, coloured by a small texture whose rows go from a dark root to
        /// a light tip and whose columns are tints from green to dry, picked per blade by its UVs.
        /// </summary>
        private Material MakeGrass()
        {
            const int tints = 8, rows = 16;
            // Matched to the grass photo on the ground: olive, with dry straw tips here and there.
            var green = new Color(0.40f, 0.42f, 0.24f);
            var dry = new Color(0.56f, 0.51f, 0.33f);
            var pixels = new Color32[tints * rows];
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < tints; x++)
            {
                float v = y / (rows - 1f);
                var c = Color.Lerp(green, dry, x / (tints - 1f) * 0.6f) * Mathf.Lerp(0.62f, 1.05f, Mathf.Pow(v, 0.8f));
                c.a = 1f;
                pixels[y * tints + x] = c;
            }
            var texture = new Texture2D(tints, rows, TextureFormat.RGBA32, false)
            {
                name = "Grass tints",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            var material = Make("Grass", Color.white, 0f);
            material.SetTexture(BaseMap, texture);
            if (material.HasProperty(Cull)) material.SetFloat(Cull, 0f); // both faces
            return material;
        }

        private static Material Tinted(Material source, string name, Color tint)
        {
            var material = new Material(source) { name = name };
            material.SetColor(BaseColor, tint);
            return material;
        }

        /// <summary>A copy of a material the editor setup saved in Resources, or null if it is not there.</summary>
        private static Material Textured(string resource)
        {
            var saved = Resources.Load<Material>(resource);
            return saved != null ? new Material(saved) { name = resource } : null;
        }

        /// <summary>
        /// A tiling normal map of small waves for the sea, made from sine waves with whole-number
        /// frequencies so it repeats without a seam. Stored as plain linear RGB (0.5 = flat).
        /// </summary>
        private static Texture2D MakeRippleTexture()
        {
            const int size = 128;
            var waves = new[]
            {
                new Vector4(3, 1, 0.30f, 0.0f), new Vector4(-2, 4, 0.22f, 1.3f), new Vector4(5, -3, 0.12f, 2.1f),
                new Vector4(1, 7, 0.08f, 0.7f), new Vector4(-8, -5, 0.05f, 4.0f), new Vector4(11, 4, 0.035f, 5.2f),
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float dx = 0f, dy = 0f;
                foreach (var w in waves)
                {
                    float phase = 2f * Mathf.PI * (w.x * u + w.y * v) + w.w;
                    float slope = w.z * Mathf.Cos(phase) * 2f * Mathf.PI / 12f;
                    dx += slope * w.x;
                    dy += slope * w.y;
                }
                var n = new Vector3(-dx, -dy, 1f).normalized;
                pixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Sea ripples",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private Material Make(string name, Color colour, float smoothness)
        {
            var material = new Material(_base) { name = name };
            material.SetColor(BaseColor, colour);
            material.SetFloat(Smoothness, smoothness);
            material.SetFloat(Metallic, 0f);
            material.enableInstancing = true;
            return material;
        }
    }
}
