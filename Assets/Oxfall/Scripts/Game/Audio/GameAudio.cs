using Oxfall.Game.World;
using UnityEngine;

namespace Oxfall.Game.Audio
{
    /// <summary>What happened, for the sound it makes.</summary>
    public enum SoundCue
    {
        Swing,
        Wood,
        Stone,
        Plant,
        Eat,
        Drink,
        Place,
        Crafted,
        Tap,
        KeyBeep,
        Accepted,
        Denied,
        HitBody,
        Hurt,
        BowDraw,
        BowShot,
        Metal,
        Revolver,
        Shotgun,
        Reload,
        DryFire,
        Bandage,
        Rifle,
    }

    /// <summary>
    /// Plays the game's sounds: one-shots for hits, picking, eating and the interface, footsteps
    /// from how far the player walks, and the wind and the surf, the surf louder near the shore.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private SoundBank _bank;
        private AudioSource _shots;
        private AudioSource _wind;
        private AudioSource _rain;

        /// <summary>How hard it is raining (0..1): the rain's hiss and a stronger wind with it.</summary>
        public float Rain { get; set; }
        private AudioSource _waves;
        private Transform _player;
        private CharacterController _body;
        private IslandShape _shape;
        private Vector3 _lastPosition;
        private float _stride;
        private int _step;
        private float _ambienceTimer;
        private float _wavesTarget;

        public SoundBank Bank => _bank;

        public static GameAudio Create(Transform player, IslandShape shape)
        {
            var go = new GameObject("Audio");
            var audio = go.AddComponent<GameAudio>();
            audio._bank = new SoundBank();
            audio._player = player;
            audio._body = player.GetComponent<CharacterController>();
            audio._shape = shape;
            audio._lastPosition = player.position;
            audio._shots = go.AddComponent<AudioSource>();
            audio._shots.playOnAwake = false;
            audio._wind = audio.Loop(go, audio._bank.WindLoop, 0.22f);
            audio._waves = audio.Loop(go, audio._bank.WavesLoop, 0f);
            audio._rain = audio.Loop(go, audio._bank.RainLoop, 0f);
            return audio;
        }

        private AudioSource Loop(GameObject host, AudioClip clip, float volume)
        {
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
            source.playOnAwake = false;
            source.Play();
            return source;
        }

        /// <summary>A looping 3D sound on an object in the world (a campfire's crackle).</summary>
        public static AudioSource Attach(GameObject host, AudioClip clip, float volume, float maxDistance)
        {
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.volume = volume;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            // Start somewhere in the loop, so two fires never crackle in step.
            source.time = Random.value * clip.length * 0.9f;
            source.Play();
            return source;
        }

        /// <summary>An animal's call from where it stands, fading with distance.</summary>
        public void PlayAnimal(Oxfall.Core.Wildlife.AnimalKind kind, Vector3 at)
        {
            var clip = kind == Oxfall.Core.Wildlife.AnimalKind.Wolf ? _bank.Growl
                : kind == Oxfall.Core.Wildlife.AnimalKind.Boar ? _bank.Grunt : _bank.Bark;
            PlayAt(clip, at, 0.9f, 2f, 45f, 0.08f);
        }

        /// <summary>A sound from somewhere in the world, fading with distance out to <paramref name="maxDistance"/>.</summary>
        public static void PlayAt(AudioClip clip, Vector3 at, float volume, float minDistance, float maxDistance, float pitchJitter = 0f)
        {
            var go = new GameObject(clip.name);
            go.transform.position = at;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.volume = volume;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.Play();
            Destroy(go, clip.length / source.pitch + 0.1f);
        }

        public void Play(SoundCue cue)
        {
            switch (cue)
            {
                case SoundCue.Swing: Shot(_bank.Swing, 0.5f, 0.15f); break;
                case SoundCue.Wood: Shot(_bank.ChopWood, 0.9f, 0.1f); break;
                case SoundCue.Stone: Shot(_bank.HitStone, 0.8f, 0.1f); break;
                case SoundCue.Plant: Shot(_bank.Pick, 0.7f, 0.1f); break;
                case SoundCue.Eat: Shot(_bank.Eat, 0.8f, 0.08f); break;
                case SoundCue.Bandage: Shot(_bank.Bandage, 0.8f, 0.06f); break;
                case SoundCue.Drink: Shot(_bank.Drink, 0.8f, 0.05f); break;
                case SoundCue.Place: Shot(_bank.Place, 0.9f, 0.05f); break;
                case SoundCue.Crafted: Shot(_bank.Crafted, 0.6f, 0f); break;
                case SoundCue.Tap: Shot(_bank.Tap, 0.5f, 0.05f); break;
                case SoundCue.KeyBeep: Shot(_bank.KeyBeep, 0.6f, 0.03f); break;
                case SoundCue.Accepted: Shot(_bank.Accepted, 0.7f, 0f); break;
                case SoundCue.Denied: Shot(_bank.Denied, 0.85f, 0f); break;
                case SoundCue.HitBody: Shot(_bank.HitBody, 1f, 0.08f); break;
                case SoundCue.Hurt: Shot(_bank.Hurt, 1f, 0.06f); break;
                case SoundCue.BowDraw: Shot(_bank.BowDraw, 0.5f, 0.05f); break;
                case SoundCue.BowShot: Shot(_bank.BowShot, 0.9f, 0.06f); break;
                case SoundCue.Metal: Shot(_bank.HitMetal, 0.75f, 0.08f); break;
                case SoundCue.Revolver: Shot(_bank.RevolverShot, 1f, 0.05f); break;
                case SoundCue.Shotgun: Shot(_bank.ShotgunShot, 1f, 0.05f); break;
                case SoundCue.Rifle: Shot(_bank.RifleShot, 1f, 0.04f); break;
                case SoundCue.Reload: Shot(_bank.GunReload, 0.7f, 0f); break;
                case SoundCue.DryFire: Shot(_bank.DryFire, 0.7f, 0.05f); break;
            }
        }

        private void Shot(AudioClip clip, float volume, float pitchJitter)
        {
            _shots.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            _shots.PlayOneShot(clip, volume);
        }

        private void Update()
        {
            if (_player == null) return;
            Footsteps();
            _ambienceTimer -= Time.deltaTime;
            if (_ambienceTimer <= 0f)
            {
                _ambienceTimer = 0.5f;
                _wavesTarget = ShoreCloseness() * 0.55f;
            }
            _waves.volume = Mathf.MoveTowards(_waves.volume, _wavesTarget, Time.deltaTime * 0.3f);
            _rain.volume = Rain * 0.5f;
            _wind.volume = 0.22f + Rain * 0.2f;
        }

        /// <summary>Thunder, as loud as it is near.</summary>
        public void PlayThunder(float volume) => Shot(_bank.Thunder, volume, 0.12f);

        /// <summary>A step every stride walked on the ground: sand on the beach, grass elsewhere.</summary>
        private void Footsteps()
        {
            var position = _player.position;
            var moved = position - _lastPosition;
            _lastPosition = position;
            moved.y = 0f;
            float distance = moved.magnitude;
            // A teleport is not a walk.
            if (distance > 2f || (_body != null && !_body.isGrounded)) return;
            _stride += distance;
            float speed = distance / Mathf.Max(Time.deltaTime, 1e-4f);
            float strideLength = speed > 5f ? 2.1f : 1.6f;
            if (_stride < strideLength) return;
            _stride = 0f;
            float h = _shape.Height(position.x, position.z);
            var steps = h < 2.2f ? _bank.StepsSand : _bank.StepsGrass;
            _shots.pitch = 1f;
            _shots.PlayOneShot(steps[_step++ % steps.Length], speed > 5f ? 0.55f : 0.4f);
        }

        /// <summary>0 far inland, 1 at the water's edge: how much of the ground around is sea.</summary>
        private float ShoreCloseness()
        {
            var p = _player.position;
            int sea = 0;
            const int samples = 12;
            for (int i = 0; i < samples; i++)
            {
                float a = i * Mathf.PI * 2f / samples;
                for (int r = 1; r <= 3; r++)
                {
                    float d = r * 12f;
                    if (_shape.Height(p.x + Mathf.Cos(a) * d, p.z + Mathf.Sin(a) * d) < IslandShape.WaterLevel)
                        sea += 4 - r;
                }
            }
            return Mathf.Clamp01(sea / (samples * 2.5f));
        }
    }
}
