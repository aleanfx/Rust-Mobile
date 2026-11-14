using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The interface's pictograms, drawn once at start-up from signed distance shapes into small
    /// white textures (tinted by the Image that shows them). No image files, crisp at any size the
    /// HUD uses, and every icon shares one stroke weight so the set reads as one family.
    /// </summary>
    public static class Icons
    {
        private const int Size = 128;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        // Shapes are drawn in a box from -1 to 1, y up. Distances are negative inside.
        private const float Stroke = 0.11f;

        public static Sprite Heart => Get("heart", p =>
            Min(Circle(p, new Vector2(-0.34f, 0.26f), 0.38f), Circle(p, new Vector2(0.34f, 0.26f), 0.38f),
                // Sides tangent to the two lobes, so the outline runs smooth into the point.
                Polygon(p, new Vector2(-0.638f, 0.024f), new Vector2(0f, -0.78f), new Vector2(0.638f, 0.024f), new Vector2(0f, 0.3f))));

        public static Sprite Drop => Get("drop", p =>
            Mathf.Min(Circle(p, new Vector2(0f, -0.28f), 0.52f),
                Polygon(p, new Vector2(-0.47f, -0.10f), new Vector2(0.47f, -0.10f), new Vector2(0f, 0.86f))));

        /// <summary>An apple: reads as "food" even at the size of a vital's icon.</summary>
        public static Sprite Food => Get("food", p =>
        {
            float body = Mathf.Min(Circle(p, new Vector2(-0.2f, -0.1f), 0.5f), Circle(p, new Vector2(0.2f, -0.1f), 0.5f));
            body = Mathf.Max(body, -Circle(p, new Vector2(0f, 0.52f), 0.18f));
            body = Mathf.Max(body, -Circle(p, new Vector2(0f, -0.8f), 0.12f));
            return Min(body, Segment(p, new Vector2(0f, 0.28f), new Vector2(0.1f, 0.72f), 0.065f),
                RotatedBox(p, new Vector2(0.36f, 0.64f), new Vector2(0.24f, 0.09f), 25f, 0.09f));
        });

        /// <summary>A pencil, slanted: "change this".</summary>
        public static Sprite Pencil => Get("pencil", p =>
            Min(RotatedBox(p, new Vector2(0.08f, 0.08f), new Vector2(0.62f, 0.15f), 45f, 0.04f),
                Polygon(p, new Vector2(-0.48f, -0.27f), new Vector2(-0.27f, -0.48f), new Vector2(-0.72f, -0.72f))));

        public static Sprite Bag => Get("bag", p =>
        {
            float body = RoundBox(p, new Vector2(0f, -0.12f), new Vector2(0.58f, 0.62f), 0.22f);
            float handle = Mathf.Abs(Circle(p, new Vector2(0f, 0.55f), 0.26f)) - Stroke * 0.8f;
            float pocket = RoundBox(p, new Vector2(0f, -0.38f), new Vector2(0.36f, 0.2f), 0.08f);
            float flap = Segment(p, new Vector2(-0.58f, 0.12f), new Vector2(0.58f, 0.12f), 0.05f);
            return Mathf.Max(Mathf.Min(body, Mathf.Max(handle, -(p.y - 0.5f))), -Mathf.Min(Mathf.Abs(pocket) - 0.04f, flap));
        });

        /// <summary>A wooden box seen from the front: lid, body and latch.</summary>
        public static Sprite Box => Get("box", p =>
        {
            float body = RoundBox(p, new Vector2(0f, -0.08f), new Vector2(0.7f, 0.52f), 0.1f);
            float lid = Segment(p, new Vector2(-0.8f, 0.16f), new Vector2(0.8f, 0.16f), 0.05f);
            float latch = RoundBox(p, new Vector2(0f, 0.12f), new Vector2(0.1f, 0.14f), 0.03f);
            return Mathf.Min(Mathf.Max(body, -lid), latch);
        });

        /// <summary>A flame with a hollow heart.</summary>
        public static Sprite Flame => Get("flame", p =>
        {
            float bulb = Circle(p, new Vector2(0f, -0.3f), 0.45f);
            float tip = Polygon(p, new Vector2(-0.41f, -0.15f), new Vector2(0.41f, -0.15f), new Vector2(0.08f, 0.8f));
            float heart = Circle(p, new Vector2(0f, -0.38f), 0.17f);
            return Mathf.Max(Mathf.Min(bulb, tip), -heart);
        });

        /// <summary>A shield (the tool cupboard's protection) with an open middle.</summary>
        public static Sprite Shield => Get("shield", p =>
        {
            float outer = Polygon(p, new Vector2(-0.62f, 0.66f), new Vector2(0.62f, 0.66f), new Vector2(0.62f, 0.06f),
                new Vector2(0f, -0.78f), new Vector2(-0.62f, 0.06f));
            float inner = Polygon(p, new Vector2(-0.4f, 0.47f), new Vector2(0.4f, 0.47f), new Vector2(0.4f, 0.06f),
                new Vector2(0f, -0.48f), new Vector2(-0.4f, 0.06f));
            return Mathf.Max(outer, -inner);
        });

        // Clothing silhouettes for the empty wear slots.

        /// <summary>A hood over a head.</summary>
        public static Sprite Hood => Get("hood", p =>
            Mathf.Max(Polygon(p, new Vector2(-0.56f, -0.62f), new Vector2(-0.6f, 0.05f), new Vector2(-0.36f, 0.55f), new Vector2(0f, 0.72f),
                new Vector2(0.36f, 0.55f), new Vector2(0.6f, 0.05f), new Vector2(0.56f, -0.62f), new Vector2(0.22f, -0.7f), new Vector2(-0.22f, -0.7f)),
                -RoundBox(p, new Vector2(0f, -0.06f), new Vector2(0.26f, 0.34f), 0.24f)));

        /// <summary>A shirt with short sleeves.</summary>
        public static Sprite Shirt => Get("shirt", p =>
            Polygon(p, new Vector2(-0.4f, -0.72f), new Vector2(0.4f, -0.72f), new Vector2(0.4f, 0.12f), new Vector2(0.6f, -0.04f),
                new Vector2(0.8f, 0.3f), new Vector2(0.42f, 0.66f), new Vector2(0.16f, 0.66f), new Vector2(0f, 0.5f),
                new Vector2(-0.16f, 0.66f), new Vector2(-0.42f, 0.66f), new Vector2(-0.8f, 0.3f), new Vector2(-0.6f, -0.04f),
                new Vector2(-0.4f, 0.12f)));

        /// <summary>A pair of trousers.</summary>
        public static Sprite Trousers => Get("trousers", p =>
            Polygon(p, new Vector2(-0.42f, 0.7f), new Vector2(0.42f, 0.7f), new Vector2(0.5f, -0.76f), new Vector2(0.1f, -0.76f),
                new Vector2(0f, 0.2f), new Vector2(-0.1f, -0.76f), new Vector2(-0.5f, -0.76f)));

        /// <summary>A boot, toe to the right.</summary>
        public static Sprite Boot => Get("boot", p =>
            Polygon(p, new Vector2(-0.46f, 0.72f), new Vector2(0.06f, 0.72f), new Vector2(0.08f, -0.08f), new Vector2(0.56f, -0.26f),
                new Vector2(0.7f, -0.5f), new Vector2(0.68f, -0.7f), new Vector2(-0.5f, -0.7f)));

        /// <summary>A sleeping bag with its pillow.</summary>
        public static Sprite Bed => Get("bed", p =>
            Mathf.Min(RoundBox(p, new Vector2(0.14f, -0.12f), new Vector2(0.62f, 0.26f), 0.14f),
                RoundBox(p, new Vector2(-0.6f, 0.12f), new Vector2(0.2f, 0.15f), 0.08f)));

        public static Sprite Hammer => Get("hammer", p =>
            Mathf.Min(Segment(p, new Vector2(0.18f, 0.18f), new Vector2(-0.64f, -0.64f), 0.1f),
                RotatedBox(p, new Vector2(0.28f, 0.28f), new Vector2(0.52f, 0.2f), -45f, 0.05f)));

        /// <summary>A round arrow, clockwise, its head at the right: reload.</summary>
        public static Sprite Reload => Get("reload", p =>
        {
            float ring = Mathf.Abs(Circle(p, Vector2.zero, 0.5f)) - Stroke * 0.85f;
            float angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            // A gap between the arrow's tail (top) and its head (right).
            if (angle > 12f && angle < 78f) ring = Mathf.Max(ring, 0.06f);
            var tip = new Vector2(0.5f, -0.2f);
            float head = Polygon(p, new Vector2(0.24f, 0.13f), new Vector2(0.76f, 0.13f), tip);
            return Mathf.Min(ring, head);
        });

        /// <summary>Two round arrows chasing each other: a recycler.</summary>
        public static Sprite Recycle => Get("recycle", p =>
        {
            float ring = Mathf.Abs(Circle(p, Vector2.zero, 0.5f)) - Stroke * 0.85f;
            float angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            // Two gaps, each between one arrow's head and the other's tail.
            if ((angle > 12f && angle < 78f) || (angle > -168f && angle < -102f)) ring = Mathf.Max(ring, 0.06f);
            float right = Polygon(p, new Vector2(0.24f, 0.13f), new Vector2(0.76f, 0.13f), new Vector2(0.5f, -0.2f));
            float left = Polygon(p, new Vector2(-0.24f, -0.13f), new Vector2(-0.76f, -0.13f), new Vector2(-0.5f, 0.2f));
            return Min(ring, right, left);
        });

        public static Sprite Jump => Get("jump", p =>
            Mathf.Min(Chevron(p, 0.5f, 0.48f), Chevron(p, -0.02f, 0.48f)));

        public static Sprite Gear => Get("gear", p =>
        {
            float ring = Mathf.Max(Circle(p, Vector2.zero, 0.58f), -Circle(p, Vector2.zero, 0.24f));
            float teeth = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var c = Quaternion.Euler(0f, 0f, a) * new Vector3(0.66f, 0f, 0f);
                teeth = Mathf.Min(teeth, RotatedBox(p, c, new Vector2(0.17f, 0.15f), a, 0.04f));
            }
            return Mathf.Max(Mathf.Min(ring, teeth), -Circle(p, Vector2.zero, 0.24f));
        });

        /// <summary>A padlock, shut: a code lock that keeps you out.</summary>
        public static Sprite Lock => Get("lock", p => Padlock(p, false));

        /// <summary>A padlock with its shackle up: an open lock.</summary>
        public static Sprite Unlock => Get("unlock", p => Padlock(p, true));

        /// <summary>Nine keys of a keypad.</summary>
        public static Sprite Keypad => Get("keypad", p =>
        {
            float d = float.MaxValue;
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                d = Mathf.Min(d, RoundBox(p, new Vector2(x * 0.52f, y * 0.52f), new Vector2(0.19f, 0.19f), 0.06f));
            return d;
        });

        private static float Padlock(Vector2 p, bool open)
        {
            float body = RoundBox(p, new Vector2(0f, -0.3f), new Vector2(0.6f, 0.44f), 0.12f);
            float keyhole = Mathf.Min(Circle(p, new Vector2(0f, -0.22f), 0.11f),
                Segment(p, new Vector2(0f, -0.26f), new Vector2(0f, -0.48f), 0.05f));
            // The shackle: an arch over the body (lifted and swung to one side when open).
            var c = open ? new Vector2(0.28f, 0.42f) : new Vector2(0f, 0.22f);
            float arch = Mathf.Abs(Circle(p, c, 0.34f)) - Stroke * 0.9f;
            arch = Mathf.Max(arch, -(p.y - c.y));
            float legs = Mathf.Min(Segment(p, new Vector2(c.x - 0.34f, c.y), new Vector2(c.x - 0.34f, open ? 0.08f : -0.1f), Stroke * 0.9f),
                Segment(p, new Vector2(c.x + 0.34f, c.y), new Vector2(c.x + 0.34f, open ? c.y - 0.04f : -0.1f), Stroke * 0.9f));
            return Mathf.Max(Mathf.Min(body, Mathf.Min(arch, legs)), -keyhole);
        }

        public static Sprite Door => Get("door", p =>
            Mathf.Min(Mathf.Abs(RoundBox(p, new Vector2(0f, 0f), new Vector2(0.46f, 0.78f), 0.06f)) - Stroke * 0.8f,
                Circle(p, new Vector2(0.22f, -0.02f), 0.1f)));

        public static Sprite Sun => Get("sun", p =>
        {
            float d = Circle(p, Vector2.zero, 0.36f);
            for (int i = 0; i < 8; i++)
            {
                var dir = (Vector2)(Quaternion.Euler(0f, 0f, i * 45f) * Vector3.right);
                d = Mathf.Min(d, Segment(p, dir * 0.58f, dir * 0.8f, 0.08f));
            }
            return d;
        });

        public static Sprite Moon => Get("moon", p =>
            Mathf.Max(Circle(p, Vector2.zero, 0.66f), -Circle(p, new Vector2(0.34f, 0.22f), 0.56f)));

        /// <summary>A speech bubble with three dots: "say something".</summary>
        public static Sprite Chat => Get("chat", p =>
        {
            float bubble = Mathf.Min(RoundBox(p, new Vector2(0f, 0.1f), new Vector2(0.72f, 0.5f), 0.26f),
                Polygon(p, new Vector2(-0.42f, -0.3f), new Vector2(-0.08f, -0.3f), new Vector2(-0.52f, -0.74f)));
            float dots = Min(Circle(p, new Vector2(-0.34f, 0.1f), 0.1f), Circle(p, new Vector2(0f, 0.1f), 0.1f), Circle(p, new Vector2(0.34f, 0.1f), 0.1f));
            return Mathf.Max(bubble, -dots);
        });

        /// <summary>A folded map: three panels, zigzag at top and bottom.</summary>
        public static Sprite Map => Get("map", p =>
        {
            float sheet = Mathf.Abs(Polygon(p, new Vector2(-0.72f, -0.52f), new Vector2(-0.24f, -0.66f), new Vector2(0.24f, -0.52f), new Vector2(0.72f, -0.66f),
                new Vector2(0.72f, 0.54f), new Vector2(0.24f, 0.68f), new Vector2(-0.24f, 0.54f), new Vector2(-0.72f, 0.68f))) - Stroke * 0.8f;
            return Min(sheet, Segment(p, new Vector2(-0.24f, -0.66f), new Vector2(-0.24f, 0.54f), 0.06f),
                Segment(p, new Vector2(0.24f, -0.52f), new Vector2(0.24f, 0.68f), 0.06f));
        });

        /// <summary>An arrowhead pointing up: where the player is and which way they face.</summary>
        public static Sprite Pointer => Get("pointer", p =>
            Polygon(p, new Vector2(0f, 0.8f), new Vector2(0.56f, -0.62f), new Vector2(0f, -0.3f), new Vector2(-0.56f, -0.62f)));

        /// <summary>A small house with a door: a base.</summary>
        public static Sprite Home => Get("home", p =>
            Mathf.Max(Polygon(p, new Vector2(-0.6f, -0.62f), new Vector2(0.6f, -0.62f), new Vector2(0.6f, 0.12f), new Vector2(0f, 0.7f), new Vector2(-0.6f, 0.12f)),
                -RoundBox(p, new Vector2(0f, -0.38f), new Vector2(0.15f, 0.26f), 0.04f)));

        /// <summary>A broken wall: two blocks of a ruin, the top one cracked off at a slant.</summary>
        public static Sprite Ruins => Get("ruins", p =>
            Min(Polygon(p, new Vector2(-0.7f, -0.62f), new Vector2(0.7f, -0.62f), new Vector2(0.7f, 0.05f), new Vector2(0.2f, 0.32f), new Vector2(-0.1f, 0.12f),
                    new Vector2(-0.38f, 0.5f), new Vector2(-0.7f, 0.5f)),
                RoundBox(p, new Vector2(0.48f, 0.5f), new Vector2(0.14f, 0.12f), 0.03f)));

        public static Sprite Close => Get("close", p =>
            Mathf.Min(Segment(p, new Vector2(-0.5f, -0.5f), new Vector2(0.5f, 0.5f), Stroke),
                Segment(p, new Vector2(-0.5f, 0.5f), new Vector2(0.5f, -0.5f), Stroke)));

        public static Sprite Hand => Get("hand", p =>
            Min(RoundBox(p, new Vector2(0f, -0.3f), new Vector2(0.42f, 0.38f), 0.2f),
                Segment(p, new Vector2(-0.3f, -0.1f), new Vector2(-0.3f, 0.5f), 0.1f),
                Segment(p, new Vector2(-0.1f, -0.1f), new Vector2(-0.1f, 0.68f), 0.1f),
                Segment(p, new Vector2(0.1f, -0.1f), new Vector2(0.1f, 0.62f), 0.1f),
                Segment(p, new Vector2(0.3f, -0.1f), new Vector2(0.3f, 0.46f), 0.1f),
                Segment(p, new Vector2(-0.4f, -0.3f), new Vector2(-0.68f, 0.05f), 0.1f)));

        public static Sprite Check => Get("check", p =>
            Mathf.Min(Segment(p, new Vector2(-0.55f, 0.02f), new Vector2(-0.15f, -0.4f), Stroke),
                Segment(p, new Vector2(-0.15f, -0.4f), new Vector2(0.6f, 0.45f), Stroke)));

        public static Sprite Clock => Get("clock", p =>
            Min(Mathf.Abs(Circle(p, Vector2.zero, 0.66f)) - Stroke * 0.7f,
                Segment(p, Vector2.zero, new Vector2(0f, 0.42f), 0.08f),
                Segment(p, Vector2.zero, new Vector2(0.3f, -0.1f), 0.08f)));

        /// <summary>A soft round glow, bright in the middle and fading to nothing at the edge.</summary>
        public static Sprite Glow
        {
            get
            {
                if (Cache.TryGetValue("glow", out var s)) return s;
                var pixels = new Color32[Size * Size];
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f).magnitude;
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * Size + x] = new Color(1f, 1f, 1f, a * a);
                }
                return Cache["glow"] = Make("glow", pixels);
            }
        }

        // ---- Drawing

        private static Sprite Get(string name, Func<Vector2, float> shape)
        {
            if (Cache.TryGetValue(name, out var sprite)) return sprite;
            var pixels = new Color32[Size * Size];
            float pixel = 2f / Size;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // Inset a little so strokes never touch the texture's edge.
                var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f) * 1.08f;
                float d = shape(p);
                float a = Mathf.Clamp01(0.5f - d / (pixel * 1.08f));
                pixels[y * Size + x] = new Color(1f, 1f, 1f, a);
            }
            sprite = Make(name, pixels);
            Cache[name] = sprite;
            return sprite;
        }

        private static Sprite Make(string name, Color32[] pixels)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "Icon " + name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static float Min(params float[] d)
        {
            float m = d[0];
            for (int i = 1; i < d.Length; i++) m = Mathf.Min(m, d[i]);
            return m;
        }

        private static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            var pa = p - a;
            var ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        private static float RoundBox(Vector2 p, Vector2 c, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        private static float RotatedBox(Vector2 p, Vector2 c, Vector2 half, float degrees, float r)
        {
            var local = (Vector2)(Quaternion.Euler(0f, 0f, -degrees) * (Vector3)(p - c));
            return RoundBox(local, Vector2.zero, half, r);
        }

        private static float Chevron(Vector2 p, float y, float width) =>
            Mathf.Min(Segment(p, new Vector2(-width, y - width), new Vector2(0f, y), Stroke),
                Segment(p, new Vector2(width, y - width), new Vector2(0f, y), Stroke));

        /// <summary>A convex polygon, its corners given counter-clockwise or clockwise.</summary>
        private static float Polygon(Vector2 p, params Vector2[] v)
        {
            // Exact distance for convex and concave polygons (sign by winding crossings).
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                var e = v[j] - v[i];
                var w = p - v[i];
                var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }
    }
}
