using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if OXFALL_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Oxfall.Editor
{
    /// <summary>
    /// Configures the whole Unity project from code: render pipeline, player settings, the base
    /// materials and the scene. Nobody opens the editor by hand on this project, so every setting
    /// lives here, in source control, where it can be read and reviewed.
    /// </summary>
    /// <remarks>
    /// Safe to run any number of times. Throws on any failure so batch mode exits non-zero: a setup
    /// that fails quietly leaves a project that looks configured and is not.
    /// Run with <c>-executeMethod Oxfall.Editor.ProjectSetup.ApplyAndExit</c>.
    /// </remarks>
    public static class ProjectSetup
    {
        public const string ProductName = "Oxfall";
        public const string CompanyName = "Oxfall";

        /// <summary>
        /// The Android package id. Free to change until the first upload to Google Play; after that
        /// it is permanent (another id would install as a different app).
        /// </summary>
        public const string PackageId = "com.oxfall.survival";

        public const string Generated = "Assets/Oxfall/Generated";
        public const string RenderingFolder = Generated + "/Rendering";
        public const string ScenesFolder = Generated + "/Scenes";
        public const string ResourcesFolder = "Assets/Oxfall/Resources";

        public const string PipelinePath = RenderingFolder + "/OxfallPipeline.asset";
        public const string RendererPath = RenderingFolder + "/OxfallRenderer.asset";
        public const string SkyboxPath = RenderingFolder + "/OxfallSky.mat";
        public const string LitMaterialPath = ResourcesFolder + "/OxfallLit.mat";
        public const string ScenePath = ScenesFolder + "/Main.unity";

        [MenuItem("Oxfall/Apply project setup")]
        public static void Apply()
        {
            try
            {
                EnsureFolder(RenderingFolder);
                EnsureFolder(ScenesFolder);
                EnsureFolder(ResourcesFolder);

                ConfigurePipeline();
                ConfigurePlayer();
                ConfigureWebGL();
                ConfigureGraphicsStripping();
                var sky = CreateSkybox();
                CreateLitMaterial();
                CreateGroundMaterial();
                AssetDatabase.SaveAssets();
                BuildScene(sky);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[Oxfall] Project setup applied.");
            }
            catch (Exception error)
            {
                Debug.LogError($"[Oxfall] Project setup failed: {error}");
                throw;
            }
        }

        public static void ApplyAndExit()
        {
            try
            {
                Apply();
            }
            catch (Exception)
            {
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        // ---- render pipeline ------------------------------------------------------------------

        private static void ConfigurePipeline()
        {
#if OXFALL_URP
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            // Forward: one sun, no deferred-shaded geometry; the cheapest path on phone GPUs.
            renderer.renderingMode = RenderingMode.Forward;
            renderer.depthPrimingMode = DepthPrimingMode.Disabled;
            if (renderer.postProcessData == null)
            {
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                if (renderer.postProcessData == null)
                    Debug.LogWarning("[Oxfall] URP's PostProcessData was not found; post-processing will be off.");
            }
            EditorUtility.SetDirty(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            // Defaults for the High tier; GraphicsQuality lowers them at run time on weaker phones.
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 2;
            pipeline.renderScale = 1f;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.useSRPBatcher = true;
            pipeline.supportsDynamicBatching = false;
            pipeline.shadowDistance = 60f;
            pipeline.shadowCascadeCount = 1;
            pipeline.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;

            var serialized = new SerializedObject(pipeline);
            SetInt(serialized, "m_MainLightRenderingMode", (int)LightRenderingMode.PerPixel, true);
            SetInt(serialized, "m_AdditionalLightsRenderingMode", (int)LightRenderingMode.Disabled, true);
            SetBool(serialized, "m_MainLightShadowsSupported", true, true);
            SetBool(serialized, "m_SoftShadowsSupported", false, false);
            SetInt(serialized, "m_MainLightShadowmapResolution", 1024, false);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
#else
            throw new InvalidOperationException("URP (com.unity.render-pipelines.universal) is not installed.");
#endif
        }

        // ---- player ---------------------------------------------------------------------------

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "1.0")
                PlayerSettings.bundleVersion = "0.1.0";

            // IL2CPP and 64-bit ARM: required by Google Play, and faster than Mono.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Landscape only, either way up.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settings == null || settings.Length == 0)
                throw new InvalidOperationException("Could not open ProjectSettings/ProjectSettings.asset.");
            var player = new SerializedObject(settings[0]);
            // The game reads touches through the built-in Input Manager.
            SetInt(player, "activeInputHandler", 0, true);
            // The classic Activity entry point: the Input Manager's touches are proven there.
            SetInt(player, "androidApplicationEntry", 1, false);
            player.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The web build: the same game in a browser, so it can be played on an iPhone without the
        /// App Store and tested automatically in a headless browser.
        /// </summary>
        private static void ConfigureWebGL()
        {
            // Brotli: about a fifth smaller than gzip. GitHub Pages does not mark the files as
            // compressed, so the loader's decompression fallback unpacks them in JavaScript.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            // Assets/WebGLTemplates/Oxfall: full screen, touch-friendly, asks to turn the phone.
            PlayerSettings.WebGL.template = "PROJECT:Oxfall";
        }

        /// <summary>
        /// Fog is switched on from code at run time, so Unity cannot see it in the scene when it
        /// decides which shader variants to keep. Keep every fog mode explicitly.
        /// </summary>
        private static void ConfigureGraphicsStripping()
        {
            var graphics = GraphicsSettings.GetGraphicsSettings();
            if (graphics == null)
            {
                Debug.LogWarning("[Oxfall] Could not open GraphicsSettings; fog variants rely on the scene's fog.");
                return;
            }
            var so = new SerializedObject(graphics);
            SetInt(so, "m_FogStripping", 1, false);
            SetBool(so, "m_FogKeepLinear", true, false);
            SetBool(so, "m_FogKeepExp", true, false);
            SetBool(so, "m_FogKeepExp2", true, false);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- materials --------------------------------------------------------------------------

        private static void CreateLitMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("The URP Lit shader was not found.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(LitMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "OxfallLit" };
                AssetDatabase.CreateAsset(material, LitMaterialPath);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        public const string GroundMaterialPath = ResourcesFolder + "/OxfallGround.mat";
        public const string GroundDetailPath = RenderingFolder + "/GroundDetail.png";

        /// <summary>
        /// The ground's material: URP Lit with a tiling detail texture. The island's colour map has
        /// one texel per metre, which is a smooth blur underfoot; the detail texture multiplies fine
        /// grain over it so the ground has something to look at close up. It fades out with distance
        /// through its mipmaps.
        /// </summary>
        private static void CreateGroundMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("The URP Lit shader was not found.");

            if (!File.Exists(GroundDetailPath))
            {
                File.WriteAllBytes(GroundDetailPath, MakeDetailTexture(256).EncodeToPNG());
                AssetDatabase.ImportAsset(GroundDetailPath, ImportAssetOptions.ForceUpdate);
            }
            var importer = AssetImporter.GetAtPath(GroundDetailPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException($"Could not import {GroundDetailPath}.");
            // Linear, so mid-grey is exactly "no change" when the shader multiplies it in.
            if (importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Repeat || !importer.mipmapEnabled
                || importer.anisoLevel != 4)
            {
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundDetailPath);

            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "OxfallGround" };
                AssetDatabase.CreateAsset(material, GroundMaterialPath);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.04f);
            material.SetFloat("_Metallic", 0f);
            material.SetTexture("_DetailAlbedoMap", detail);
            // The ground's UVs run 0..1 across the whole island: 150 repeats is one tile every ~3 m.
            material.SetTextureScale("_DetailAlbedoMap", new Vector2(150f, 150f));
            material.SetFloat("_DetailAlbedoMapScale", 1f);
            material.EnableKeyword("_DETAIL_MULX2");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        /// <summary>Seamlessly tiling grey grain around mid-grey: blotches, clumps and fine speckle.</summary>
        private static Texture2D MakeDetailTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Blend four shifted copies so the left edge meets the right and the top the bottom.
                float u = (float)x / size, v = (float)y / size;
                float n = Grain(u, v) * (1f - u) * (1f - v) + Grain(u - 1f, v) * u * (1f - v)
                        + Grain(u - 1f, v - 1f) * u * v + Grain(u, v - 1f) * (1f - u) * v;
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt((0.5f + (n - 0.5f) * 0.9f) * 255f), 0, 255);
                pixels[y * size + x] = new Color32(g, g, g, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static float Grain(float u, float v)
        {
            float sum = 0f, amplitude = 0.5f, frequency = 5f, norm = 0f;
            for (int i = 0; i < 4; i++)
            {
                sum += Mathf.PerlinNoise(u * frequency + 137.3f, v * frequency + 111.7f) * amplitude;
                norm += amplitude;
                amplitude *= 0.62f;
                frequency *= 2.3f;
            }
            return sum / norm;
        }

        /// <summary>
        /// The sky material. <c>Oxfall/GradientSky</c> (Assets/Oxfall/Shaders) matches the fog at the
        /// horizon; its colours are set by the game at start-up. Unity's procedural sky is the fallback.
        /// </summary>
        private static Material CreateSkybox()
        {
            var shader = Shader.Find("Oxfall/GradientSky");
            bool gradient = shader != null;
            if (!gradient)
            {
                Debug.LogWarning("[Oxfall] Oxfall/GradientSky was not found; using Skybox/Procedural.");
                shader = Shader.Find("Skybox/Procedural");
            }
            if (shader == null) throw new InvalidOperationException("No sky shader was found.");

            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (sky == null)
            {
                sky = new Material(shader) { name = "OxfallSky" };
                AssetDatabase.CreateAsset(sky, SkyboxPath);
            }
            sky.shader = shader;
            if (!gradient)
            {
                sky.SetFloat("_SunDisk", 1f);
                sky.DisableKeyword("_SUNDISK_NONE");
                sky.DisableKeyword("_SUNDISK_HIGH_QUALITY");
                sky.EnableKeyword("_SUNDISK_SIMPLE");
                sky.SetFloat("_SunSize", 0.035f);
                sky.SetFloat("_SunSizeConvergence", 5f);
                sky.SetFloat("_AtmosphereThickness", 0.85f);
                sky.SetColor("_SkyTint", new Color(0.50f, 0.55f, 0.62f));
                sky.SetColor("_GroundColor", new Color(0.42f, 0.44f, 0.46f));
                sky.SetFloat("_Exposure", 1.2f);
            }
            EditorUtility.SetDirty(sky);
            Debug.Log($"[Oxfall] Sky shader: {shader.name}");
            return sky;
        }

        // ---- scene ------------------------------------------------------------------------------

        /// <summary>
        /// The one scene: empty but for its lighting settings. <c>OxfallGame</c> builds the world
        /// when it loads, so the scene holds no references to scripts that could break.
        /// </summary>
        private static void BuildScene(Material sky)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.64f, 0.76f);
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.52f, 0.49f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.23f, 0.19f);
            RenderSettings.fog = true;
            // The game sets the real fog at start-up; this only decides the scene's defaults.
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.81f, 0.77f, 0.69f);
            RenderSettings.fogDensity = 0.009f;

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException($"Unity could not save {ScenePath}.");

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ---- plumbing ---------------------------------------------------------------------------

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            var built = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }

        private static void SetInt(SerializedObject owner, string field, int value, bool required)
        {
            var property = Find(owner, field, required);
            if (property == null) return;
            // enumValueFlag is the enum's value; enumValueIndex would be its position in the declaration.
            if (property.propertyType == SerializedPropertyType.Enum) property.enumValueFlag = value;
            else property.intValue = value;
        }

        private static void SetBool(SerializedObject owner, string field, bool value, bool required)
        {
            var property = Find(owner, field, required);
            if (property != null) property.boolValue = value;
        }

        private static SerializedProperty Find(SerializedObject owner, string field, bool required)
        {
            var property = owner.FindProperty(field);
            if (property != null) return property;
            var message = $"'{field}' is not a serialised field of {owner.targetObject.GetType().Name} in this Unity version.";
            if (required) throw new InvalidOperationException(message);
            Debug.LogWarning("[Oxfall] " + message + " Left at its default.");
            return null;
        }
    }
}
