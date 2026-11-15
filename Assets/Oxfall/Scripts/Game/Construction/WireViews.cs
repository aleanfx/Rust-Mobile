using System;
using System.Collections.Generic;
using Oxfall.Core.Rules;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oxfall.Game.Construction
{
    /// <summary>
    /// The cables between electric things, hanging in a slight sag from the output of one to the input
    /// of the next, and the loose cable from a device to the player's hand while one is being run.
    /// </summary>
    public sealed class WireViews : MonoBehaviour
    {
        private const int Points = 12;
        private const float Width = 0.028f;

        private Settlement _settlement;
        private Material _material;
        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private LineRenderer _loose;
        private bool _dirty = true;

        /// <summary>The device a cable is being run from (0: none), and where the hand holding its end is.</summary>
        public int LooseFrom { get; set; }
        public Func<Vector3> Hand { get; set; }

        public static WireViews Create(Settlement settlement, Material material)
        {
            var views = new GameObject("Cables").AddComponent<WireViews>();
            views._settlement = settlement;
            views._material = material;
            settlement.Power.WiresChanged += () => views._dirty = true;
            settlement.DeployableRemoved += id => views._dirty = true;
            settlement.DeployableAdded += d => { if (Electricity.IsElectric(d.Kind)) views._dirty = true; };
            views._loose = views.NewLine("Loose cable");
            views._loose.gameObject.SetActive(false);
            return views;
        }

        /// <summary>Where a cable plugs into (or out of) a device, in the world.</summary>
        public static Vector3 Port(Deployable d, bool output)
        {
            var local = output ? MeshFactory.PowerOut(d.Kind) : MeshFactory.PowerIn(d.Kind);
            return new Vector3(d.X, d.Y, d.Z) + Quaternion.Euler(0f, d.Yaw, 0f) * local;
        }

        private LineRenderer NewLine(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.positionCount = Points;
            line.widthMultiplier = Width;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.generateLightingData = true;
            line.textureMode = LineTextureMode.Stretch;
            return line;
        }

        private static readonly Vector3[] Scratch = new Vector3[Points];

        /// <summary>A cable hanging between two points: lowest in the middle, by more the longer it is.</summary>
        private static void Hang(LineRenderer line, Vector3 a, Vector3 b)
        {
            float length = Vector3.Distance(a, b);
            float sag = Mathf.Min(0.6f, 0.04f + length * 0.045f);
            for (int i = 0; i < Points; i++)
            {
                float t = i / (float)(Points - 1);
                Scratch[i] = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
            }
            line.SetPositions(Scratch);
        }

        private void Rebuild()
        {
            _dirty = false;
            int n = 0;
            foreach (var w in _settlement.Power.Wires)
            {
                if (!_settlement.TryGet(w.Device, out var device) || !_settlement.TryGet(w.Source, out var source)) continue;
                if (n == _lines.Count) _lines.Add(NewLine("Cable"));
                var line = _lines[n++];
                line.gameObject.SetActive(true);
                Hang(line, Port(source, true), Port(device, false));
            }
            for (int i = n; i < _lines.Count; i++) _lines[i].gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_settlement == null) return;
            if (_dirty) Rebuild();
            bool loose = LooseFrom != 0 && Hand != null && _settlement.TryGet(LooseFrom, out var from);
            if (_loose.gameObject.activeSelf != loose) _loose.gameObject.SetActive(loose);
            if (!loose) return;
            _settlement.TryGet(LooseFrom, out from);
            Hang(_loose, Port(from, true), Hand());
        }
    }
}
