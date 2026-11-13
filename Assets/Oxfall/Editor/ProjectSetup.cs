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
                ConfigureGraphicsStripping();
                var sky = CreateSkybox();
                CreateLitMaterial();
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

        private static Material CreateSkybox()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) throw new InvalidOperationException("The Skybox/Procedural shader was not found.");

            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (sky == null)
            {
                sky = new Material(shader) { name = "OxfallSky" };
                AssetDatabase.CreateAsset(sky, SkyboxPath);
            }
            sky.shader = shader;
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
            EditorUtility.SetDirty(sky);
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
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.68f, 0.75f, 0.80f);
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 260f;

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
