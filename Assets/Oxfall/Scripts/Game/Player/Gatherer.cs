using System;
using System.Collections.Generic;
using System.Text;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
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

        /// <summary>Raised with a message for the player ("+15 Madera").</summary>
        public event Action<string> Message;

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
        private float _cooldown;
        private float _swing = 10f;
        private float _hintCooldown;
        private readonly StringBuilder _text = new StringBuilder();

        public void Init(PlayerInput input, Inventory inventory, Hotbar hotbar, ItemCatalog catalog, Transform head,
            IReadOnlyDictionary<string, ItemModel> models)
        {
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
                return def == null || def.Category == ItemCategory.Tool;
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
                Strike();
            }

            AnimateHand(dt);
        }

        private void Strike()
        {
            var ray = new Ray(_head.position, _head.forward);
            if (!Physics.SphereCast(ray, AimRadius, out var hit, Reach, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide))
                return;

            var node = hit.collider.GetComponentInParent<ResourceNodeView>();
            if (node == null) return;

            var yields = node.Hit(_hotbar.HeldTool);
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

            _text.Clear();
            foreach (var y in yields)
            {
                int left = _inventory.Add(y.ItemId, y.Amount);
                int got = y.Amount - left;
                if (_text.Length > 0) _text.Append("   ");
                _text.Append('+').Append(got).Append(' ').Append(_catalog.Get(y.ItemId).DisplayName);
                if (left > 0) _text.Append(" (inventario lleno)");
            }
            Message?.Invoke(_text.ToString());
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
