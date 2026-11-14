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
        private static Sprite _soft;
        private static Sprite _outline;

        public static Font Font => _font != null ? _font : (_font = UiTheme.Regular);

        /// <summary>A filled circle with a soft edge.</summary>
        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeCircle(128, 0f));

        /// <summary>A circle outline over a faint fill, for the joystick base.</summary>
        public static Sprite Ring => _ring != null ? _ring : (_ring = MakeCircle(256, 0.1f));

        /// <summary>A rounded rectangle that stretches without blurring its corners (9-sliced).</summary>
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = MakeRounded(64, 22f, 0f));

        /// <summary>A rectangle with small rounded corners (8 px at full size): panels, slots, buttons.</summary>
        public static Sprite Soft => _soft != null ? _soft : (_soft = MakeRounded(32, 9f, 0f));

        /// <summary>The outline of <see cref="Soft"/>, 3 px wide: selection frames.</summary>
        public static Sprite Outline => _outline != null ? _outline : (_outline = MakeRounded(32, 9f, 3f));

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

        public static Text AddText(RectTransform rt, string value, int size, TextAnchor alignment, Color colour, bool bold = false)
        {
            var text = rt.gameObject.AddComponent<Text>();
            text.font = bold ? UiTheme.Bold : Font;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = colour;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        /// <summary>A rect stretched over its parent with a margin on every side.</summary>
        public static RectTransform Inset(string name, Transform parent, float margin)
        {
            var rt = NewRect(name, parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(margin, margin);
            rt.offsetMax = new Vector2(-margin, -margin);
            return rt;
        }

        /// <summary>An icon sprite in a square, tinted.</summary>
        public static Image AddIcon(RectTransform parent, Sprite icon, Vector2 position, float size, Color colour,
            Vector2? anchor = null)
        {
            var a = anchor ?? new Vector2(0.5f, 0.5f);
            var rt = NewRect("Icon", parent, a, new Vector2(0.5f, 0.5f), position, new Vector2(size, size));
            var image = AddImage(rt, icon, colour);
            image.preserveAspect = true;
            return image;
        }

        /// <summary>Stretches a RectTransform over its whole parent.</summary>
        public static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Sprite _vignette;

        /// <summary>Clear in the middle, darkening towards the edges and most in the corners (for the hurt flash).</summary>
        public static Sprite Vignette => _vignette != null ? _vignette : (_vignette = MakeVignette(128));

        private static Sprite MakeVignette(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v) / 1.41421356f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 1f, d));
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
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

        /// <param name="ring">0 for a filled shape, else the width of an outline in texture pixels.</param>
        private static Sprite MakeRounded(int size, float radius, float ring)
        {
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
                float alpha = Mathf.Clamp01(radius - d);
                if (ring > 0f) alpha *= Mathf.Clamp01(d - (radius - ring) + 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            float border = radius + 2f;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
    }
}
