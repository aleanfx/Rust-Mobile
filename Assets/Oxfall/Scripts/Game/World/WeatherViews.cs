using System.Collections;
using Oxfall.Core.Environment;
using Oxfall.Game.Audio;
using Oxfall.Game.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.World
{
    /// <summary>
    /// The weather as the player sees and hears it: rain streaking round the camera, a greyer, dimmer
    /// sky under cloud, thicker fog, the rain's hiss and, in a storm, lightning and thunder rolling in
    /// a few seconds later. It eases towards what it is told (by the server, or alone by
    /// <see cref="Weather"/>) over half a minute or so, so showers come and go rather than switch.
    /// </summary>
    public sealed class WeatherViews : MonoBehaviour
    {
        private const float Ease = 1f / 35f;
        private const int Streaks = 2400;
        private const float Box = 24f;

        private static readonly int Amount = Shader.PropertyToID("_Amount");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int RainTime = Shader.PropertyToID("_OxfallRainTime");

        private DayNight _dayNight;
        private GameAudio _audio;
        private Material _material;
        private MeshRenderer _renderer;
        private float _targetRain, _targetFog;
        private float? _forcedRain, _forcedFog;
        private float _nextThunder;
        private float _flash;
        private float _streakShare = 1f;

        /// <summary>How hard it rains and how thick the fog is now (0..1 each), as shown.</summary>
        public float Rain { get; private set; }
        public float Fog { get; private set; }

        public static WeatherViews Create(MaterialLibrary materials, DayNight dayNight, GameAudio audio)
        {
            var go = new GameObject("Weather");
            var w = go.AddComponent<WeatherViews>();
            w._dayNight = dayNight;
            w._audio = audio;
            if (materials.Rain != null)
            {
                w._material = materials.Rain;
                go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.RainStreaks(Streaks, Box, 11);
                w._renderer = go.AddComponent<MeshRenderer>();
                w._renderer.sharedMaterial = w._material;
                w._renderer.shadowCastingMode = ShadowCastingMode.Off;
                w._renderer.receiveShadows = false;
                w._renderer.enabled = false;
            }
            w._nextThunder = Time.time + 20f;
            return w;
        }

        /// <summary>The weather to ease towards (0..1 each).</summary>
        public void SetTarget(float rain, float fog)
        {
            _targetRain = Mathf.Clamp01(rain);
            _targetFog = Mathf.Clamp01(fog);
        }

        /// <summary>Tests: holds the weather at these values (null lets it go back to the forecast), at once.</summary>
        public void Force(float? rain, float? fog)
        {
            _forcedRain = rain;
            _forcedFog = fog;
            if (rain.HasValue) Rain = rain.Value;
            if (fog.HasValue) Fog = fog.Value;
        }

        /// <summary>Fewer streaks on a slower phone (1: all of them).</summary>
        public void SetDensity(float share) => _streakShare = Mathf.Clamp(share, 0.2f, 1f);

        private void Update()
        {
            float dt = Time.deltaTime;
            Rain = Mathf.MoveTowards(Rain, _forcedRain ?? _targetRain, Ease * dt);
            Fog = Mathf.MoveTowards(Fog, _forcedFog ?? _targetFog, Ease * dt);

            if (_renderer != null)
            {
                bool raining = Rain > 0.01f;
                if (_renderer.enabled != raining) _renderer.enabled = raining;
                if (raining)
                {
                    _material.SetFloat(Amount, Rain * _streakShare);
                    // Lit like the air around it: pale by day, dark by night, bright in a flash.
                    var c = RenderSettings.fogColor * 1.25f + new Color(0.06f, 0.06f, 0.07f);
                    c.a = 0.22f + 0.16f * Rain;
                    _material.SetColor(ColorId, c);
                    Shader.SetGlobalFloat(RainTime, Time.time % 600f);
                }
            }

            if (_dayNight != null)
            {
                _dayNight.Overcast = Mathf.Max(Rain, Fog * 0.55f);
                _dayNight.FogBoost = Fog;
                if (_flash > 0f || _dayNight.Flash > 0f)
                {
                    _flash = Mathf.Max(0f, _flash - dt * 6f);
                    _dayNight.Flash = _flash;
                    _dayNight.Relight();
                }
            }
            if (_audio != null) _audio.Rain = Rain;

            // A storm: lightning now and then, its thunder after it (the further, the later and quieter).
            if (Rain >= Weather.Storm && Time.time >= _nextThunder)
            {
                _nextThunder = Time.time + Random.Range(14f, 40f);
                float distance = Random.Range(0.3f, 1f);
                _flash = Mathf.Lerp(1f, 0.35f, distance);
                StartCoroutine(Thunder(distance * 4.5f, Mathf.Lerp(1f, 0.45f, distance)));
            }
        }

        private IEnumerator Thunder(float delay, float volume)
        {
            yield return new WaitForSeconds(delay);
            _audio?.PlayThunder(volume);
        }
    }
}
