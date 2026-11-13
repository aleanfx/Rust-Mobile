using System;
using System.Collections.Generic;
using System.Text;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Survival;
using Oxfall.Game.Audio;
using Oxfall.Game.Controls;
using Oxfall.Game.Items;
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
                _cooldown = SwingSeconds;
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

        private void Strike()
        {
            var ray = new Ray(_head.position, _head.forward);
            if (!Physics.SphereCast(ray, AimRadius, out var hit, Reach, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide))
            {
                TryDrink();
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
            if (_vitals == null || !_inventory.Remove(food.Id, 1)) return;
            _vitals.Consume(food.Nourishment);
            Sound?.Invoke(SoundCue.Eat);
            _text.Clear();
            _text.Append("Comes ").Append(food.DisplayName.ToLowerInvariant());
            if (food.Nourishment.Food > 0f) _text.Append("   +").Append(Mathf.RoundToInt(food.Nourishment.Food)).Append(" comida");
            if (food.Nourishment.Water > 0f) _text.Append("   +").Append(Mathf.RoundToInt(food.Nourishment.Water)).Append(" agua");
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
                _vitals.Consume(new Nourishment(0f, DrinkWater));
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

        private void Gather(ResourceNodeView node)
        {
            var yields = node.Hit(_hotbar.HeldTool);
            if (yields.Count > 0 || !node.Node.IsDepleted)
            {
                var type = node.Node.Type;
                Sound?.Invoke(type == ResourceNodeType.Tree ? SoundCue.Wood
                    : type == ResourceNodeType.Stone || type == ResourceNodeType.MetalOre ? SoundCue.Stone
                    : SoundCue.Plant);
            }
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

        /// <summary>A quick chop: the held item tips forward and down about the grip, then back.</summary>
        private void AnimateHand(float dt)
        {
            if (_held == null) return;
            _swing += dt;
            float t = Mathf.Clamp01(_swing / (SwingSeconds * 0.6f));
            float arc = Mathf.Sin(t * Mathf.PI);
            _hand.localPosition = _held.HandPosition + new Vector3(-0.08f * arc, -0.06f * arc, 0.22f * arc);
            _hand.localRotation = Quaternion.AngleAxis(55f * arc, Vector3.right) * _held.HandRotation;
        }
    }
}
