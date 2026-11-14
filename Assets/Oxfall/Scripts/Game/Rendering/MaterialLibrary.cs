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
        private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
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
        /// <summary>Worn, rusty iron: hinges, handles, metal fragments.</summary>
        public Material Metal { get; }
        /// <summary>The olive canvas of a sleeping bag.</summary>
        public Material Bedroll { get; }
        /// <summary>Corrugated, riveted, rust-streaked sheet metal: the metal building tier and the sheet metal door.</summary>
        public Material SheetMetal { get; }
        /// <summary>Bright worn steel: the heads of metal tools.</summary>
        public Material Steel { get; }
        /// <summary>Old oil drums: blue, rust red and olive paint, and their bare, rusting rims and lids.</summary>
        public Material[] BarrelPaint { get; }
        /// <summary>The ruins: weathered concrete, and the olive paint of their supply crates.</summary>
        public Material Concrete { get; }
        public Material CratePaint { get; }
        /// <summary>The ruins' recycler: chipped machine yellow.</summary>
        public Material RecyclerPaint { get; }
        /// <summary>Supply drops: the cargo plane's grey-olive paint, the crate's deep blue, and the parachute's orange and white.</summary>
        public Material PlanePaint { get; }
        public Material DropPaint { get; }
        public Material ChuteOrange { get; }
        public Material ChuteWhite { get; }
        public Material BarrelRust { get; }
        /// <summary>The ruins' dressing: white paint, dark glass, rubber, sandbags, warning yellow, oil stains.</summary>
        public Material WhitePaint { get; }
        public Material Glass { get; }
        public Material Rubber { get; }
        public Material Sandbag { get; }
        public Material WarnYellow { get; }
        public Material Stain { get; }
        /// <summary>Rain round the camera (null in a build without its shader: no rain drawn).</summary>
        public Material Rain { get; }
        /// <summary>A code lock: its dark casing, its keys, and its light (red locked, green open).</summary>
        public Material LockBody { get; }
        public Material LockKeys { get; }
        public Material LedRed { get; }
        public Material LedGreen { get; }
        /// <summary>The glow of fire (Oxfall/Flame, additive); a plain orange if it isn't in the build.</summary>
        public Material Flame { get; }
        public Material Berries { get; }
        /// <summary>Fresh water in the ponds: the sea's material, greener, without the shore and its foam.</summary>
        public Material Pond { get; }

        /// <summary>Other players: a worn shirt, dark trousers and skin.</summary>
        public Material Shirt { get; }
        public Material Trousers { get; }
        public Material Skin { get; }

        /// <summary>The animals' coats (a boar's bristles, a wolf's grey, a deer's tan), their noses and hooves, and horn.</summary>
        public Material BoarHide { get; }
        public Material WolfFur { get; }
        public Material DeerHide { get; }
        public Material Hoof { get; }
        public Material Horn { get; }

        /// <summary>What an animal gives: raw and roasted meat, bone, and leather.</summary>
        public Material RawMeat { get; }
        public Material CookedMeat { get; }
        public Material Bone { get; }
        public Material Leather { get; }

        /// <summary>Sulfur rocks: grey stone with a sickly yellow cast, and the bright crystals crusted on them.</summary>
        public Material SulfurRock { get; }
        public Material Sulfur { get; }
        /// <summary>Gunpowder: grey-black grains.</summary>
        public Material Gunpowder { get; }
        /// <summary>A satchel charge's olive canvas.</summary>
        public Material Satchel { get; }

        /// <summary>Guns: blued steel, the brass of a cartridge, and a shell's red paper.</summary>
        public Material GunMetal { get; }
        public Material Brass { get; }
        public Material ShellPaper { get; }

        /// <summary>Medicine: clean bandage cloth, its red stripes and a syringe's dose, the clear barrel, white plastic.</summary>
        public Material Bandage { get; }
        public Material MedRed { get; }
        public Material SyringeBarrel { get; }
        public Material Plastic { get; }

        /// <summary>Blasts: the fireball (additive) and the smoke and dust (alpha-blended), each puff tinted as it goes.</summary>
        public Material Fireball { get; }
        public Material Smoke { get; }
        /// <summary>Whether those are the real puff materials (false: plain stand-ins, kept small and brief).</summary>
        public bool HasPuffs { get; }

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
            Metal = Make("Metal", new Color(0.40f, 0.36f, 0.33f), 0.42f);
            Bedroll = Make("Bedroll", new Color(0.30f, 0.34f, 0.22f), 0.04f);
            SheetMetal = MakeSheetMetal(StoneWall);
            // Forged steel: darker and more metallic than paint, so it takes the sky's light along its edges.
            Steel = Make("Steel", new Color(0.50f, 0.51f, 0.53f), 0.58f);
            Steel.SetFloat(Metallic, 0.6f);
            BarrelPaint = new[]
            {
                Make("Barrel blue", new Color(0.13f, 0.27f, 0.42f), 0.38f),
                Make("Barrel red", new Color(0.46f, 0.13f, 0.09f), 0.34f),
                Make("Barrel olive", new Color(0.29f, 0.31f, 0.18f), 0.3f),
            };
            foreach (var paint in BarrelPaint) paint.SetFloat(Metallic, 0.25f);
            BarrelRust = Make("Barrel rust", new Color(0.33f, 0.21f, 0.13f), 0.22f);
            BarrelRust.SetFloat(Metallic, 0.3f);
            WhitePaint = Make("White paint", new Color(0.8f, 0.8f, 0.77f), 0.32f);
            Glass = Make("Glass", new Color(0.07f, 0.1f, 0.12f), 0.92f);
            Glass.SetFloat(Metallic, 0.35f);
            Rubber = Make("Rubber", new Color(0.075f, 0.075f, 0.08f), 0.18f);
            Sandbag = Make("Sandbag", new Color(0.5f, 0.45f, 0.33f), 0.05f);
            WarnYellow = Make("Warning yellow", new Color(0.86f, 0.64f, 0.1f), 0.3f);
            Stain = Make("Oil stain", new Color(0.2f, 0.19f, 0.18f), 0.62f);
            var rain = Resources.Load<Material>("OxfallRain");
            Rain = rain != null ? new Material(rain) { name = "Rain" } : null;
            Concrete = Textured("OxfallConcrete") ?? Tinted(Stone, "Concrete", Stone.HasProperty(BaseMap) && Stone.GetTexture(BaseMap) != null
                ? new Color(1.05f, 1.05f, 1.02f) : new Color(0.62f, 0.61f, 0.58f));
            CratePaint = Make("Crate paint", new Color(0.25f, 0.29f, 0.17f), 0.28f);
            CratePaint.SetFloat(Metallic, 0.2f);
            RecyclerPaint = Make("Recycler paint", new Color(0.62f, 0.47f, 0.12f), 0.3f);
            RecyclerPaint.SetFloat(Metallic, 0.25f);
            PlanePaint = Make("Plane paint", new Color(0.36f, 0.39f, 0.35f), 0.35f);
            PlanePaint.SetFloat(Metallic, 0.3f);
            DropPaint = Make("Supply crate paint", new Color(0.16f, 0.36f, 0.56f), 0.35f);
            DropPaint.SetFloat(Metallic, 0.2f);
            ChuteOrange = Make("Parachute orange", new Color(0.93f, 0.38f, 0.1f), 0.15f);
            ChuteWhite = Make("Parachute white", new Color(0.86f, 0.85f, 0.8f), 0.15f);
            foreach (var chute in new[] { ChuteOrange, ChuteWhite }) chute.SetFloat(Cull, 0f);
            LockBody = Make("Lock body", new Color(0.15f, 0.16f, 0.17f), 0.55f);
            LockKeys = Make("Lock keys", new Color(0.58f, 0.59f, 0.57f), 0.4f);
            LedRed = Make("Lock red", new Color(1f, 0.16f, 0.1f), 0.9f);
            LedGreen = Make("Lock green", new Color(0.25f, 1f, 0.38f), 0.9f);
            Flame = Resources.Load<Material>("OxfallFlame") ?? Make("Flame", new Color(1f, 0.55f, 0.15f), 0f);
            Berries = Make("Berries", new Color(0.50f, 0.06f, 0.20f), 0.55f);
            Shirt = Make("Shirt", new Color(0.46f, 0.42f, 0.33f), 0.05f);
            Trousers = Make("Trousers", new Color(0.17f, 0.18f, 0.20f), 0.05f);
            Skin = Make("Skin", new Color(0.78f, 0.58f, 0.45f), 0.25f);
            BoarHide = Make("Boar hide", new Color(0.22f, 0.17f, 0.13f), 0.08f);
            WolfFur = Make("Wolf fur", new Color(0.44f, 0.42f, 0.39f), 0.06f);
            DeerHide = Make("Deer hide", new Color(0.56f, 0.39f, 0.24f), 0.08f);
            Hoof = Make("Hoof", new Color(0.07f, 0.06f, 0.055f), 0.35f);
            Horn = Make("Horn", new Color(0.78f, 0.71f, 0.58f), 0.3f);
            RawMeat = Make("Raw meat", new Color(0.66f, 0.17f, 0.15f), 0.55f);
            CookedMeat = Make("Cooked meat", new Color(0.40f, 0.21f, 0.10f), 0.35f);
            Bone = Make("Bone", new Color(0.88f, 0.84f, 0.74f), 0.25f);
            Leather = Make("Leather", new Color(0.47f, 0.31f, 0.18f), 0.22f);
            SulfurRock = Tinted(Stone, "Sulfur rock", Stone.HasProperty(BaseMap) && Stone.GetTexture(BaseMap) != null
                ? new Color(1.02f, 0.95f, 0.62f) : new Color(0.52f, 0.49f, 0.36f));
            Sulfur = Make("Sulfur", new Color(0.86f, 0.74f, 0.16f), 0.45f);
            Gunpowder = Make("Gunpowder", new Color(0.16f, 0.16f, 0.17f), 0.12f);
            Satchel = Make("Satchel", new Color(0.33f, 0.34f, 0.22f), 0.06f);
            GunMetal = Make("Gun metal", new Color(0.16f, 0.17f, 0.19f), 0.62f);
            GunMetal.SetFloat(Metallic, 0.7f);
            Brass = Make("Brass", new Color(0.72f, 0.55f, 0.26f), 0.66f);
            Brass.SetFloat(Metallic, 0.75f);
            ShellPaper = Make("Shell paper", new Color(0.55f, 0.13f, 0.1f), 0.22f);
            Bandage = Make("Bandage", new Color(0.86f, 0.84f, 0.78f), 0.05f);
            MedRed = Make("Med red", new Color(0.72f, 0.08f, 0.07f), 0.5f);
            SyringeBarrel = Make("Syringe barrel", new Color(0.78f, 0.85f, 0.88f), 0.9f);
            Plastic = Make("Plastic", new Color(0.92f, 0.92f, 0.9f), 0.45f);
            var fireball = Resources.Load<Material>("OxfallFireball");
            var smoke = Resources.Load<Material>("OxfallSmoke");
            HasPuffs = fireball != null && smoke != null;
            Fireball = fireball ?? Make("Fireball", new Color(1f, 0.55f, 0.15f), 0f);
            Smoke = smoke ?? Make("Smoke", new Color(0.3f, 0.29f, 0.28f), 0f);
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
                Pond.SetFloat("_Reflection", 0.38f);
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

        /// <summary>
        /// Sheet metal made in code: corrugated sheets about 80 cm wide, overlapping, with rivets
        /// along the seams and rust running down from them. Built on a normal-mapped material from
        /// the editor setup when there is one, so its normal map is used.
        /// </summary>
        private Material MakeSheetMetal(Material normalMapped)
        {
            MakeSheetMetalTextures(out var albedo, out var normal);
            bool textured = normalMapped.HasProperty(BaseMap) && normalMapped.GetTexture(BaseMap) != null;
            var material = textured ? new Material(normalMapped) { name = "Sheet metal" } : Make("Sheet metal", Color.white, 0.3f);
            material.SetTexture(BaseMap, albedo);
            material.SetColor(BaseColor, Color.white);
            if (material.HasProperty(BumpMap)) material.SetTexture(BumpMap, normal);
            material.SetFloat(Smoothness, 0.34f);
            material.SetFloat(Metallic, 0.25f);
            return material;
        }

        /// <summary>One tile (1.6 m, as building pieces are mapped): two sheets across, two down.</summary>
        private static void MakeSheetMetalTextures(out Texture2D albedo, out Texture2D normal)
        {
            const int size = 256;
            const int ridges = 10;
            const float rivetRadius = 3.2f;
            var colours = new Color32[size * size];
            var normals = new Color32[size * size];
            var tints = new[] { 1f, 0.9f, 0.95f, 0.85f };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                int sheet = (u < 0.5f ? 0 : 1) + (v < 0.5f ? 0 : 2);
                // Corrugation across u: ridges and valleys.
                float phase = u * ridges * 2f * Mathf.PI;
                float nx = -Mathf.Cos(phase) * 0.55f, ny = 0f;
                float valley = 0.5f - 0.5f * Mathf.Sin(phase);
                // Each sheet's lower edge laps over the one below: a step across at v = 0 and 0.5,
                // and a seam down at u = 0 and 0.5.
                float above = Mathf.Repeat(v, 0.5f);
                float step = Mathf.Min(above, 0.5f - above);
                float seam = Mathf.Min(Mathf.Repeat(u, 0.5f), 0.5f - Mathf.Repeat(u, 0.5f));
                if (step < 0.01f) ny = above < 0.25f ? 0.9f : -0.9f;
                // Rivets on every other ridge crest, just above each step.
                float rivet = 0f;
                float crest = (Mathf.Round((u * ridges - 0.25f) / 2f) * 2f + 0.25f) / ridges;
                float du = (u - crest) * size, dv = (above - 0.03f) * size;
                float rd = Mathf.Sqrt(du * du + dv * dv);
                if (rd < rivetRadius)
                {
                    rivet = 1f - rd / rivetRadius;
                    nx = du / rivetRadius * 0.9f;
                    ny = dv / rivetRadius * 0.9f;
                }
                // Rust: blotches, heavier just above each step where water sits, streaking down.
                // Each sheet rusts its own way (sheets are separate, so no seam to hide between them).
                float blotch = Fbm(u, v, 6, 6, 7 + sheet * 101);
                float streak = Fbm(u, v, 40, 3, 13 + sheet * 7);
                float wet = 1f - Mathf.Clamp01(above / 0.12f);
                float rust = Mathf.Clamp01((blotch * 1.15f + streak * 0.45f + wet * 0.25f - 0.85f) * 2.4f);
                float grain = Fbm(u, v, 64, 64, 3);
                var steel = new Color(0.40f, 0.42f, 0.43f) * (tints[sheet] * (0.9f + 0.16f * grain));
                var rustColour = Color.Lerp(new Color(0.42f, 0.22f, 0.10f), new Color(0.26f, 0.13f, 0.07f), grain);
                var c = Color.Lerp(steel, rustColour, rust);
                c *= 1f - 0.18f * valley + 0.15f * rivet;
                if (step < 0.01f) c *= 0.7f;
                if (seam < 0.006f) c *= 0.82f;
                c.a = 1f;
                colours[y * size + x] = c;
                // Rust roughens the surface a little.
                nx += (Fbm(u, v, 96, 96, 5) - 0.5f) * 0.6f * rust;
                var n = new Vector3(nx, ny, 1f).normalized;
                byte r = (byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), g = (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f);
                // x in red and in alpha: read right whether the platform takes RGB or AG normal maps.
                normals[y * size + x] = new Color32(r, g, (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), r);
            }
            albedo = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Sheet metal", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            albedo.SetPixels32(colours);
            albedo.Apply(true, true);
            normal = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Sheet metal normals", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            normal.SetPixels32(normals);
            normal.Apply(true, true);
        }

        /// <summary>
        /// Four octaves of value noise over the tile (u, v from 0 to 1), starting at fx by fy cells,
        /// wrapping at the tile's edges so it repeats without a seam. 0 to 1.
        /// </summary>
        private static float Fbm(float u, float v, int fx, int fy, int seed)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += Noise(u * fx, v * fy, fx, fy, seed + octave * 31) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                fx *= 2;
                fy *= 2;
            }
            return sum / total;
        }

        private static float Noise(float x, float y, int periodX, int periodY, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int xa = Wrap(x0, periodX), xb = Wrap(x0 + 1, periodX), ya = Wrap(y0, periodY), yb = Wrap(y0 + 1, periodY);
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static int Wrap(int i, int period) => ((i % period) + period) % period;

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
            }
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
