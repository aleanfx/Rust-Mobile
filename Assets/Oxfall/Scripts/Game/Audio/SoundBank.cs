using System;
using UnityEngine;

namespace Oxfall.Game.Audio
{
    /// <summary>
    /// Every sound in the game, synthesised at start-up from noise and tones: no audio files to
    /// license or download, and a few hundred kilobytes of memory. Good enough to give hits, steps
    /// and fire their feel; recorded sounds can replace any of them later under the same name.
    /// </summary>
    public sealed class SoundBank
    {
        public const int Rate = 22050;

        public AudioClip ChopWood { get; }
        public AudioClip HitStone { get; }
        public AudioClip Pick { get; }
        public AudioClip Swing { get; }
        public AudioClip[] StepsGrass { get; }
        public AudioClip[] StepsSand { get; }
        public AudioClip Place { get; }
        public AudioClip Crafted { get; }
        public AudioClip Eat { get; }
        public AudioClip Drink { get; }
        public AudioClip Tap { get; }
        public AudioClip KeyBeep { get; }
        public AudioClip Accepted { get; }
        public AudioClip Denied { get; }
        public AudioClip HitBody { get; }
        public AudioClip Hurt { get; }

        /// <summary>A wolf's low snarl, a boar's grunts, a deer's alarm bark.</summary>
        public AudioClip Growl { get; }
        public AudioClip Grunt { get; }
        public AudioClip Bark { get; }
        public AudioClip FireLoop { get; }
        public AudioClip WindLoop { get; }
        public AudioClip WavesLoop { get; }

        private readonly System.Random _random = new System.Random(5);

        public SoundBank()
        {
            ChopWood = Make("chop", 0.22f, (t, i) =>
                Thump(t, 120f, 0.05f) * 0.9f + Noise() * Env(t, 0.002f, 0.05f) * 0.45f + Tone(t, 420f) * Env(t, 0.001f, 0.03f) * 0.2f);
            HitStone = Make("stone", 0.2f, (t, i) =>
                Noise() * Env(t, 0.001f, 0.025f) * 0.7f + Tone(t, 1250f) * Env(t, 0.001f, 0.06f) * 0.25f
                + Tone(t, 1870f) * Env(t, 0.001f, 0.04f) * 0.15f + Thump(t, 90f, 0.04f) * 0.4f);
            Pick = Make("pick", 0.32f, (t, i) => Rustle(t, 0.32f) * 0.6f);
            Swing = Make("swing", 0.2f, (t, i) => Noise() * Mathf.Sin(Mathf.Clamp01(t / 0.2f) * Mathf.PI) * 0.12f);
            StepsGrass = new AudioClip[4];
            StepsSand = new AudioClip[4];
            for (int v = 0; v < 4; v++)
            {
                float pitch = 0.85f + v * 0.1f;
                StepsGrass[v] = Make("step grass " + v, 0.16f, (t, i) => Rustle(t * pitch, 0.16f) * 0.35f + Thump(t, 70f, 0.03f) * 0.25f);
                StepsSand[v] = Make("step sand " + v, 0.18f, (t, i) => Dark(0.25f) * Env(t * pitch, 0.01f, 0.07f) * 0.5f + Thump(t, 60f, 0.03f) * 0.2f);
            }
            Place = Make("place", 0.35f, (t, i) => Thump(t, 110f, 0.06f) * 0.8f + Thump(t - 0.11f, 140f, 0.05f) * 0.6f + Noise() * Env(t, 0.001f, 0.03f) * 0.2f);
            Crafted = Make("crafted", 0.5f, (t, i) => (Tone(t, 660f) * Env(t, 0.005f, 0.25f) + Tone(t - 0.09f, 880f) * Env(t - 0.09f, 0.005f, 0.3f)) * 0.25f);
            Eat = Make("eat", 0.4f, (t, i) => Dark(0.5f) * (Env(t, 0.002f, 0.04f) + Env(t - 0.12f, 0.002f, 0.04f) + Env(t - 0.25f, 0.002f, 0.05f)) * 0.6f);
            Drink = Make("drink", 0.6f, (t, i) => Gulp(t) * 0.5f + Gulp(t - 0.28f) * 0.45f);
            Tap = Make("tap", 0.05f, (t, i) => Tone(t, 1500f) * Env(t, 0.001f, 0.012f) * 0.25f);
            // The code lock: a short square-ish beep per key, two rising notes for a right code, and a
            // low buzz with the crackle of the shock for a wrong one.
            KeyBeep = Make("key beep", 0.07f, (t, i) => Mathf.Clamp(Tone(t, 2100f) * 2.2f, -1f, 1f) * Env(t, 0.001f, 0.02f) * 0.16f);
            Accepted = Make("accepted", 0.32f, (t, i) =>
                (Tone(t, 1320f) * Env(t, 0.002f, 0.05f) + Tone(t - 0.1f, 1760f) * Env(t - 0.1f, 0.002f, 0.09f)) * 0.22f);
            // A blow landing on someone: a dull, heavy knock with a slap on top; being hit: lower, longer.
            HitBody = Make("hit body", 0.25f, (t, i) =>
                Thump(t, 85f, 0.05f) * 0.95f + Dark(0.4f) * Env(t, 0.001f, 0.02f) * 0.5f + Tone(t, 300f) * Env(t, 0.001f, 0.015f) * 0.15f);
            Hurt = Make("hurt", 0.4f, (t, i) =>
                Thump(t, 60f, 0.09f) * 0.9f + Dark(0.6f) * Env(t, 0.003f, 0.06f) * 0.45f);
            Denied = Make("denied", 0.45f, (t, i) =>
                Mathf.Clamp(Tone(t, 180f) * 3f, -1f, 1f) * Env(t, 0.004f, 0.16f) * 0.2f
                + Noise() * Env(t, 0.001f, 0.05f) * (0.5f + 0.5f * Mathf.Sign(Tone(t, 37f))) * 0.3f);
            // A rasping, rumbling snarl that swells and fades.
            Growl = Make("growl", 1.3f, (t, i) =>
            {
                float swell = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((1.3f - t) / 0.4f);
                float rasp = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 27f * t + Mathf.Sin(t * 13f) * 2f);
                float voice = Tone(t, 82f + 9f * Mathf.Sin(t * 17f)) * 0.5f + Tone(t, 164f + 14f * Mathf.Sin(t * 11f)) * 0.25f;
                return (Dark(0.35f) * 0.8f + voice) * rasp * swell;
            });
            // Two snorting grunts.
            Grunt = Make("grunt", 0.55f, (t, i) =>
            {
                float g = 0f;
                foreach (float at in new[] { 0f, 0.24f })
                {
                    float u = t - at;
                    if (u < 0f) continue;
                    float pitch = 105f - u * 120f;
                    g += (Tone(u, pitch) + 0.6f * Tone(u, pitch * 2f) + 0.35f * Tone(u, pitch * 3f)) * Env(u, 0.008f, 0.07f);
                }
                return g * 0.5f + Dark(0.5f) * Env(t, 0.005f, 0.2f) * 0.35f;
            });
            // A single hoarse cough of alarm.
            Bark = Make("bark", 0.35f, (t, i) =>
                (Tone(t, 420f - t * 500f) * 0.5f + Tone(t, 840f - t * 900f) * 0.2f) * Env(t, 0.006f, 0.06f)
                + Noise() * Env(t, 0.002f, 0.05f) * 0.4f);
            FireLoop = MakeLoop("fire", 3f, FireSample);
            WindLoop = MakeLoop("wind", 6f, WindSample);
            WavesLoop = MakeLoop("waves", 8f, WavesSample);
        }

        // ---- building blocks ----

        private float Noise() => (float)(_random.NextDouble() * 2.0 - 1.0);

        private static float Tone(float t, float hz) => t < 0f ? 0f : Mathf.Sin(2f * Mathf.PI * hz * t);

        /// <summary>Fast attack, exponential decay; 0 before the start.</summary>
        private static float Env(float t, float attack, float decay)
        {
            if (t < 0f) return 0f;
            float a = attack > 0f ? Mathf.Clamp01(t / attack) : 1f;
            return a * Mathf.Exp(-Mathf.Max(0f, t - attack) / decay);
        }

        /// <summary>A low knock whose pitch drops as it dies, like a blow on wood or earth.</summary>
        private static float Thump(float t, float hz, float decay)
        {
            if (t < 0f) return 0f;
            float f = hz * (1f + 1.5f * Mathf.Exp(-t / 0.01f));
            return Mathf.Sin(2f * Mathf.PI * f * t) * Env(t, 0.001f, decay);
        }

        private float _low;
        /// <summary>Noise through a one-pole low-pass: the smaller the amount, the darker.</summary>
        private float Dark(float amount)
        {
            _low += (Noise() - _low) * amount;
            return _low * 2f;
        }

        private float _band;
        /// <summary>Leaves and grass brushing: crackly noise in short bursts under an envelope.</summary>
        private float Rustle(float t, float length)
        {
            if (t < 0f || t > length) return 0f;
            _band += (Noise() - _band) * 0.6f;
            float bursts = 0.5f + 0.5f * Mathf.Sin(t * 90f + Mathf.Sin(t * 37f) * 3f);
            float shape = Mathf.Sin(t / length * Mathf.PI);
            return (Noise() - _band) * bursts * shape;
        }

        /// <summary>A swallow: a quick falling tone with a soft knock.</summary>
        private static float Gulp(float t)
        {
            if (t < 0f || t > 0.22f) return 0f;
            float f = 420f - 900f * t;
            return Mathf.Sin(2f * Mathf.PI * f * t) * Env(t, 0.01f, 0.06f) + Thump(t, 80f, 0.03f) * 0.4f;
        }

        // ---- loops ----

        private float _rumble;
        private float FireSample(float t, int i)
        {
            // A low roar with pops and crackles at random.
            _rumble += (Noise() - _rumble) * 0.03f;
            float sample = _rumble * 0.6f;
            if (_random.NextDouble() < 0.0009) _pop = 1f;
            if (_random.NextDouble() < 0.006) _crackle = 0.6f;
            sample += Noise() * _pop * 0.9f + Noise() * _crackle * 0.35f;
            _pop *= 0.985f;
            _crackle *= 0.9f;
            return sample;
        }
        private float _pop, _crackle;

        private float _wind;
        private float WindSample(float t, int i)
        {
            // Dark noise swelling slowly in gusts.
            _wind += (Noise() - _wind) * 0.02f;
            float gust = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * t / 6f) * Mathf.Sin(2f * Mathf.PI * t / 3f + 1f);
            return _wind * gust * 2.2f;
        }

        private float _surf;
        private float WavesSample(float t, int i)
        {
            // Two waves per loop: a rising wash, then a long hiss as it draws back.
            _surf += (Noise() - _surf) * 0.12f;
            float phase = (t / 4f) % 1f;
            float swell = phase < 0.35f ? phase / 0.35f : Mathf.Exp(-(phase - 0.35f) * 4f);
            return (_surf * 0.8f + Noise() * 0.15f) * (0.15f + swell * 0.85f);
        }

        // ---- clips ----

        private AudioClip Make(string name, float seconds, Func<float, int, float> sample)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = sample(i / (float)Rate, i);
            Normalise(data, 0.9f);
            // A few milliseconds of fade at the end, so nothing clicks off.
            int fade = Math.Min(count, Rate / 200);
            for (int i = 0; i < fade; i++) data[count - 1 - i] *= i / (float)fade;
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A seamless loop: the end is cross-faded into the start.</summary>
        private AudioClip MakeLoop(string name, float seconds, Func<float, int, float> sample)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            int overlap = Rate / 2;
            var raw = new float[count + overlap];
            for (int i = 0; i < raw.Length; i++) raw[i] = sample(i / (float)Rate, i);
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = raw[i];
            for (int i = 0; i < overlap; i++)
            {
                float w = i / (float)overlap;
                data[i] = raw[i] * w + raw[count + i] * (1f - w);
            }
            Normalise(data, 0.8f);
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void Normalise(float[] data, float peak)
        {
            float max = 0f;
            foreach (float s in data) max = Mathf.Max(max, Mathf.Abs(s));
            if (max < 1e-5f) return;
            float gain = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
        }
    }
}
