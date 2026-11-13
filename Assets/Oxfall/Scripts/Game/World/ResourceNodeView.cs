using System.Collections.Generic;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using UnityEngine;

namespace Oxfall.Game.World
{
    /// <summary>
    /// A tree, rock or plant in the world. The rules (how much it holds, what a tool gets from it,
    /// when it grows back) are the Core's <see cref="ResourceNode"/>; this only shows them.
    /// </summary>
    public sealed class ResourceNodeView : MonoBehaviour
    {
        private const float ShakeSeconds = 0.25f;

        private Renderer[] _renderers;
        private Collider[] _colliders;
        private Quaternion _restRotation;
        private float _shake;

        public ResourceNode Node { get; private set; }

        public void Init(ResourceNode node)
        {
            Node = node;
            _renderers = GetComponentsInChildren<Renderer>();
            _colliders = GetComponentsInChildren<Collider>();
            _restRotation = transform.localRotation;
            // Only runs while shaking: hundreds of idle Update calls would cost frame time for nothing.
            enabled = false;
        }

        /// <summary>Hits the node with a tool and returns what it gave (possibly nothing).</summary>
        public IReadOnlyList<Yield> Hit(ToolKind tool)
        {
            var yields = Node.Hit(tool);
            if (yields.Count > 0)
            {
                _shake = ShakeSeconds;
                enabled = true;
            }
            if (Node.IsDepleted) SetVisible(false);
            return yields;
        }

        /// <summary>Called by the world once a second or so, with the time since the last call.</summary>
        public void TickRespawn(float deltaSeconds)
        {
            if (Node.TickRespawn(deltaSeconds)) SetVisible(true);
        }

        private void Update()
        {
            _shake -= Time.deltaTime;
            if (_shake <= 0f)
            {
                transform.localRotation = _restRotation;
                enabled = false;
                return;
            }
            float t = _shake / ShakeSeconds;
            float angle = Mathf.Sin(t * Mathf.PI * 6f) * 2.5f * t;
            transform.localRotation = _restRotation * Quaternion.Euler(angle, 0f, angle * 0.6f);
        }

        private void SetVisible(bool visible)
        {
            foreach (var r in _renderers) r.enabled = visible;
            foreach (var c in _colliders) c.enabled = visible;
            if (!visible)
            {
                _shake = 0f;
                transform.localRotation = _restRotation;
                enabled = false;
            }
        }
    }
}
