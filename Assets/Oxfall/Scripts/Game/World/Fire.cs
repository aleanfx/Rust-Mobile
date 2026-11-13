using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>
    /// A fire: a few flame shapes that lick on their own (Oxfall/Flame) and a warm point light that
    /// flickers. Torches and campfires are made of one.
    /// </summary>
    public sealed class Fire : MonoBehaviour
    {
        public static readonly Color LightColour = new Color(1f, 0.62f, 0.32f);

        private Light _light;
        private float _intensity;
        private float _seed;

        public Light Light => _light;

        /// <param name="flameScale">Size of the main flame; the flame shape is about 0.26 m tall at 1.</param>
        public static Fire Create(Transform parent, Vector3 localPosition, float flameScale, int flames, float range,
            float intensity, Mesh flameMesh, Material flameMaterial)
        {
            var go = new GameObject("Fire");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var fire = go.AddComponent<Fire>();

            for (int i = 0; i < flames; i++)
            {
                var flame = new GameObject("Flame");
                flame.transform.SetParent(go.transform, false);
                // The first flame in the middle, the others smaller around it, leaning out.
                float angle = i * 137.5f;
                var offset = i == 0 ? Vector3.zero : Quaternion.Euler(0f, angle, 0f) * new Vector3(0.09f * flameScale, 0f, 0f);
                flame.transform.localPosition = offset;
                flame.transform.localRotation = Quaternion.Euler(0f, angle, i == 0 ? 0f : 10f);
                flame.transform.localScale = Vector3.one * flameScale * (i == 0 ? 1f : 0.72f - 0.06f * i);
                flame.AddComponent<MeshFilter>().sharedMesh = flameMesh;
                var renderer = flame.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = flameMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var lightObject = new GameObject("Light");
            lightObject.transform.SetParent(go.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.2f * flameScale, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LightColour;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;

            fire._light = light;
            fire._intensity = intensity;
            fire._seed = Random.value * 100f;
            return fire;
        }

        private void Update()
        {
            // Two speeds of noise: a slow breathing and a quick flicker.
            float t = Time.time;
            float slow = Mathf.PerlinNoise(_seed, t * 1.3f);
            float quick = Mathf.PerlinNoise(_seed + 17f, t * 7f);
            _light.intensity = _intensity * (0.78f + 0.22f * slow + 0.18f * quick);
        }
    }
}
