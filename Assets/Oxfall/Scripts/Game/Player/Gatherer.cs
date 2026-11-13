using System;
using System.Text;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Game.Controls;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Player
{
    /// <summary>
    /// Swings the held tool while attack is held, and hits whatever resource is in front of the
    /// camera. What a hit gives is decided by the Core rules; this adds it to the inventory.
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
        private ItemCatalog _catalog;
        private Transform _head;
        private Transform _hand;
        private Vector3 _handRest;
        private float _cooldown;
        private float _swing = 10f;
        private readonly StringBuilder _text = new StringBuilder();

        /// <summary>The tool used to hit. Only the rock exists in the first build.</summary>
        public ToolKind Tool { get; set; } = ToolKind.Rock;

        public void Init(PlayerInput input, Inventory inventory, ItemCatalog catalog, Transform head, Mesh handMesh,
            Material handMaterial)
        {
            _input = input;
            _inventory = inventory;
            _catalog = catalog;
            _head = head;

            var hand = new GameObject("Held rock");
            hand.transform.SetParent(head, false);
            _handRest = new Vector3(0.34f, -0.34f, 0.62f);
            hand.transform.localPosition = _handRest;
            hand.transform.localRotation = Quaternion.Euler(10f, -20f, 15f);
            hand.transform.localScale = Vector3.one * 0.12f; // 0.16 covered the jump button on phones
            hand.AddComponent<MeshFilter>().sharedMesh = handMesh;
            var renderer = hand.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = handMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _hand = hand.transform;
        }

        private void Update()
        {
            if (_input == null) return;
            float dt = Time.deltaTime;
            _cooldown -= dt;

            if (_input.Attack && _cooldown <= 0f)
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

            var yields = node.Hit(BestTool(node.Node.Type));
            if (yields.Count == 0) return;

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

        /// <summary>The best tool the player owns for this kind of node; the bare rock otherwise.</summary>
        private ToolKind BestTool(ResourceNodeType type)
        {
            switch (type)
            {
                case ResourceNodeType.Tree:
                    return _inventory.Has(ItemIds.StoneHatchet, 1) ? ToolKind.Hatchet : Tool;
                case ResourceNodeType.Stone:
                case ResourceNodeType.MetalOre:
                    return _inventory.Has(ItemIds.StonePickaxe, 1) ? ToolKind.Pickaxe : Tool;
                default:
                    return Tool;
            }
        }

        /// <summary>A quick chop: down and forward, then back.</summary>
        private void AnimateHand(float dt)
        {
            if (_hand == null) return;
            _swing += dt;
            float t = Mathf.Clamp01(_swing / (SwingSeconds * 0.6f));
            float arc = Mathf.Sin(t * Mathf.PI);
            _hand.localPosition = _handRest + new Vector3(-0.08f * arc, -0.06f * arc, 0.22f * arc);
            _hand.localRotation = Quaternion.Euler(10f + 55f * arc, -20f, 15f);
        }
    }
}
