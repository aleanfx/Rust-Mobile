using Oxfall.Core.Rules;
using Oxfall.Game.Audio;
using Oxfall.Game.Controls;
using Oxfall.Game.Net;
using Oxfall.Game.UI;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Player
{
    /// <summary>
    /// A fishing rod in hand: aim at deep water and LANZAR sends the float there on its line; it bobs
    /// until a fish takes it, when it is pulled under for a moment: RECOGER then lands the fish.
    /// Too soon brings the line back empty, too late and the fish is gone.
    /// </summary>
    public sealed class FishingRodTool : MonoBehaviour
    {
        /// <summary>Where the rod's tip is in its own metres (the line leaves from there).</summary>
        private static readonly Vector3 Tip = new Vector3(0f, 1.25f, 0f);

        private PlayerInput _input;
        private Transform _head;
        private Hud _hud;
        private Gatherer _gatherer;
        private GameActions _actions;
        private Transform _bobber;
        private LineRenderer _line;
        private Vector3 _from, _to;
        private float _flight = -1f, _since, _bite = -1f, _cooldown;
        private float _surface;

        public bool Active { get; private set; }

        /// <summary>The float is out on the water.</summary>
        public bool Casting { get; private set; }

        /// <summary>A fish is on: the float is under.</summary>
        public bool Biting => _bite >= 0f;

        public event System.Action<SoundCue> Sound;

        public void Init(PlayerInput input, Transform head, Hud hud, Gatherer gatherer, GameActions actions, Material[] bobberMaterials, Material lineMaterial)
        {
            _input = input;
            _head = head;
            _hud = hud;
            _gatherer = gatherer;
            _actions = actions;
            _actions.FishNews += OnNews;

            var bobber = new GameObject("Bobber");
            bobber.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Bobber();
            var r = bobber.AddComponent<MeshRenderer>();
            r.sharedMaterials = bobberMaterials;
            r.shadowCastingMode = ShadowCastingMode.Off;
            _bobber = bobber.transform;
            _bobber.localScale = Vector3.one * 1.6f;
            _line = bobber.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 12;
            _line.widthMultiplier = 0.006f;
            _line.numCapVertices = 0;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            if (lineMaterial != null) _line.sharedMaterial = lineMaterial;
            bobber.SetActive(false);
        }

        public void SetActive(bool on)
        {
            if (on == Active) return;
            Active = on;
            if (on)
            {
                _hud.SetAttackLabel("LANZAR");
                _hud.ShowToast("Apunta al mar o a un estanque hondo y toca LANZAR. Cuando el flotador se hunda, toca RECOGER");
                return;
            }
            if (Casting) _actions.StopFishing();
            Clear();
        }

        private void Update()
        {
            if (!Active) return;
            float dt = Time.deltaTime;
            _cooldown -= dt;
            _actions.TickFishing();
            if (_input.AttackPressed && _cooldown <= 0f)
            {
                if (Casting) Reel();
                else Cast();
            }
            ShowAim();
        }

        private void LateUpdate()
        {
            if (!Casting) return;
            _since += Time.deltaTime;
            Vector3 at;
            if (_flight >= 0f)
            {
                // Out along an arc to where it lands.
                _flight = Mathf.Min(1f, _flight + Time.deltaTime / 0.7f);
                at = Vector3.Lerp(_from, _to, _flight) + Vector3.up * Mathf.Sin(_flight * Mathf.PI) * 2.5f;
                if (_flight >= 1f)
                {
                    _flight = -1f;
                    Sound?.Invoke(SoundCue.Plant);
                }
            }
            else
            {
                // Bobbing on the swell; pulled under (and jerking) while a fish has it.
                float bob = Mathf.Sin(_since * 2.1f) * 0.025f;
                if (_bite >= 0f)
                {
                    _bite += Time.deltaTime;
                    bob = -0.14f + Mathf.Sin(_bite * 23f) * 0.03f;
                }
                at = new Vector3(_to.x, _surface + bob, _to.z);
            }
            _bobber.position = at;
            var tip = _gatherer.HandPoint(Tip);
            // The line sags between the tip and the float, less once it is tight with a fish on it.
            float sag = _bite >= 0f ? 0.1f : 0.6f;
            for (int i = 0; i < _line.positionCount; i++)
            {
                float t = i / (float)(_line.positionCount - 1);
                _line.SetPosition(i, Vector3.Lerp(tip, at + Vector3.up * 0.08f, t) - Vector3.up * Mathf.Sin(t * Mathf.PI) * sag);
            }
        }

        /// <summary>Where a cast would land: on the water where the crosshair meets it, or the farthest water straight ahead.</summary>
        public bool TryAim(out Vector3 at)
        {
            var ground = _actions.Ground;
            var origin = _head.position;
            var dir = _head.forward;
            var feet = transform.position;
            for (float d = 0.5f; d < Fishing.CastRange * 2f; d += 0.25f)
            {
                var p = origin + dir * d;
                float flat = new Vector2(p.x - feet.x, p.z - feet.z).magnitude;
                if (flat > Fishing.CastRange) break;
                if (flat >= Fishing.MinCast && Fishing.WaterAt(ground, p.x, p.z, out float surface) && p.y <= surface + 0.05f)
                {
                    at = new Vector3(p.x, surface, p.z);
                    return true;
                }
                if (p.y < ground.Height(p.x, p.z) - 0.05f) break;
            }
            var ahead = new Vector3(dir.x, 0f, dir.z).normalized;
            for (float d = Fishing.CastRange * 0.75f; d >= Fishing.MinCast; d -= 0.5f)
                if (TryWater(feet + ahead * d, out at)) return true;
            for (float d = Fishing.CastRange * 0.75f; d <= Fishing.CastRange; d += 0.5f)
                if (TryWater(feet + ahead * d, out at)) return true;
            at = default;
            return false;
        }

        private bool TryWater(Vector3 p, out Vector3 at)
        {
            bool ok = Fishing.WaterAt(_actions.Ground, p.x, p.z, out float surface);
            at = new Vector3(p.x, surface, p.z);
            return ok;
        }

        /// <summary>Casts at where it aims. For tests too.</summary>
        public void Cast()
        {
            if (!Active || Casting) return;
            _cooldown = 0.5f;
            if (!TryAim(out var at))
            {
                _hud.ShowToast("Aquí no hay agua honda: apunta al mar o a un estanque");
                Sound?.Invoke(SoundCue.Denied);
                return;
            }
            var r = _actions.Cast(at);
            if (r != ActionResult.Ok)
            {
                _hud.ShowToast(r == ActionResult.TooFar ? "El agua está demasiado lejos: acércate a la orilla"
                    : r == ActionResult.TooClose ? "Demasiado cerca: lánzala más lejos"
                    : r == ActionResult.TooHigh ? "Estás demasiado alto sobre el agua"
                    : r == ActionResult.NoWater ? "Ahí el agua no es honda"
                    : GameActions.Explain(r));
                Sound?.Invoke(SoundCue.Denied);
                return;
            }
            Casting = true;
            _since = 0f;
            _bite = -1f;
            _from = _gatherer.HandPoint(Tip);
            _to = at;
            _surface = at.y;
            _flight = 0f;
            _bobber.gameObject.SetActive(true);
            _gatherer.PlaySwing();
            Sound?.Invoke(SoundCue.Swing);
            _hud.SetAttackLabel("RECOGER");
            _hud.ShowToast("Espera a que el flotador se hunda…");
        }

        /// <summary>Reels in now. For tests too.</summary>
        public void Reel()
        {
            if (!Casting) return;
            _cooldown = 0.6f;
            _gatherer.PlaySwing();
            _actions.Reel();
            Clear();
            Sound?.Invoke(SoundCue.BowDraw);
        }

        private void Clear()
        {
            Casting = false;
            _bite = -1f;
            _flight = -1f;
            if (_bobber != null) _bobber.gameObject.SetActive(false);
            if (Active) _hud.SetAttackLabel("LANZAR");
        }

        private void OnNews(FishCatch news)
        {
            switch (news)
            {
                case FishCatch.Bite:
                    if (!Casting) return;
                    _bite = 0f;
                    _hud.ShowToast("¡Pica! Toca RECOGER ya");
                    _hud.SetAttackLabel("¡RECOGER!");
                    Sound?.Invoke(SoundCue.KeyBeep);
                    return;
                case FishCatch.Nothing:
                    _hud.ShowToast("Nada todavía: espera a que el flotador se hunda");
                    return;
                case FishCatch.Escaped:
                    _hud.ShowToast("Se escapó. Recoge justo cuando el flotador se hunda");
                    Sound?.Invoke(SoundCue.Denied);
                    Clear();
                    return;
                case FishCatch.Lost:
                    _hud.ShowToast("Se perdió el sedal");
                    Clear();
                    return;
                default:
                    _hud.ShowToast($"Pescaste {Fishing.Name(news)}: +{Fishing.Pieces(news)} pescado crudo. Ásalo en una fogata");
                    Sound?.Invoke(SoundCue.Accepted);
                    Clear();
                    return;
            }
        }

        private void ShowAim()
        {
            if (Casting) return;
            _hud.SetAimInfo(TryAim(out _) ? null : "Sin agua honda al alcance");
        }

        private void OnDestroy()
        {
            if (_actions != null) _actions.FishNews -= OnNews;
            if (_bobber != null) Destroy(_bobber.gameObject);
        }
    }
}
