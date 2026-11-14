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
                return def == null || (def.Category == ItemCategory.Tool && def.Tool != ToolKind.Hammer);
            }
        }

        private void Update()
        {
            if (_input == null) return;
            float dt = Time.deltaTime;
            _cooldown -= dt;
            _hintCooldown -= dt;

            if (_input.Attack && _cooldown <= 0f && CanSwing)
            {
                _cooldown = Mathf.Max(SwingSeconds, Combat.SwingSeconds(_hotbar.HeldTool));
                _swing = 0f;
                Sound?.Invoke(SoundCue.Swing);
                Strike();
            }
            else if (_input.AttackPressed && _cooldown <= 0f && _hotbar.HeldDefinition?.Category == ItemCategory.Food)
            {
                _cooldown = 0.5f;
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
            _swing = 0f;
            if (CanSwing) Strike();
            else if (_hotbar.HeldDefinition?.Category == ItemCategory.Food) Eat(_hotbar.HeldDefinition);
        }

        /// <summary>
        /// Online, a blow that lands on another player goes to the server (their id, the tool in
        /// hand), which judges it. Null when playing alone.
        /// </summary>
        public Action<ushort, ToolKind> StrikePlayer;

        /// <summary>A blow that lands on an animal, living or a carcass (its id, the tool in hand).</summary>
        public Action<int, ToolKind> StrikeAnimal;

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
            Sound?.Invoke(SoundCue.Eat);
            _text.Clear();
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

            var yields = node.Hit(_hotbar.HeldTool);
            if (yields.Count > 0 || !node.Node.IsDepleted) PlayHitSound(node.Node.Type);
            if (yields.Count == 0)
            {
                // Bare hands or a torch on a tree or a rock: say once in a while what is missing.
                if (!node.Node.IsDepleted && _hintCooldown <= 0f)
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
                : type == ResourceNodeType.Stone || type == ResourceNodeType.MetalOre ? SoundCue.Stone
                : SoundCue.Plant);

        /// <summary>A quick chop: the held item tips forward and down about the grip, then back.</summary>
        private void AnimateHand(float dt)
        {
            if (_held == null) return;
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
