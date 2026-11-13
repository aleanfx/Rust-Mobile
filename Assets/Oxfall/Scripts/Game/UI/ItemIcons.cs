using System;
using System.Collections;
using System.Collections.Generic;
using Oxfall.Game.Items;
using UnityEngine;
using UnityEngine.Rendering;
#if OXFALL_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Oxfall.Game.UI
{
    /// <summary>
    /// Inventory icons, rendered at start-up from the same 3D models the player holds: each item is
    /// posed far below the island in front of its own small camera for one frame, and the picture
    /// is kept as a sprite. No icon art to draw or import, and icons always match the models.
    /// </summary>
    public sealed class ItemIcons : MonoBehaviour
    {
        /// <summary>A layer nothing else uses, so the icon cameras see only the icon models.</summary>
        public const int Layer = 31;

        public const int Size = 160;

        // Cleared to magenta: if a device drops the alpha channel, the background is still known.
        private static readonly Color Key = new Color(1f, 0f, 1f, 0f);

        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        /// <summary>Raised once the icons are ready.</summary>
        public event Action Ready;

        /// <summary>True for the frame the icons borrow the sun; the day-night cycle leaves the light alone then.</summary>
        public static bool Busy { get; private set; }

        /// <summary>The icon of an item, or null before they are rendered (or if it failed).</summary>
        public static Sprite Get(string itemId) => itemId != null && Sprites.TryGetValue(itemId, out var s) ? s : null;

        public static ItemIcons Render(IReadOnlyDictionary<string, ItemModel> models, Light sun)
        {
            var icons = new GameObject("Item icons").AddComponent<ItemIcons>();
            icons.StartCoroutine(icons.Run(models, sun));
            return icons;
        }

        private sealed class Job
        {
            public string Id;
            public GameObject Model;
            public Camera Camera;
            public RenderTexture Target;
        }

        private IEnumerator Run(IReadOnlyDictionary<string, ItemModel> models, Light sun)
        {
            Busy = true;
            var jobs = new List<Job>();
            int n = 0;
            foreach (var pair in models)
            {
                var job = new Job { Id = pair.Key };
                var model = pair.Value;
                job.Model = new GameObject("Icon " + pair.Key) { layer = Layer };
                job.Model.transform.SetParent(transform, false);
                job.Model.transform.SetPositionAndRotation(new Vector3(n++ * 20f, -2000f, 0f), model.IconRotation);
                job.Model.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
                var renderer = job.Model.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = model.Materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var bounds = renderer.bounds;

                job.Target = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32) { name = "Icon " + pair.Key };
                job.Target.Create();

                var cameraObject = new GameObject("Icon camera " + pair.Key);
                cameraObject.transform.SetParent(transform, false);
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.08f / Mathf.Max(0.5f, model.IconZoom);
                camera.transform.SetPositionAndRotation(bounds.center - Vector3.forward * (bounds.extents.z + 1f), Quaternion.identity);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = bounds.extents.z * 2f + 2f;
                camera.cullingMask = 1 << Layer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Key;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = false;
                camera.depth = -50;
                camera.targetTexture = job.Target;
#if OXFALL_URP
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderShadows = false;
                data.renderPostProcessing = false;
                data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
#endif
                job.Camera = camera;
                jobs.Add(job);
            }

            // A studio light for the one frame: from above, left and behind the icon cameras, so
            // icons look the same whatever the time of day.
            Quaternion sunRotation = default;
            float sunIntensity = 0f;
            Color sunColour = Color.white;
            if (sun != null)
            {
                sunRotation = sun.transform.rotation;
                sunIntensity = sun.intensity;
                sunColour = sun.color;
                sun.transform.rotation = Quaternion.LookRotation(new Vector3(0.45f, -0.6f, 0.65f));
                sun.intensity = 2.1f;
                sun.color = new Color(1f, 0.95f, 0.88f);
            }
            var ambientSky = RenderSettings.ambientSkyColor;
            var ambientEquator = RenderSettings.ambientEquatorColor;
            var ambientGround = RenderSettings.ambientGroundColor;
            // Brighter than any real hour: icons sit on dark slots and must read at a glance.
            RenderSettings.ambientSkyColor = new Color(0.75f, 0.78f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.70f, 0.68f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.42f, 0.38f);
#if OXFALL_URP
            // A lower render scale (the Low graphics tier) would blur the icons; full size for this frame.
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float renderScale = urp != null ? urp.renderScale : 1f;
            if (urp != null) urp.renderScale = 1f;
#endif

            // The cameras render at the end of this frame; read the pictures back on the next.
            yield return null;

            var previous = RenderTexture.active;
            foreach (var job in jobs)
            {
                try
                {
                    RenderTexture.active = job.Target;
                    var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Icon " + job.Id };
                    texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                    CutOut(texture);
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Trilinear;
                    texture.Apply(true, true);
                    Sprites[job.Id] = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Oxfall] Icon for {job.Id} failed: {e.Message}");
                }
            }
            RenderTexture.active = previous;

            if (sun != null)
            {
                sun.transform.rotation = sunRotation;
                sun.intensity = sunIntensity;
                sun.color = sunColour;
            }
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
#if OXFALL_URP
            if (urp != null) urp.renderScale = renderScale;
#endif
            foreach (var job in jobs)
            {
                job.Camera.targetTexture = null;
                job.Target.Release();
                Destroy(job.Target);
            }

            Busy = false;
            Debug.Log($"[Oxfall] Rendered {Sprites.Count} item icons.");
            Ready?.Invoke();
            Destroy(gameObject);
        }

        /// <summary>
        /// Makes the background see-through (whether or not the alpha survived the render) and gives
        /// the item a soft dark outline so it reads on any slot colour.
        /// </summary>
        private static void CutOut(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            int size = texture.width;
            var solid = new bool[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                bool background = p.a < 128 || (p.r > 230 && p.g < 40 && p.b > 230);
                solid[i] = !background;
                pixels[i] = background ? new Color32(16, 12, 10, 0) : new Color32(p.r, p.g, p.b, 255);
            }
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                if (solid[i]) continue;
                int near = 0;
                for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int sx = x + dx, sy = y + dy;
                    if (sx < 0 || sy < 0 || sx >= size || sy >= size) continue;
                    if (solid[sy * size + sx]) near++;
                }
                if (near > 0) pixels[i].a = (byte)Mathf.Min(150, 40 + near * 12);
            }
            texture.SetPixels32(pixels);
        }
    }
}
