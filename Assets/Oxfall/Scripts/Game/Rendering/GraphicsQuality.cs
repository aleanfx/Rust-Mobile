using UnityEngine;
using UnityEngine.Rendering;
#if OXFALL_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Oxfall.Game.Rendering
{
    public enum QualityTier
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>
    /// Three graphics levels, chosen automatically from the phone's memory and changeable in game.
    /// Low is for phones like the Redmi 12C (30 FPS target), Medium and High aim for 60.
    /// </summary>
    /// <remarks>
    /// The levels change the URP asset in memory at run time (render scale, shadows, MSAA, HDR) and
    /// switch the camera's post-processing; nothing is written to disk. The colour grading is the
    /// "semi-realistic" look: warm light, a little more contrast and colour, filmic tone mapping
    /// when the phone can afford HDR.
    /// </remarks>
    public sealed class GraphicsQuality
    {
        private const string PrefKey = "oxfall.quality";

        private readonly Camera _camera;
        private readonly Light _sun;
#if OXFALL_URP
        private Volume _volume;
        private Tonemapping _tonemapping;
        private ColorAdjustments _colour;
        private Bloom _bloom;
#endif

        public QualityTier Tier { get; private set; }

        public GraphicsQuality(Camera camera, Light sun)
        {
            _camera = camera;
            _sun = sun;
            BuildPostProcessing();
            int saved = PlayerPrefs.GetInt(PrefKey, -1);
            Apply(saved >= 0 && saved <= 2 ? (QualityTier)saved : Recommended());
        }

        public static string Name(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.Low: return "Bajo";
                case QualityTier.Medium: return "Medio";
                default: return "Alto";
            }
        }

        /// <summary>A first guess from total memory; the player can change it.</summary>
        public static QualityTier Recommended()
        {
            if (!Application.isMobilePlatform) return QualityTier.High;
            int megabytes = SystemInfo.systemMemorySize;
            if (megabytes <= 4600) return QualityTier.Low;
            if (megabytes <= 7000) return QualityTier.Medium;
            return QualityTier.High;
        }

        public void Next()
        {
            Apply((QualityTier)(((int)Tier + 1) % 3));
            PlayerPrefs.SetInt(PrefKey, (int)Tier);
            PlayerPrefs.Save();
        }

        public void Apply(QualityTier tier)
        {
            Tier = tier;

            // Always ask for 60: on the low tier the savings below are what make 30+ possible.
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            float fogEnd, far, shadowDistance, renderScale;
            int msaa;
            bool hdr, post;
            LightShadows shadows;
            switch (tier)
            {
                case QualityTier.Low:
                    renderScale = 0.75f; shadows = LightShadows.None; shadowDistance = 0f;
                    msaa = 1; hdr = false; post = false; fogEnd = 170f; far = 210f;
                    break;
                case QualityTier.Medium:
                    renderScale = 0.9f; shadows = LightShadows.Hard; shadowDistance = 35f;
                    msaa = 1; hdr = false; post = true; fogEnd = 230f; far = 270f;
                    break;
                default:
                    renderScale = 1f; shadows = LightShadows.Hard; shadowDistance = 60f;
                    msaa = 2; hdr = true; post = true; fogEnd = 300f; far = 340f;
                    break;
            }

            RenderSettings.fogEndDistance = fogEnd;
            _camera.farClipPlane = far;
            _sun.shadows = shadows;

#if OXFALL_URP
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = renderScale;
                urp.msaaSampleCount = msaa;
                urp.supportsHDR = hdr;
                if (shadowDistance > 0f) urp.shadowDistance = shadowDistance;
            }

            var cameraData = _camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = post;
            cameraData.renderShadows = shadows != LightShadows.None;

            if (_tonemapping != null)
            {
                // Filmic tone mapping only with HDR; without it, ACES just crushes the colours.
                _tonemapping.mode.Override(hdr ? TonemappingMode.ACES : TonemappingMode.None);
                _colour.postExposure.Override(hdr ? 0.35f : 0f);
                _colour.contrast.Override(hdr ? 14f : 8f);
                _colour.saturation.Override(hdr ? 12f : 8f);
                _bloom.active = tier == QualityTier.High;
            }
            if (_volume != null) _volume.enabled = post;
#endif
        }

        private void BuildPostProcessing()
        {
#if OXFALL_URP
            var go = new GameObject("Post-processing");
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _tonemapping = profile.Add<Tonemapping>();
            _colour = profile.Add<ColorAdjustments>();
            _colour.colorFilter.Override(new Color(1f, 0.97f, 0.92f));

            var white = profile.Add<WhiteBalance>();
            white.temperature.Override(8f);

            _bloom = profile.Add<Bloom>();
            _bloom.intensity.Override(0.35f);
            _bloom.threshold.Override(1.1f);
            _bloom.scatter.Override(0.6f);

            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.4f);

            _volume.sharedProfile = profile;
#endif
        }
    }
}
