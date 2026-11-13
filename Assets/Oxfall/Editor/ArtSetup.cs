using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Editor
{
    /// <summary>
    /// Imports the CC0 photo textures in <c>Assets/Oxfall/Art/Textures</c> (fetched by the fetch-art
    /// workflow) and saves the materials that use them in Resources, where the game finds them at run
    /// time. Without the textures nothing is made and the game keeps its plain colours.
    /// </summary>
    public static class ArtSetup
    {
        public const string TexturesFolder = "Assets/Oxfall/Art/Textures";

        public static void Apply()
        {
            // Fire needs no texture: made whether or not the photos are there.
            MakeFlame();
            if (!AssetDatabase.IsValidFolder(TexturesFolder))
            {
                Debug.LogWarning($"[Oxfall] {TexturesFolder} is missing; the game keeps its plain colours.");
                return;
            }

            ConfigureImports();
            MakeLit("OxfallStone", "rock_boulder_dry", new Color(0.86f, 0.84f, 0.80f), 0.12f);
            MakeLit("OxfallOre", "rock_face", new Color(1f, 0.92f, 0.86f), 0.18f);
            MakeLit("OxfallBark", "bark_brown_02", new Color(0.85f, 0.80f, 0.74f), 0.05f);
            MakeLit("OxfallPlanks", "weathered_planks", new Color(1.05f, 0.98f, 0.90f), 0.08f);
            MakeLit("OxfallDoor", "old_planks_02", new Color(0.95f, 0.88f, 0.80f), 0.08f);
            // The first of these stone block photos that downloaded (see tools/art/polyhaven.txt).
            foreach (var id in new[] { "castle_wall_slates", "stone_wall", "medieval_blocks_02", "stone_brick_wall_001" })
                if (Load(id, "diff") != null) { MakeLit("OxfallStoneWall", id, new Color(0.95f, 0.93f, 0.90f), 0.1f); break; }
            MakeGround();
            MakeWater();
            MakeCutout("OxfallFoliage", "jacaranda_tree/jacaranda_tree_leaves.png", 0.1f, 0.15f);
            MakeCutout("OxfallNeedles", "oxfall_fir_branch/oxfall_fir_branch.png", 0.1f, 0.12f);
            MakeCutout("OxfallGrassCards", "grass_bermuda_01/grass_bermuda_01.png", 0.05f, 0.07f);
            AssetDatabase.SaveAssets();
            Debug.Log("[Oxfall] Art materials made from the CC0 textures.");
        }

        /// <summary>
        /// 512 px, mipmapped and compressed; "_nor_gl" files are normal maps. PNG files are leaf
        /// cut-outs: their alpha is kept, their mipmaps keep the same coverage (so trees don't go
        /// bald in the distance) and they don't tile.
        /// </summary>
        private static void ConfigureImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool normal = path.Contains("_nor_gl");
                bool cutout = path.EndsWith(".png");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                var wrap = cutout ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                if (importer.textureType == type && importer.maxTextureSize == 512 && importer.mipmapEnabled
                    && importer.wrapMode == wrap && importer.anisoLevel == 4
                    && importer.sRGBTexture == !normal && importer.alphaIsTransparency == cutout
                    && importer.mipMapsPreserveCoverage == cutout)
                    continue;

                importer.textureType = type;
                importer.sRGBTexture = !normal;
                importer.maxTextureSize = 512;
                importer.mipmapEnabled = true;
                importer.wrapMode = wrap;
                importer.alphaIsTransparency = cutout;
                importer.mipMapsPreserveCoverage = cutout;
                if (cutout)
                {
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaTestReferenceValue = 0.5f;
                }
                importer.anisoLevel = 4;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
        }

        private static Texture2D Load(string id, string map) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{id}/{id}_{map}.jpg");

        private static Material LoadOrCreate(string name, Shader shader)
        {
            string path = $"{ProjectSetup.ResourcesFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            return material;
        }

        /// <summary>A URP Lit material with a photo texture and its normal map.</summary>
        private static void MakeLit(string name, string id, Color tint, float smoothness)
        {
            var albedo = Load(id, "diff");
            if (albedo == null)
            {
                Debug.LogWarning($"[Oxfall] Texture {id} is missing; {name} not made.");
                return;
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;

            var material = LoadOrCreate(name, shader);
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            var normal = Load(id, "nor_gl");
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        /// <summary>The ground: grass, dirt, sand and rock photos blended by the game's splat map.</summary>
        private static void MakeGround()
        {
            var shader = Shader.Find("Oxfall/Ground");
            if (shader == null)
            {
                Debug.LogWarning("[Oxfall] Shader Oxfall/Ground not found; the ground keeps its colour map.");
                return;
            }
            var material = LoadOrCreate("OxfallTerrain", shader);
            SetLayer(material, "_Grass", "leafy_grass");
            SetLayer(material, "_Dirt", "brown_mud_leaves_01");
            SetLayer(material, "_Sand", "coast_sand_01");
            SetLayer(material, "_Rock", "rock_boulder_dry");
            // Metres per tile, about the real size of each photo.
            material.SetVector("_Tiling", new Vector4(3f, 2.5f, 3f, 4f));
            material.EnableKeyword("_GROUND_NORMALS");
            EditorUtility.SetDirty(material);
        }

        private static void SetLayer(Material material, string property, string id)
        {
            var albedo = Load(id, "diff");
            if (albedo != null) material.SetTexture(property, albedo);
            else Debug.LogWarning($"[Oxfall] Ground layer {id} is missing.");
            var normal = Load(id, "nor_gl");
            if (normal != null) material.SetTexture(property + "Normal", normal);
        }

        /// <summary>
        /// Leaves, needles or grass on cards: the Oxfall/Foliage shader (cut out by the alpha of a
        /// photo atlas, both faces drawn, swaying in the wind), or URP Lit with alpha clipping if that
        /// shader is missing. Saved as an asset so the build includes the shader and its variants.
        /// </summary>
        private static void MakeCutout(string name, string atlasPath, float smoothness, float wind)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{atlasPath}");
            if (atlas == null)
            {
                Debug.LogWarning($"[Oxfall] {atlasPath} missing; {name} not made, the plain look stays.");
                return;
            }
            var shader = Shader.Find("Oxfall/Foliage");
            if (shader != null)
            {
                var foliage = LoadOrCreate(name, shader);
                foliage.SetTexture("_BaseMap", atlas);
                foliage.SetColor("_BaseColor", Color.white);
                foliage.SetFloat("_Cutoff", 0.5f);
                foliage.SetFloat("_WindStrength", wind);
                foliage.SetFloat("_WindSpeed", 1f);
                foliage.SetFloat("_Translucency", 0.35f);
                foliage.renderQueue = (int)RenderQueue.AlphaTest;
                // The shader has no instancing variant; the SRP batcher batches it instead.
                foliage.enableInstancing = false;
                EditorUtility.SetDirty(foliage);
                return;
            }
            Debug.LogWarning("[Oxfall] Shader Oxfall/Foliage not found; foliage uses URP Lit without wind.");
            shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;

            var material = LoadOrCreate(name, shader);
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.doubleSidedGI = true;
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        /// <summary>The glow of torch and campfire flames (Oxfall/Flame, additive and unlit).</summary>
        private static void MakeFlame()
        {
            var shader = Shader.Find("Oxfall/Flame");
            if (shader == null)
            {
                Debug.LogWarning("[Oxfall] Shader Oxfall/Flame not found; flames use a plain colour.");
                return;
            }
            var material = LoadOrCreate("OxfallFlame", shader);
            material.SetColor("_CoreColor", new Color(1f, 0.82f, 0.42f));
            material.SetColor("_TipColor", new Color(0.95f, 0.28f, 0.06f));
            material.SetFloat("_Intensity", 1.6f);
            material.SetFloat("_Height", 0.26f);
            material.SetFloat("_Lick", 0.03f);
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The sea. Its ripple texture is made by the game at start-up.</summary>
        private static void MakeWater()
        {
            var shader = Shader.Find("Oxfall/Water");
            if (shader == null)
            {
                Debug.LogWarning("[Oxfall] Shader Oxfall/Water not found; the sea stays plain.");
                return;
            }
            var material = LoadOrCreate("OxfallWater", shader);
            // Set every value here: a material saved earlier keeps its old values even when the
            // shader's defaults change.
            material.SetColor("_DeepColor", new Color(0.04f, 0.15f, 0.19f));
            material.SetColor("_ShallowColor", new Color(0.12f, 0.38f, 0.36f));
            material.SetColor("_SandColor", new Color(0.58f, 0.52f, 0.40f));
            material.SetColor("_FoamColor", new Color(0.86f, 0.88f, 0.86f));
            // Longer, gentler ripples: the first ones read as a busy, repeating pattern.
            material.SetVector("_RippleTiling", new Vector4(14f, 37f, 0.025f, 0f));
            material.SetFloat("_RippleStrength", 0.35f);
            material.SetFloat("_Reflection", 0.6f);
            material.SetFloat("_Glint", 3f);
            EditorUtility.SetDirty(material);
        }
    }
}
