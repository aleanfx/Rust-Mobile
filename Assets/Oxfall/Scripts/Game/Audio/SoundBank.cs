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
        /// <summary>Cloth unrolled, then torn: a bandage (or a syringe's quick hiss).</summary>
        public AudioClip Bandage { get; }
        public AudioClip Drink { get; }
        public AudioClip Tap { get; }
        public AudioClip KeyBeep { get; }
        public AudioClip Accepted { get; }
        public AudioClip Denied { get; }
        public AudioClip HitBody { get; }
        public AudioClip Hurt { get; }

        /// <summary>A wolf's low snarl, a boar's grunts, a deer's alarm bark.</summary>
        public AudioClip Growl { get; }

        /// <summary>A bow creaking as it is drawn, and the string's thrum and the arrow's whoosh as it is loosed.</summary>
        public AudioClip BowDraw { get; }
        public AudioClip BowShot { get; }
        public AudioClip HitMetal { get; }
        public AudioClip WeakSpot { get; }
        public AudioClip Headshot { get; }
        public AudioClip Grunt { get; }
        public AudioClip Bark { get; }
        public AudioClip FireLoop { get; }

        /// <summary>A satchel charge going off: a sharp crack, a deep boom that rolls on, debris pattering down.</summary>
        public AudioClip Explosion { get; }

        /// <summary>A lit fuse: a hiss full of spitting sparks (a loop).</summary>
        public AudioClip FuseLoop { get; }

        /// <summary>Guns: the revolver's sharp crack, the shotgun's deep blast, a reload's clicks, and the click of an empty one.</summary>
        public AudioClip RevolverShot { get; }
        public AudioClip ShotgunShot { get; }
        public AudioClip RifleShot { get; }
        public AudioClip GunReload { get; }
        public AudioClip DryFire { get; }
        public AudioClip WindLoop { get; }
        public AudioClip WavesLoop { get; }

        /// <summary>A recycler running: a motor's hum, the drum's grinding rasp and a clank each turn (a loop).</summary>
        public AudioClip MachineLoop { get; }
        /// <summary>A siren's wail, rising and falling (loops).</summary>
        public AudioClip SirenLoop { get; }
        /// <summary>A cargo plane's four engines droning, heard from far off (loops).</summary>
        public AudioClip PlaneLoop { get; }

        /// <summary>The patrol helicopter: its blades' heavy thudding over a turbine's whine (loops), a round of its gun, a rocket's rush.</summary>
        public AudioClip RotorLoop { get; }
        public AudioClip HeliGun { get; }
        public AudioClip RocketWhoosh { get; }

        /// <summary>An outboard motor's two-stroke putter (a loop; its pitch rises with the boat's speed).</summary>
        public AudioClip EngineLoop { get; }

        /// <summary>A bandit's radio: the squelch as he keys it, a burst of crackly, muffled voice, a beep as he lets go.</summary>
        public AudioClip Radio { get; }

        /// <summary>Birds by day: a few different calls (a whistle, a trill, a chirp run), one now and then from the trees.</summary>
        public AudioClip[] Birds { get; }

        /// <summary>A wolf's howl: a rising, wavering cry that holds and falls away.</summary>
        public AudioClip Howl { get; }

        /// <summary>Crickets at night (a loop).</summary>
        public AudioClip CricketsLoop { get; }

        /// <summary>Rain falling all round (a loop) and a roll of thunder far off.</summary>
        public AudioClip RainLoop { get; }
        public AudioClip Thunder { get; }

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
            Bandage = Make("bandage", 0.75f, (t, i) => Rustle(t, 0.4f) * 0.45f
                + Noise() * Env(t - 0.42f, 0.004f, 0.11f) * 0.4f + Noise() * Env(t - 0.5f, 0.004f, 0.08f) * 0.25f);
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
            BowDraw = Make("bow draw", 0.6f, (t, i) =>
                Dark(0.25f) * (0.4f + 0.6f * Mathf.Abs(Mathf.Sin(t * 70f))) * Mathf.Clamp01(t / 0.15f) * Mathf.Clamp01((0.6f - t) / 0.15f) * 0.5f
                + Tone(t, 95f + t * 60f) * Env(t, 0.05f, 0.4f) * 0.08f);
            BowShot = Make("bow shot", 0.5f, (t, i) =>
                (Tone(t, 110f) + 0.5f * Tone(t, 220f) + 0.25f * Tone(t, 330f)) * Env(t, 0.002f, 0.09f) * 0.6f
                + Thump(t, 70f, 0.05f) * 0.5f
                + Noise() * Env(t - 0.02f, 0.03f, 0.12f) * 0.35f);
            // A hollow steel drum struck: a dull boom under a ring of inharmonic partials.
            HitMetal = Make("metal", 0.6f, (t, i) =>
                Thump(t, 85f, 0.08f) * 0.55f + Noise() * Env(t, 0.001f, 0.02f) * 0.35f
                + (Tone(t, 310f) * 0.35f + Tone(t, 523f) * 0.22f + Tone(t, 887f) * 0.14f + Tone(t, 1391f) * 0.08f) * Env(t, 0.001f, 0.22f));
            // The weak spot: a sharp crack that splits, with a bright ring over the knock.
            WeakSpot = Make("weak spot", 0.35f, (t, i) =>
                Thump(t, 140f, 0.05f) * 0.8f + Noise() * Env(t, 0.0005f, 0.018f) * 0.8f
                + (Tone(t, 1480f) * 0.3f + Tone(t, 2220f) * 0.18f) * Env(t, 0.001f, 0.09f));
            // A head hit: a short, high, dull 'tink' that cuts through the shot.
            Headshot = Make("headshot", 0.3f, (t, i) =>
                (Tone(t, 1960f) * 0.4f + Tone(t, 2940f) * 0.2f + Tone(t, 3920f) * 0.1f) * Env(t, 0.0008f, 0.07f)
                + Thump(t, 110f, 0.03f) * 0.4f);
            FireLoop = MakeLoop("fire", 3f, FireSample);
            var falls = new float[14];
            for (int k = 0; k < falls.Length; k++) falls[k] = 0.35f + (float)_random.NextDouble() * 1.5f;
            Explosion = Make("explosion", 2.8f, (t, i) =>
            {
                float crack = Noise() * Env(t, 0.0005f, 0.025f) * 1.6f;
                float boom = Thump(t, 48f, 0.42f) * 1.3f + Tone(t, 31f) * Env(t, 0.004f, 0.6f) * 0.8f;
                float roll = Dark(0.08f) * Env(t, 0.01f, 0.9f) * 1.6f;
                float debris = 0f;
                foreach (float at in falls) debris += Noise() * Env(t - at, 0.0008f, 0.018f) * 0.22f;
                float s = crack + boom + roll + debris;
                // Driven into soft clipping: loud and torn, as a blast should sound up close.
                return (float)Math.Tanh(s * 2.2f);
            });
            FuseLoop = MakeLoop("fuse", 1.6f, FuseSample);
            RevolverShot = Make("revolver", 1.1f, (t, i) =>
            {
                float crack = Noise() * Env(t, 0.0002f, 0.009f) * 1.8f;
                float body = Thump(t, 140f, 0.045f) * 0.9f + Thump(t, 62f, 0.16f) * 0.7f;
                // The report off the hills behind.
                float tail = Dark(0.12f) * Env(t, 0.004f, 0.32f) * 0.55f + Dark(0.05f) * Env(t - 0.18f, 0.02f, 0.3f) * 0.3f;
                return (float)Math.Tanh((crack + body + tail) * 2.4f);
            });
            // The rifle: a sharper crack and a longer, rolling report.
            RifleShot = Make("rifle", 1.6f, (t, i) =>
            {
                float crack = Noise() * Env(t, 0.0001f, 0.006f) * 2.1f;
                float body = Thump(t, 180f, 0.035f) * 0.8f + Thump(t, 75f, 0.12f) * 0.8f;
                float tail = Dark(0.14f) * Env(t, 0.003f, 0.5f) * 0.6f + Dark(0.05f) * Env(t - 0.25f, 0.03f, 0.55f) * 0.38f;
                return (float)Math.Tanh((crack + body + tail) * 2.5f);
            });
            ShotgunShot = Make("shotgun", 1.5f, (t, i) =>
            {
                float crack = Noise() * Env(t, 0.0003f, 0.016f) * 1.7f;
                float boom = Thump(t, 52f, 0.3f) * 1.3f + Thump(t, 95f, 0.07f) * 0.8f;
                float tail = Dark(0.09f) * Env(t, 0.006f, 0.45f) * 0.9f + Dark(0.04f) * Env(t - 0.22f, 0.03f, 0.4f) * 0.35f;
                return (float)Math.Tanh((crack + boom + tail) * 2.6f);
            });
            // Steel on steel: the gate opening, rounds going in, the gate shut and the hammer cocked.
            var clicks = new[] { 0.04f, 0.42f, 0.7f, 0.98f, 1.4f, 1.62f };
            GunReload = Make("gun reload", 1.9f, (t, i) =>
            {
                float s = 0f;
                for (int k = 0; k < clicks.Length; k++)
                {
                    float u = t - clicks[k];
                    float pitch = k == 0 || k >= 4 ? 1f : 0.75f;
                    s += (Tone(u, 2650f * pitch) * 0.6f + Tone(u, 4170f * pitch) * 0.35f + Tone(u, 6900f * pitch) * 0.2f) * Env(u, 0.0004f, 0.012f)
                         + Noise() * Env(u, 0.0002f, 0.005f) * 0.5f;
                }
                return s;
            });
            DryFire = Make("dry fire", 0.12f, (t, i) =>
                (Tone(t, 3100f) * 0.6f + Tone(t, 5300f) * 0.3f) * Env(t, 0.0003f, 0.01f) + Noise() * Env(t, 0.0002f, 0.004f) * 0.5f);
            WindLoop = MakeLoop("wind", 6f, WindSample);
            WavesLoop = MakeLoop("waves", 8f, WavesSample);
            MachineLoop = MakeLoop("machine", 2f, MachineSample);
            SirenLoop = MakeLoop("siren", 2f, SirenSample);
            PlaneLoop = MakeLoop("plane", 4f, PlaneSample);
            RotorLoop = MakeLoop("rotor", 2f, RotorSample);
            HeliGun = Make("heli gun", 0.32f, (t, i) =>
                Noise() * Env(t, 0.0001f, 0.008f) * 1.4f + Thump(t, 140f, 0.03f) * 0.9f + Dark(0.2f) * Env(t, 0.002f, 0.12f) * 0.5f);
            RocketWhoosh = Make("rocket", 1.4f, (t, i) =>
            {
                // A pop as it leaves the tube, then a roaring hiss that fades as it goes.
                _rush += (Noise() - _rush) * 0.35f;
                return Thump(t, 90f, 0.05f) * 0.8f + _rush * Env(t, 0.03f, 0.9f) * 1.1f + Noise() * Env(t, 0.01f, 0.5f) * 0.25f;
            });
            EngineLoop = MakeLoop("engine", 2f, EngineSample);
            RainLoop = MakeLoop("rain", 5f, RainSample);
            Thunder = Make("thunder", 7f, ThunderSample);
            Radio = Make("radio", 1.25f, RadioSample);
            Birds = new[]
            {
                // A falling two-note whistle.
                Make("bird whistle", 0.7f, (t, i) => Chirp(t, 3400f, 2900f, 0.18f) + Chirp(t - 0.28f, 3000f, 2300f, 0.3f)),
                // A quick trill.
                Make("bird trill", 0.8f, (t, i) =>
                {
                    float sum = 0f;
                    for (int k = 0; k < 9; k++) sum += Chirp(t - k * 0.075f, 4600f - k * 60f, 3900f - k * 60f, 0.05f) * (1f - k * 0.07f);
                    return sum;
                }),
                // Three rising chirps.
                Make("bird chirps", 0.75f, (t, i) => Chirp(t, 2500f, 3900f, 0.09f) + Chirp(t - 0.22f, 2600f, 4100f, 0.09f) + Chirp(t - 0.44f, 2700f, 4400f, 0.11f)),
                // A soft cooing low call.
                Make("bird coo", 1.1f, (t, i) => (Tone(t, 520f + 40f * Mathf.Sin(t * 9f)) * Env(t, 0.06f, 0.18f)
                    + Tone(t - 0.45f, 470f + 30f * Mathf.Sin(t * 9f)) * Env(t - 0.45f, 0.08f, 0.3f)) * 0.6f),
            };
            CricketsLoop = MakeLoop("crickets", 4f, CricketSample);
            Howl = Make("howl", 3.2f, HowlSample);
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

        /// <summary>One bird's note: a sweep from one pitch to another under a quick swell and fade.</summary>
        private static float Chirp(float t, float from, float to, float length)
        {
            if (t < 0f || t > length) return 0f;
            float u = t / length;
            // The phase of a linear sweep, so the pitch glides without clicks.
            float phase = 2f * Mathf.PI * (from * t + (to - from) * t * u * 0.5f);
            float shape = Mathf.Sin(u * Mathf.PI);
            return (Mathf.Sin(phase) + 0.25f * Mathf.Sin(phase * 2f)) * shape * shape * 0.5f;
        }

        private float _radioLow;
        private float RadioSample(float t, int i)
        {
            float n = Noise();
            _radioLow += (n - _radioLow) * 0.5f;
            float bright = n - _radioLow;
            // Keyed: a burst of squelch; then a voice, muffled and broken up; a beep when let go.
            float squelch = bright * Env(t, 0.002f, 0.06f) * 0.9f;
            float talking = t > 0.12f && t < 1.0f ? 1f : 0f;
            float syllables = Mathf.Max(0f, Mathf.Sin(t * 26f + Mathf.Sin(t * 7f) * 2.5f));
            float pitch = 170f + 40f * Mathf.Sin(t * 5f) + 25f * Mathf.Sin(t * 13f);
            float voice = Mathf.Clamp((Tone(t, pitch) + 0.6f * Tone(t, pitch * 2f) + 0.5f * Tone(t, pitch * 3f) + 0.3f * Tone(t, pitch * 5f)) * 1.8f, -1f, 1f);
            float speech = (voice * 0.55f + bright * 0.35f) * syllables * talking;
            float beep = Mathf.Clamp(Tone(t - 1.06f, 1450f) * 3f, -1f, 1f) * (t > 1.06f && t < 1.16f ? 1f : 0f) * 0.35f;
            return squelch + speech * 0.7f + bright * 0.05f * talking + beep;
        }

        private float _howlPhase;
        private float HowlSample(float t, int i)
        {
            // The pitch glides up, wavers as it holds, and sinks at the end; a breathy edge on top.
            float rise = Mathf.SmoothStep(0f, 1f, t / 0.5f), fall = Mathf.SmoothStep(0f, 1f, (t - 2.2f) / 0.9f);
            float pitch = 330f + 330f * rise - 170f * fall + 9f * Mathf.Sin(t * 31f) * rise;
            _howlPhase += 2f * Mathf.PI * pitch / Rate;
            float voice = Mathf.Sin(_howlPhase) + 0.35f * Mathf.Sin(_howlPhase * 2f) + 0.12f * Mathf.Sin(_howlPhase * 3f);
            float swell = Mathf.Clamp01(t / 0.35f) * Mathf.Clamp01((3.2f - t) / 0.7f);
            return (voice * 0.8f + Dark(0.3f) * 0.12f) * swell;
        }

        private float CricketSample(float t, int i)
        {
            // Two crickets out of step: quick pulses of a high tone, in bursts.
            float a = Tone(t, 4300f) * Pulse(t, 28f, 0.55f) * Gate(t, 0.9f, 0.45f);
            float b = Tone(t, 4700f) * Pulse(t + 0.013f, 31f, 0.5f) * Gate(t + 0.37f, 1.15f, 0.5f) * 0.6f;
            return (a + b) * 0.35f + Noise() * 0.01f;
        }

        /// <summary>1 for the first <paramref name="duty"/> of each cycle at <paramref name="hz"/>, softened.</summary>
        private static float Pulse(float t, float hz, float duty)
        {
            float u = (t * hz) % 1f;
            return u < duty ? Mathf.Sin(u / duty * Mathf.PI) : 0f;
        }

        /// <summary>On for <paramref name="on"/> seconds out of every <paramref name="period"/>.</summary>
        private static float Gate(float t, float period, float on) => (t % period) < on ? 1f : 0f;

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

        private float _hissLow, _spit;
        private float FuseSample(float t, int i)
        {
            // Bright hiss (noise less its low end) with sparks spitting at random.
            float n = Noise();
            _hissLow += (n - _hissLow) * 0.2f;
            if (_random.NextDouble() < 0.004) _spit = 1f;
            float sample = (n - _hissLow) * (0.45f + 0.15f * Mathf.Sin(t * 23f)) + Noise() * _spit * 0.7f;
            _spit *= 0.93f;
            return sample;
        }

        private float _grind;
        private static float SirenSample(float t, int i)
        {
            // The pitch swings between 600 and 1100 Hz once every two seconds; the phase is the sweep's
            // integral (850 t, less 250/pi cos(pi t), turns), so it rises and falls smoothly and the loop joins.
            float phase = 2f * Mathf.PI * 850f * t - 500f * Mathf.Cos(Mathf.PI * t);
            // A horn's buzz: the tone and two overtones.
            return Mathf.Sin(phase) * 0.6f + Mathf.Sin(phase * 2f) * 0.25f + Mathf.Sin(phase * 3f) * 0.12f;
        }

        private float MachineSample(float t, int i)
        {
            // Whole cycles in the loop's two seconds, so it joins up: a 55 Hz hum with its overtones,
            // grinding that swells twice a second, and a clank as the drum comes round.
            float hum = Tone(t, 55f) * 0.32f + Tone(t, 110f) * 0.18f + Tone(t, 165f) * 0.07f;
            _grind += (Noise() - _grind) * 0.3f;
            float grind = _grind * (0.3f + 0.22f * Mathf.Sin(t * Mathf.PI * 4f));
            float turn = t % 0.5f;
            float clank = Tone(turn, 410f) * Env(turn, 0.0005f, 0.03f) * 0.45f + Noise() * Env(turn, 0.0005f, 0.012f) * 0.3f;
            return hum + grind + clank;
        }

        private float _drone;
        private float PlaneSample(float t, int i)
        {
            // Four propellers a little out of step (whole cycles in the loop's four seconds, so it joins
            // up), beating against each other, over a rumble.
            float props = Tone(t, 62f) * 0.22f + Tone(t, 63.5f) * 0.2f + Tone(t, 124f) * 0.12f + Tone(t, 126.5f) * 0.1f + Tone(t, 31f) * 0.18f;
            _drone += (Noise() - _drone) * 0.05f;
            float beat = 0.8f + 0.2f * Mathf.Sin(t * Mathf.PI * 2f * 1.5f);
            return (props + _drone * 0.9f) * beat;
        }

        private float _slap, _rush;
        private float RotorSample(float t, int i)
        {
            // Five blade passes a second (whole cycles in the loop's two seconds): each a heavy thud of
            // low tone and chopped air, over the turbine's steady whine and a rumble.
            float pass = t % 0.2f;
            _slap += (Noise() - _slap) * 0.18f;
            float thud = Mathf.Exp(-pass * 26f);
            float chop = (Tone(pass, 48f) * 0.9f + _slap * 1.1f) * thud;
            float whine = Tone(t, 1150f) * 0.035f + Tone(t, 2300f) * 0.015f;
            _drone += (Noise() - _drone) * 0.04f;
            return chop + whine + _drone * 0.5f;
        }

        private float _exhaust;
        private float EngineSample(float t, int i)
        {
            // Twenty-five firings a second (whole cycles in the loop's two seconds), each a short
            // knock of tone and exhaust noise, over the hum of the crank and its first overtone.
            float fire = t % 0.04f;
            _exhaust += (Noise() - _exhaust) * 0.5f;
            float knock = (Tone(fire, 190f) * 0.35f + _exhaust * 0.6f) * Env(fire, 0.0008f, 0.011f);
            float hum = Tone(t, 50f) * 0.22f + Tone(t, 100f) * 0.12f + Tone(t, 25f) * 0.1f;
            return knock + hum;
        }

        private float _hiss, _patter;
        private float RainSample(float t, int i)
        {
            // A soft hiss (noise with its harshest top taken off) under a patter of single drops.
            _hiss += (Noise() - _hiss) * 0.45f;
            _patter *= 0.94f;
            if (_random.NextDouble() < 0.0016) _patter += Noise() * 0.9f;
            float swell = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * t / 5f);
            return _hiss * 0.55f * swell + _patter * Noise() * 0.5f;
        }

        private float _thunder, _thunder2;
        private float ThunderSample(float t, int i)
        {
            // A crack, then a long low roll that swells and dies away in rolls.
            _thunder += (Noise() - _thunder) * 0.012f;
            _thunder2 += (_thunder - _thunder2) * 0.05f;
            float crack = Noise() * Env(t, 0.002f, 0.08f) * 0.35f;
            float roll = Env(t - 0.05f, 0.25f, 2.4f) * (0.75f + 0.25f * Mathf.Sin(t * 5.3f) * Mathf.Sin(t * 1.7f + 0.6f));
            return crack + _thunder2 * roll * 9f;
        }

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
