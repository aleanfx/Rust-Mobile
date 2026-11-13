using UnityEditor;
using UnityEngine;

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
            MakeGround();
            MakeWater();
            AssetDatabase.SaveAssets();
            Debug.Log("[Oxfall] Art materials made from the CC0 textures.");
        }

        /// <summary>512 px, tiling, mipmapped and compressed; "_nor_gl" files are normal maps.</summary>
        private static void ConfigureImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool normal = path.Contains("_nor_gl");
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType == type && importer.maxTextureSize == 512 && importer.mipmapEnabled
                    && importer.wrapMode == TextureWrapMode.Repeat && importer.anisoLevel == 4
                    && importer.sRGBTexture == !normal)
                    continue;

                importer.textureType = type;
                importer.sRGBTexture = !normal;
                importer.maxTextureSize = 512;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
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
            EditorUtility.SetDirty(material);
        }
    }
}
