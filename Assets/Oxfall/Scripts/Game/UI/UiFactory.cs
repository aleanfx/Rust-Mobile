using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// Small helpers to build uGUI in code: sprites drawn at start-up and anchored elements. No
    /// prefabs or imported art, so the whole HUD lives in source control as text.
    /// </summary>
    public static class UiFactory
    {
        private static Font _font;
        private static Sprite _circle;
        private static Sprite _ring;
        private static Sprite _rounded;

        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        /// <summary>A filled circle with a soft edge.</summary>
        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeCircle(128, 0f));

        /// <summary>A circle outline over a faint fill, for the joystick base.</summary>
        public static Sprite Ring => _ring != null ? _ring : (_ring = MakeCircle(256, 0.1f));

        /// <summary>A rounded rectangle that stretches without blurring its corners (9-sliced).</summary>
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = MakeRounded());

        public static RectTransform NewRect(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image AddImage(RectTransform rt, Sprite sprite, Color colour)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
            return image;
        }

        public static Text AddText(RectTransform rt, string value, int size, TextAnchor alignment, Color colour)
        {
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = colour;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        /// <summary>Stretches a RectTransform over its whole parent.</summary>
        public static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Sprite MakeCircle(int size, float fillAlpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            float ringWidth = size * 0.04f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float edge = Mathf.Clamp01(r - d);              // 1 inside, soft 1px edge
                float alpha;
                if (fillAlpha <= 0f) alpha = edge;
                else
                {
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - (r - ringWidth)) / ringWidth);
                    alpha = Mathf.Max(ring, fillAlpha) * edge;
                }
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite MakeRounded()
        {
            const int size = 64;
            const float radius = 22f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float py = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(px, py));
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
        }
    }
}
