using System;
using Oxfall.Core.Environment;
using Oxfall.Game.UI;
using UnityEngine;

namespace Oxfall.Game.World
{
    /// <summary>
    /// Runs the clock and lights the island for the hour: the sun crosses the sky and warms near
    /// the horizon, the sky, fog and ambient light fade to a dark blue night, and the moon takes
    /// over the same light. Nights are dark enough that a torch or a campfire matters, but not so
    /// dark that a phone screen shows nothing.
    /// </summary>
    public sealed class DayNight : MonoBehaviour
    {
        // The direction the light comes from at noon, turned so the afternoon looks as the island
        // was first designed (sun from the south-west, long shadows).
        private const float BaseYaw = -52f;

        private static readonly Color DawnSun = new Color(1f, 0.62f, 0.38f);
        private static readonly Color MoonColour = new Color(0.60f, 0.70f, 1f);

        private static readonly Color DaySky = new Color(0.62f, 0.68f, 0.78f);
        private static readonly Color DayEquator = new Color(0.62f, 0.61f, 0.56f);
        private static readonly Color DayGround = new Color(0.32f, 0.30f, 0.25f);
        private static readonly Color NightSky = new Color(0.15f, 0.18f, 0.27f);
        private static readonly Color NightEquator = new Color(0.10f, 0.12f, 0.17f);
        private static readonly Color NightGround = new Color(0.05f, 0.05f, 0.065f);
        private static readonly Color DuskEquator = new Color(0.62f, 0.44f, 0.34f);

        private static readonly Color NightFog = new Color(0.055f, 0.07f, 0.11f);
        private static readonly Color DuskFog = new Color(0.80f, 0.62f, 0.50f);
        private static readonly Color NightSkyTop = new Color(0.012f, 0.02f, 0.05f);
        private static readonly Color NightBelow = new Color(0.03f, 0.035f, 0.05f);

        private static readonly int TopColor = Shader.PropertyToID("_TopColor");
        private static readonly int HorizonColor = Shader.PropertyToID("_HorizonColor");
        private static readonly int BottomColor = Shader.PropertyToID("_BottomColor");
        private static readonly int SunColor = Shader.PropertyToID("_SunColor");
        private static readonly int SunDirection = Shader.PropertyToID("_SunDirection");
        private static readonly int SkyTopGlobal = Shader.PropertyToID("_OxfallSkyTop");
        private static readonly int SkyHorizonGlobal = Shader.PropertyToID("_OxfallSkyHorizon");

        private DayCycle _cycle;
        private Light _sun;
        private Camera _camera;
        private Material _sky;
        private float _timer;
        private string _clock;

        public DayCycle Cycle => _cycle;

        /// <summary>Raised when the clock shows a new minute, with the time as the player reads it.</summary>
        public event Action<string> ClockChanged;

        public void Init(DayCycle cycle, Light sun, Camera camera)
        {
            _cycle = cycle;
            _sun = sun;
            _camera = camera;
            var sky = RenderSettings.skybox;
            _sky = sky != null && sky.HasProperty(HorizonColor) ? sky : null;
            Apply();
        }

        /// <summary>Jumps to an hour and relights at once.</summary>
        public void SetHour(float hours)
        {
            _cycle.Set(hours);
            Apply();
        }

        private void Update()
        {
            if (_cycle == null) return;
            _cycle.Tick(Time.deltaTime);
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            // Ten times a second is smooth enough: a minute of game time passes in about two seconds.
            _timer = 0.1f;
            Apply();
        }

        private void Apply()
        {
            var clock = _cycle.Clock;
            if (clock != _clock)
            {
                _clock = clock;
                ClockChanged?.Invoke((_cycle.IsNight ? "Noche " : "Día ") + clock);
            }
            // The item icons borrow the sun for one frame at start-up.
            if (ItemIcons.Busy || _sun == null) return;

            float h = _cycle.Hours;
            // 0 at night, 1 by day, fading over the last hour before sunset into the first 45 minutes
            // of night, and back around sunrise: the sky keeps some glow after the sun is down.
            float twilight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DayCycle.Sunrise - 0.75f, DayCycle.Sunrise + 1f, h))
                             * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DayCycle.Sunset - 1f, DayCycle.Sunset + 0.75f, h)));
            // Orange skies while the sun is low (and just after it sets).
            float low = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 22f, _cycle.SunElevation));
            float dusk = low * twilight;

            if (!_cycle.IsNight)
            {
                float yaw = BaseYaw + Mathf.Lerp(-100f, 100f, _cycle.DayProgress);
                _sun.transform.rotation = Quaternion.Euler(Mathf.Max(_cycle.SunElevation, 1.5f), yaw, 0f);
                _sun.color = Color.Lerp(WorldBuilder.SunColour, DawnSun, low);
                _sun.intensity = 1.8f * _cycle.Daylight;
            }
            else
            {
                float yaw = BaseYaw + 180f + Mathf.Lerp(-100f, 100f, _cycle.NightProgress);
                _sun.transform.rotation = Quaternion.Euler(Mathf.Max(_cycle.MoonElevation, 4f), yaw, 0f);
                _sun.color = MoonColour;
                _sun.intensity = 0.4f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 10f, _cycle.MoonElevation));
            }

            RenderSettings.ambientSkyColor = Color.Lerp(NightSky, DaySky, twilight);
            RenderSettings.ambientEquatorColor = Color.Lerp(Color.Lerp(NightEquator, DayEquator, twilight), DuskEquator, dusk * 0.45f);
            RenderSettings.ambientGroundColor = Color.Lerp(NightGround, DayGround, twilight);

            var fog = Color.Lerp(Color.Lerp(NightFog, WorldBuilder.FogColour, twilight), DuskFog, dusk * 0.6f);
            RenderSettings.fogColor = fog;
            if (_camera != null) _camera.backgroundColor = fog;

            var top = Color.Lerp(NightSkyTop, WorldBuilder.SkyTop, twilight);
            Shader.SetGlobalVector(SkyTopGlobal, top.linear);
            Shader.SetGlobalVector(SkyHorizonGlobal, fog.linear);
            if (_sky != null)
            {
                _sky.SetColor(TopColor, top);
                _sky.SetColor(HorizonColor, fog);
                _sky.SetColor(BottomColor, Color.Lerp(NightBelow, WorldBuilder.SkyBelowHorizon, twilight));
                // By night the disc in the sky is the moon.
                _sky.SetColor(SunColor, _cycle.IsNight ? new Color(0.78f, 0.82f, 0.9f) : _sun.color);
                _sky.SetVector(SunDirection, -_sun.transform.forward);
            }
        }
    }
}
