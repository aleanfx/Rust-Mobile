using System;
using System.Collections.Generic;
using System.Text;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.Survival;
using Oxfall.Game.Audio;
using Oxfall.Game.Controls;
using Oxfall.Game.Items;
using Oxfall.Game.Net;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Player
{
    /// <summary>
    /// Shows what is chosen on the hotbar in the player's hand, swings it while attack is held, and
    /// hits whatever resource is in front of the camera. What a hit gives is decided by the Core
    /// rules from the tool in hand; this adds it to the inventory.
    /// </summary>
    [DefaultExecutionOrder(10)]
    public sealed class Gatherer : MonoBehaviour
    {
        public float SwingSeconds = 0.7f;
        public float Reach = 2.7f;
        public float AimRadius = 0.3f;

        /// <summary>Water from one swallow at a pond.</summary>
        public const float DrinkWater = 25f;

        /// <summary>Raised with a message for the player ("+15 Madera").</summary>
        public event Action<string> Message;

        /// <summary>Raised for every swing, hit, bite and swallow, for its sound.</summary>
        public event Action<SoundCue> Sound;

        /// <summary>The crackle of a burning torch, when there is one to play.</summary>
        public AudioClip FireSound { get; set; }

        /// <summary>Eating and drinking go through here (the shared rules alone, the server online).</summary>
        public GameActions Actions { get; set; }

        private PlayerInput _input;
        private Inventory _inventory;
        private Hotbar _hotbar;
        private ItemCatalog _catalog;
        private IReadOnlyDictionary<string, ItemModel> _models;
        private Transform _head;
        private Transform _hand;
        private MeshFilter _handFilter;
        private MeshRenderer _handRenderer;
        private ItemModel _held;
        private string _heldId;
        private Material _flameMaterial;
        private Fire _fire;
        private Vitals _vitals;
        private IslandShape _shape;
        private float _cooldown;
        private float _swing = 10f;
        private float _hintCooldown;
        private readonly StringBuilder _text = new StringBuilder();

        // The bow: how long it has been drawn (-1: not drawing), its string, the arrow on it, and its recoil.
        private float _draw = -1f;
        private float _recoil = 10f;
        private LineRenderer _string;
        private Transform _nocked;
        private Mesh _bowLimbs;
        private static readonly Vector3[] StringPoints = new Vector3[3];

        /// <summary>Where loosed arrows fly (and stick).</summary>
        public ArrowFlights Arrows { get; set; }

        /// <summary>Materials for the bow's string and the arrow on it.</summary>
        public Material StringMaterial { get; set; }
        public Material[] ArrowMaterials { get; set; }

        /// <summary>The bow is drawn (for tests and the HUD).</summary>
        public bool Drawing => _draw >= 0f;

        /// <summary>Where gunfire shows: muzzle flashes, bullets, what they strike, the kick.</summary>
        public Explosions Effects { get; set; }

        /// <summary>A gun fired from this hand: which kind (for the HUD and tests).</summary>
        public event Action<ToolKind> Fired;

        // Guns: since the last shot (for the kick), whether it was reloading last frame, and what the pellets hit.
        private float _gunKick = 10f;
        private bool _wasReloading;
        private readonly Dictionary<(ArrowTarget Kind, int Id), int> _pelletHits = new Dictionary<(ArrowTarget, int), int>();

        public void Init(PlayerInput input, Inventory inventory, Hotbar hotbar, ItemCatalog catalog, Transform head,
            IReadOnlyDictionary<string, ItemModel> models, Material flameMaterial, Vitals vitals, IslandShape shape)
        {
            _flameMaterial = flameMaterial;
            _vitals = vitals;
            _shape = shape;
            _input = input;
            _inventory = inventory;
            _hotbar = hotbar;
            _catalog = catalog;
            _models = models;
            _head = head;

            var hand = new GameObject("Held item");
            hand.transform.SetParent(head, false);
            _handFilter = hand.AddComponent<MeshFilter>();
            _handRenderer = hand.AddComponent<MeshRenderer>();
            _handRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _handRenderer.receiveShadows = false;
            _hand = hand.transform;
            RefreshHeld();
        }

        /// <summary>Puts what is chosen on the hotbar in the hand. Call after the inventory or the choice changes.</summary>
        public void RefreshHeld()
        {
            var id = _hotbar.Held.ItemId;
            if (id == _heldId) return;
            _heldId = id;
            _held = id != null && _models.TryGetValue(id, out var model) && model.ShowInHand ? model : null;
            _hand.gameObject.SetActive(_held != null);
            if (_held == null) return;
            _handFilter.sharedMesh = _held.Mesh;
            _handRenderer.sharedMaterials = _held.Materials;
            _hand.localScale = Vector3.one * _held.HandScale;
            _draw = -1f;
            if (_held.Shoots) RigBow();
            if (_string != null) _string.gameObject.SetActive(_held.Shoots);
            if (_nocked != null) _nocked.gameObject.SetActive(false);

            // A torch burns while it is in hand: a small flame and a light that reaches about 9 m.
            if (_held.Burns && _fire == null)
            {
                _fire = Fire.Create(_hand, _held.FirePosition, 0.7f, 2, 12f, 3.2f, MeshFactory.Flame(), _flameMaterial);
                if (FireSound != null) GameAudio.Attach(_fire.gameObject, FireSound, 0.18f, 6f);
            }
            if (_fire != null)
            {
                _fire.gameObject.SetActive(_held.Burns);
                _fire.transform.localPosition = _held.FirePosition;
            }
            _swing = 10f;
            AnimateHand(0f);
        }

        /// <summary>
        /// Swings with a tool, or bare-handed to pick plants. Not with the building plan (attack
        /// places pieces then), a campfire or resources.
        /// </summary>
        private bool CanSwing
        {
            get
            {
                var def = _hotbar.HeldDefinition;
                // The hammer builds (HammerTool) instead of gathering.
                return def == null || (def.Category == ItemCategory.Tool && def.Tool != ToolKind.Hammer && def.Tool != ToolKind.Explosive
                                       && !Firearms.IsGun(def.Tool));
            }
        }

        private void Update()
        {
            if (_input == null) return;
            float dt = Time.deltaTime;
            _cooldown -= dt;
            _hintCooldown -= dt;

            if (_held != null && _held.Shoots)
            {
                TickBow(dt);
                AnimateHand(dt);
                return;
            }
            if (_held != null && _held.Fires)
            {
                TickGun();
                AnimateHand(dt);
                return;
            }
            if (_input.Attack && _cooldown <= 0f && CanSwing)
            {
                _cooldown = Mathf.Max(SwingSeconds, Combat.SwingSeconds(_hotbar.HeldTool));
                _swing = 0f;
                Sound?.Invoke(SoundCue.Swing);
                Strike();
            }
            else if (_input.AttackPressed && _cooldown <= 0f && IsUsable(_hotbar.HeldDefinition))
            {
                // A bandage takes a moment to wrap; a bite is quick.
                _cooldown = _hotbar.HeldDefinition.Category == ItemCategory.Medical ? 1.2f : 0.5f;
                _swing = 0f;
                Eat(_hotbar.HeldDefinition);
            }

            AnimateHand(dt);
        }

        /// <summary>Plays a swing of what is in hand, without hitting anything (the hammer's blow).</summary>
        public void PlaySwing() => _swing = 0f;

        /// <summary>One swing (or one bite) with what is in hand, as one press of attack. For tests.</summary>
        public void UseOnce()
        {
            if (_held != null && _held.Shoots)
            {
                Loose();
                return;
            }
            if (_held != null && _held.Fires)
            {
                PullTrigger(true);
                return;
            }
            _swing = 0f;
            if (CanSwing) Strike();
            else if (IsUsable(_hotbar.HeldDefinition)) Eat(_hotbar.HeldDefinition);
        }

        /// <summary>Food to eat, or a bandage or syringe to use, from the hand.</summary>
        private static bool IsUsable(ItemDefinition def) =>
            def != null && (def.Category == ItemCategory.Food || def.Category == ItemCategory.Medical);

        /// <summary>
        /// Online, a blow that lands on another player goes to the server (their id, the tool in
        /// hand), which judges it. Null when playing alone.
        /// </summary>
        public Action<ushort, ToolKind> StrikePlayer;

        /// <summary>A blow that lands on an animal, living or a carcass (its id, the tool in hand).</summary>
        public Action<int, ToolKind> StrikeAnimal;

        /// <summary>Online, a blow that lands on a sleeping survivor (their owner number, the tool in hand).</summary>
        public Action<int, ToolKind> StrikeSleeper;

        private void Strike()
        {
            var ray = new Ray(_head.position, _head.forward);
            var tool = _hotbar.HeldTool;
            if (!Physics.SphereCast(ray, AimRadius, out var hit, Mathf.Max(Reach, Combat.MeleeReach(tool)), Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide))
            {
                TryDrink();
                return;
            }

            var player = hit.collider.GetComponent<Net.PlayerTag>();
            if (player != null)
            {
                if (StrikePlayer != null && Combat.CanHurt(tool)) StrikePlayer(player.Id, tool);
                return;
            }
            var sleeper = hit.collider.GetComponent<Net.SleeperTag>();
            if (sleeper != null)
            {
                if (StrikeSleeper != null && Combat.CanHurt(tool)) StrikeSleeper(sleeper.Owner, tool);
                return;
            }
            var animal = hit.collider.GetComponent<World.AnimalTag>();
            if (animal != null)
            {
                StrikeAnimal?.Invoke(animal.Id, tool);
                return;
            }

            // Aiming down at a pond, the cast can reach the pond's bed: water is checked then too.
            var node = hit.collider.GetComponentInParent<ResourceNodeView>();
            if (node == null) TryDrink();
            else Gather(node);
        }

        /// <summary>Eats one of what is in hand.</summary>
        private void Eat(ItemDefinition food)
        {
            int slot = _hotbar.Selected;
            if (_vitals == null || Actions == null || slot < 0 || Actions.Eat(slot) != ActionResult.Ok) return;
            _text.Clear();
            if (food.Category == ItemCategory.Medical)
            {
                Sound?.Invoke(SoundCue.Bandage);
                _text.Append(food.Id == ItemIds.MedicalSyringe ? "Te inyectas la jeringa" : "Te vendas las heridas");
                _text.Append("   +").Append(Mathf.RoundToInt(food.Nourishment.Health + food.Nourishment.Regen)).Append(" vida");
                Message?.Invoke(_text.ToString());
                return;
            }
            Sound?.Invoke(SoundCue.Eat);
            _text.Append("Comes ").Append(food.DisplayName.ToLowerInvariant());
            if (food.Nourishment.Food > 0f) _text.Append("   +").Append(Mathf.RoundToInt(food.Nourishment.Food)).Append(" comida");
            if (food.Nourishment.Water > 0f) _text.Append("   +").Append(Mathf.RoundToInt(food.Nourishment.Water)).Append(" agua");
            else if (food.Nourishment.Water < 0f) _text.Append("   ").Append(Mathf.RoundToInt(food.Nourishment.Water)).Append(" agua");
            Message?.Invoke(_text.ToString());
        }

        /// <summary>
        /// Drinks if the player is looking at fresh water close by; says so if it is the sea. Returns
        /// true if the swing went into water.
        /// </summary>
        private bool TryDrink()
        {
            if (_vitals == null || _shape == null) return false;
            var origin = _head.position;
            var forward = _head.forward;
            if (forward.y > -0.15f) return false;
            foreach (var pond in _shape.Ponds)
            {
                float t = (pond.Level - origin.y) / forward.y;
                if (t < 0f || t > Reach + 1.5f) continue;
                var p = origin + forward * t;
                if ((new Vector2(p.x, p.z) - pond.Centre).sqrMagnitude > pond.Radius * pond.Radius) continue;
                if (Actions == null || Actions.Drink() != ActionResult.Ok) return true;
                Sound?.Invoke(SoundCue.Drink);
                Message?.Invoke($"Bebes agua dulce   +{DrinkWater:0} agua");
                return true;
            }
            float sea = (IslandShape.WaterLevel - origin.y) / forward.y;
            if (sea >= 0f && sea <= Reach + 1.5f)
            {
                var p = origin + forward * sea;
                if (_shape.Height(p.x, p.z) < IslandShape.WaterLevel)
                {
                    if (_hintCooldown <= 0f)
                    {
                        _hintCooldown = 2.5f;
                        Message?.Invoke("El agua de mar es salada: busca una charca de agua dulce");
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Online, the server decides what a hit gives: this sends the swing (node id, tool) instead of
        /// taking from the node here. Null when playing alone.
        /// </summary>
        public Action<int, ToolKind> RemoteHit;

        private void Gather(ResourceNodeView node)
        {
            if (RemoteHit != null)
            {
                // The swing is felt at once (sound, shake); what it gave arrives from the server.
                var tool = _hotbar.HeldTool;
                if (node.Node.IsDepleted) return;
                if (GatherTable.PerHit(node.Node.Type, tool) <= 0)
                {
                    if (_hintCooldown <= 0f)
                    {
                        _hintCooldown = 2.5f;
                        Message?.Invoke(tool == ToolKind.None
                            ? "Elige una herramienta en la barra (la roca sirve)"
                            : "Esta herramienta no sirve aquí");
                    }
                    return;
                }
                PlayHitSound(node.Node.Type);
                node.Shake();
                RemoteHit(node.Node.Id, tool);
                return;
            }

            bool works = GatherTable.PerHit(node.Node.Type, _hotbar.HeldTool) > 0;
            var yields = node.Hit(_hotbar.HeldTool);
            if (yields.Count > 0 || !node.Node.IsDepleted) PlayHitSound(node.Node.Type);
            if (yields.Count == 0)
            {
                // Bare hands or a torch on a tree or a rock: say once in a while what is missing. (A barrel
                // that took the blow but is not open yet gives nothing either, and needs no hint.)
                if (!works && !node.Node.IsDepleted && _hintCooldown <= 0f)
                {
                    _hintCooldown = 2.5f;
                    Message?.Invoke(_hotbar.HeldTool == ToolKind.None
                        ? "Elige una herramienta en la barra (la roca sirve)"
                        : "Esta herramienta no sirve aquí");
                }
                return;
            }

            // What was gathered shows in the pickup feed (it follows the inventory); only a full
            // inventory needs saying.
            bool full = false;
            foreach (var y in yields) full |= _inventory.Add(y.ItemId, y.Amount) > 0;
            if (full) Message?.Invoke("Inventario lleno: parte se perdió");
        }

        private void PlayHitSound(ResourceNodeType type) =>
            Sound?.Invoke(type == ResourceNodeType.Tree ? SoundCue.Wood
                : type == ResourceNodeType.Stone || type == ResourceNodeType.MetalOre || type == ResourceNodeType.Sulfur ? SoundCue.Stone
                : type == ResourceNodeType.Barrel ? SoundCue.Metal
                : SoundCue.Plant);

        /// <summary>A quick chop: the held item tips forward and down about the grip, then back.</summary>
        // ---- The bow: hold attack to draw (and aim), let go to loose once it is fully drawn.

        private bool HasArrow => _inventory.Count(ItemIds.Arrow) > 0;

        private void TickBow(float dt)
        {
            if (_input.Attack && _cooldown <= 0f)
            {
                if (!HasArrow)
                {
                    if (_input.AttackPressed && _hintCooldown <= 0f)
                    {
                        _hintCooldown = 2.5f;
                        Message?.Invoke("No te quedan flechas: fabrícalas con madera y piedra");
                    }
                    return;
                }
                if (_draw < 0f)
                {
                    _draw = 0f;
                    Sound?.Invoke(SoundCue.BowDraw);
                }
                _draw += dt;
                return;
            }
            if (_draw < 0f) return;
            bool full = _draw >= Combat.DrawSeconds;
            _draw = -1f;
            if (full) Loose();
        }

        /// <summary>Lets the arrow fly along the crosshair; it lands where it lands and says on what.</summary>
        private void Loose()
        {
            if (Actions == null || !HasArrow) return;
            var forward = _head.forward;
            var origin = _head.position + forward * 0.35f;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (!Actions.Shoot(origin, yaw, pitch)) return;
            _draw = -1f;
            _cooldown = 0.3f;
            _recoil = 0f;
            Sound?.Invoke(SoundCue.BowShot);
            if (Arrows != null) Arrows.Launch(origin, forward, OnArrowLanded);
            else Actions.ArrowHit(ArrowTarget.None, 0);
        }

        private void OnArrowLanded(Collider collider, Vector3 point)
        {
            if (Actions == null) return;
            var player = collider != null ? collider.GetComponent<Net.PlayerTag>() : null;
            var animal = collider != null ? collider.GetComponent<AnimalTag>() : null;
            var sleeper = collider != null ? collider.GetComponent<Net.SleeperTag>() : null;
            if (player != null) Actions.ArrowHit(ArrowTarget.Player, player.Id);
            else if (animal != null) Actions.ArrowHit(ArrowTarget.Animal, animal.Id);
            else if (sleeper != null) Actions.ArrowHit(ArrowTarget.Sleeper, sleeper.Owner);
            else Actions.ArrowHit(ArrowTarget.None, 0);
        }

        /// <summary>The string (pulled back with the arrow as it is drawn) and the arrow on it.</summary>
        private void RigBow()
        {
            if (_bowLimbs == null) _bowLimbs = MeshFactory.WoodenBow(false);
            _handFilter.sharedMesh = _bowLimbs;
            if (_string == null)
            {
                var go = new GameObject("Bow string");
                go.transform.SetParent(_hand, false);
                _string = go.AddComponent<LineRenderer>();
                _string.useWorldSpace = false;
                _string.positionCount = 3;
                _string.widthMultiplier = 0.005f;
                _string.numCapVertices = 0;
                _string.shadowCastingMode = ShadowCastingMode.Off;
                _string.receiveShadows = false;
                if (StringMaterial != null) _string.sharedMaterial = StringMaterial;
            }
            if (_nocked == null && ArrowMaterials != null)
            {
                var go = new GameObject("Nocked arrow");
                go.transform.SetParent(_hand, false);
                go.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Arrow();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = ArrowMaterials;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                _nocked = go.transform;
            }
        }

        private void AnimateBow(float dt)
        {
            float drawn = _draw >= 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_draw / Combat.DrawSeconds)) : 0f;
            _recoil += dt;
            float kick = Mathf.Exp(-_recoil * 14f) * (1f - Mathf.Clamp01(_recoil / 0.4f));
            // Drawn: brought up to the middle and level, the arrow along the crosshair.
            _hand.localPosition = Vector3.Lerp(_held.HandPosition, new Vector3(0.0f, -0.06f, 0.56f), drawn)
                + new Vector3(0f, 0.015f, -0.05f) * kick;
            _hand.localRotation = Quaternion.Slerp(_held.HandRotation, Quaternion.Euler(0f, 0f, 6f), drawn)
                * Quaternion.AngleAxis(-6f * kick, Vector3.right);
            float nockZ = Mathf.Lerp(MeshFactory.BowTipTop.z, -0.5f, drawn);
            if (_string != null)
            {
                StringPoints[0] = MeshFactory.BowTipTop;
                StringPoints[1] = new Vector3(0f, 0f, nockZ);
                StringPoints[2] = MeshFactory.BowTipBottom;
                _string.SetPositions(StringPoints);
            }
            if (_nocked != null)
            {
                bool show = HasArrow && _recoil > 0.25f;
                if (_nocked.gameObject.activeSelf != show) _nocked.gameObject.SetActive(show);
                _nocked.localPosition = new Vector3(0.018f, 0f, nockZ);
                _nocked.localRotation = Quaternion.identity;
            }
        }

        // ---- Guns: hold attack to fire (as fast as the gun allows); empty, it reloads by itself.

        private void TickGun()
        {
            if (Actions == null || !Firearms.TryGet(_hotbar.HeldTool, out var gun)) return;
            bool reloading = Actions.GunState(gun.Tool).Reloading(Time.time);
            if (reloading && !_wasReloading) Sound?.Invoke(SoundCue.Reload);
            _wasReloading = reloading;
            if (_input.Attack) PullTrigger(_input.AttackPressed);
        }

        /// <summary>Fires the gun in hand if it can; a fresh press on an empty one clicks and says so.</summary>
        private void PullTrigger(bool pressed)
        {
            if (Actions == null || !Firearms.TryGet(_hotbar.HeldTool, out var gun)) return;
            if (_inventory.Count(gun.Ammo) < 1)
            {
                if (pressed && _hintCooldown <= 0f)
                {
                    _hintCooldown = 1.5f;
                    Sound?.Invoke(SoundCue.DryFire);
                    Message?.Invoke(gun.Tool == ToolKind.Shotgun
                        ? "Sin cartuchos: fabrícalos con pólvora y piedra"
                        : "Sin balas: fabrícalas con pólvora y metal");
                }
                return;
            }
            if (!Actions.GunState(gun.Tool).CanFire(gun, Time.time)) return;
            FireGun(gun);
        }

        /// <summary>One shot along the crosshair: each pellet a ray strayed by the shot's spray; what they hit is reported.</summary>
        private void FireGun(GunSpec gun)
        {
            var forward = _head.forward;
            var origin = _head.position;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            ushort seed = (ushort)UnityEngine.Random.Range(0, 65536);
            if (!Actions.Fire(gun.Tool, origin, yaw, pitch, seed)) return;
            _gunKick = 0f;
            bool big = gun.Pellets > 1;
            Sound?.Invoke(big ? SoundCue.Shotgun : SoundCue.Revolver);
            var muzzle = _hand.TransformPoint(_held.Muzzle);
            if (Effects != null)
            {
                Effects.MuzzleFlash(muzzle, forward, big);
                Effects.Punch(big ? 3.5f : 2f);
            }
            _pelletHits.Clear();
            Spray(gun, origin, muzzle, yaw, pitch, seed, Effects, _pelletHits);
            foreach (var pair in _pelletHits) Actions.GunHit(pair.Key.Kind, pair.Key.Id, pair.Value);
            Fired?.Invoke(gun.Tool);
        }

        /// <summary>
        /// The pellets of one shot, cast from <paramref name="origin"/> along yaw/pitch with the shot's
        /// spray: where each lands is drawn, and (if <paramref name="hits"/> is given) who it struck is counted.
        /// </summary>
        public static void Spray(GunSpec gun, Vector3 origin, Vector3 muzzle, float yaw, float pitch, ushort seed, Explosions effects,
            Dictionary<(ArrowTarget Kind, int Id), int> hits)
        {
            for (int i = 0; i < gun.Pellets; i++)
            {
                var (dy, dp) = Firearms.PelletOffset(gun, seed, i);
                var dir = Quaternion.Euler(-(pitch + dp), yaw + dy, 0f) * Vector3.forward;
                Vector3 end;
                if (Physics.Raycast(origin, dir, out var hit, gun.Range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                {
                    end = hit.point;
                    var player = hit.collider.GetComponent<Net.PlayerTag>();
                    var animal = player == null ? hit.collider.GetComponent<AnimalTag>() : null;
                    var sleeper = player == null && animal == null ? hit.collider.GetComponent<Net.SleeperTag>() : null;
                    bool flesh = player != null || animal != null || sleeper != null;
                    if (hits != null && flesh)
                    {
                        var key = player != null ? (ArrowTarget.Player, (int)player.Id)
                            : animal != null ? (ArrowTarget.Animal, animal.Id) : (ArrowTarget.Sleeper, sleeper.Owner);
                        hits.TryGetValue(key, out int n);
                        hits[key] = n + 1;
                    }
                    effects?.Impact(hit.point, hit.normal, flesh);
                }
                else end = origin + dir * Mathf.Min(gun.Range, 80f);
                effects?.Bullet(muzzle, end);
            }
        }

        private void AnimateGun(float dt)
        {
            _gunKick += dt;
            float kick = Mathf.Exp(-_gunKick * 16f);
            bool big = _hotbar.HeldTool == ToolKind.Shotgun;
            // Reloading: dropped and tipped over, then back up.
            float reload = 0f;
            if (Actions != null && Firearms.TryGet(_hotbar.HeldTool, out var gun))
            {
                var state = Actions.GunState(gun.Tool);
                if (state.Reloading(Time.time))
                {
                    float t = 1f - (state.ReloadEndsAt - Time.time) / gun.ReloadSeconds;
                    reload = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                    reload = Mathf.Min(1f, reload * 1.6f);
                }
            }
            _hand.localPosition = _held.HandPosition + new Vector3(0f, 0.012f, big ? -0.09f : -0.06f) * kick
                + new Vector3(-0.05f, -0.1f, -0.04f) * reload;
            _hand.localRotation = Quaternion.Euler(-(big ? 14f : 10f) * kick + 28f * reload, -8f * reload, 35f * reload) * _held.HandRotation;
        }

        private void AnimateHand(float dt)
        {
            if (_held == null) return;
            if (_held.Shoots)
            {
                AnimateBow(dt);
                return;
            }
            if (_held.Fires)
            {
                AnimateGun(dt);
                return;
            }
            _swing += dt;
            float t = Mathf.Clamp01(_swing / (SwingSeconds * 0.6f));
            float arc = Mathf.Sin(t * Mathf.PI);
            if (_held.Thrusts)
            {
                // A stab: quick out, slower back.
                float stab = t < 0.35f ? Mathf.SmoothStep(0f, 1f, t / 0.35f) : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.65f);
                _hand.localPosition = _held.HandPosition + new Vector3(-0.1f * stab, 0.05f * stab, 0.42f * stab);
                _hand.localRotation = _held.HandRotation;
                return;
            }
            _hand.localPosition = _held.HandPosition + new Vector3(-0.08f * arc, -0.06f * arc, 0.22f * arc);
            _hand.localRotation = Quaternion.AngleAxis(55f * arc, Vector3.right) * _held.HandRotation;
        }
    }
}
