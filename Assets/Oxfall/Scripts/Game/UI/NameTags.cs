using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// Names over other players' heads, drawn on the HUD (crisp at any distance, no 3D text): only
    /// for players in front of the camera and near enough, fading out with distance.
    /// </summary>
    public sealed class NameTags
    {
        private const float MaxDistance = 45f;
        private readonly RectTransform _root;
        private readonly List<Text> _pool = new List<Text>();
        private int _used;

        public NameTags(Hud hud)
        {
            _root = UiFactory.NewRect("Name tags", hud.transform, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UiFactory.Fill(_root);
            // Its own canvas: tags move every frame, the rest of the HUD does not.
            _root.gameObject.AddComponent<Canvas>();
            _root.SetAsFirstSibling();
        }

        public void Begin() => _used = 0;

        /// <summary>One name over a head at <paramref name="head"/> (world position).</summary>
        public void Show(Camera camera, Vector3 head, string name)
        {
            var screen = camera.WorldToScreenPoint(head);
            if (screen.z <= 0.3f || screen.z > MaxDistance) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var local)) return;
            if (_used == _pool.Count)
            {
                var rect = UiFactory.NewRect("Tag", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(300f, 36f));
                _pool.Add(UiFactory.AddText(rect, "", 26, TextAnchor.LowerCenter, UiTheme.Text, true));
            }
            var tag = _pool[_used++];
            if (!tag.gameObject.activeSelf) tag.gameObject.SetActive(true);
            if (tag.text != name) tag.text = name;
            var colour = UiTheme.Text;
            colour.a = Mathf.Clamp01((MaxDistance - screen.z) / 10f);
            tag.color = colour;
            tag.rectTransform.anchoredPosition = local;
        }

        public void End()
        {
            for (int i = _used; i < _pool.Count; i++)
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        }
    }
}
