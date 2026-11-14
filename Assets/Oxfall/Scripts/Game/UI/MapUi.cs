using System.Collections;
using System.Collections.Generic;
using Oxfall.Game.World;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The island seen from above, as the PC game's map: painted from the ground itself (sea and
    /// shallows, beaches, grass and woods, bare rock, ponds), shaded by its hills, under a lettered
    /// grid; where the player stands and faces, their sleeping bags and cupboards, and where they
    /// last died. Opened by tapping the compass; any tap closes it.
    /// </summary>
    public sealed class MapUi
    {
        /// <summary>Metres across the map: the whole island and a ring of sea round it.</summary>
        public const float Extent = 400f;

        /// <summary>Grid squares across, lettered A… left to right and numbered 1… top to bottom.</summary>
        public const int Grid = 8;

        private const int TexSize = 320;
        private const float MapPixels = 860f;

        private readonly Hud _hud;
        private readonly IslandShape _shape;
        private readonly RectTransform _root;
        private readonly RectTransform _map;
        private readonly RawImage _image;
        private readonly RectTransform _you;
        private readonly Text _where;
        private readonly RectTransform _pinLayer;
        private readonly List<(RectTransform Rect, Image Icon, Text Label)> _pins = new List<(RectTransform, Image, Text)>();
        private Texture2D _texture;
        private bool _painting;
        private RectTransform _modalBefore;

        /// <summary>Something to mark on the map.</summary>
        public readonly struct Pin
        {
            public readonly Vector3 Position;
            public readonly Sprite Icon;
            public readonly Color Colour;
            public readonly string Label;

            public Pin(Vector3 position, Sprite icon, Color colour, string label = null)
            {
                Position = position;
                Icon = icon;
                Colour = colour;
                Label = label;
            }
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        public MapUi(Hud hud, RectTransform parent, IslandShape shape)
        {
            _hud = hud;
            _shape = shape;
            var centre = new Vector2(0.5f, 0.5f);

            _root = UiFactory.NewRect("Map", parent, centre, centre, Vector2.zero, Vector2.zero);
            UiFactory.Fill(_root);
            UiFactory.AddImage(_root, null, new Color(0.02f, 0.025f, 0.03f, 0.86f));
            hud.TapTargets.Add(new Hud.TapTarget { Rect = _root, OnTap = Close });

            // The sheet: a thin frame round the painted island.
            var frame = UiFactory.NewRect("Frame", _root, centre, centre, new Vector2(0f, -6f), new Vector2(MapPixels + 16f, MapPixels + 16f));
            UiFactory.AddImage(frame, UiFactory.Soft, new Color(0.11f, 0.12f, 0.13f, 0.98f));
            _map = UiFactory.NewRect("Island", frame, centre, centre, Vector2.zero, new Vector2(MapPixels, MapPixels));
            _image = _map.gameObject.AddComponent<RawImage>();
            _image.raycastTarget = false;
            _image.color = new Color(0.13f, 0.27f, 0.35f);

            // Grid letters along the top, numbers down the left.
            float cell = MapPixels / Grid;
            for (int i = 0; i < Grid; i++)
            {
                var letter = UiFactory.NewRect("Col " + i, _map, new Vector2(0f, 1f), centre, new Vector2((i + 0.5f) * cell, 18f), new Vector2(cell, 30f));
                UiFactory.AddText(letter, ((char)('A' + i)).ToString(), 22, TextAnchor.MiddleCenter, UiTheme.TextMuted, true);
                var number = UiFactory.NewRect("Row " + i, _map, new Vector2(0f, 1f), centre, new Vector2(-20f, -(i + 0.5f) * cell), new Vector2(30f, cell));
                UiFactory.AddText(number, (i + 1).ToString(), 22, TextAnchor.MiddleCenter, UiTheme.TextMuted, true);
            }

            _pinLayer = UiFactory.NewRect("Pins", _map, centre, centre, Vector2.zero, Vector2.zero);
            UiFactory.Fill(_pinLayer);
            _you = UiFactory.NewRect("You", _map, centre, centre, Vector2.zero, new Vector2(44f, 44f));
            var halo = UiFactory.NewRect("Halo", _you, centre, centre, Vector2.zero, new Vector2(72f, 72f));
            UiFactory.AddImage(halo, Icons.Glow, UiTheme.Accent.WithAlpha(0.55f));
            UiFactory.AddImage(_you, Icons.Pointer, Color.white);
            var pointer = UiFactory.NewRect("Arrow", _you, centre, centre, Vector2.zero, new Vector2(34f, 34f));
            UiFactory.AddImage(pointer, Icons.Pointer, UiTheme.Accent);

            // Title on the left, where the player is under it; closing on the right.
            var left = new Vector2(0f, 0.5f);
            var title = UiFactory.NewRect("Title", _root, left, left, new Vector2(60f, 360f), new Vector2(300f, 60f));
            UiFactory.AddText(title, "MAPA", 46, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var bar = UiFactory.NewRect("Bar", _root, left, left, new Vector2(62f, 322f), new Vector2(54f, 5f));
            UiFactory.AddImage(bar, null, UiTheme.Accent);
            var where = UiFactory.NewRect("Where", _root, left, left, new Vector2(60f, 270f), new Vector2(320f, 40f));
            _where = UiFactory.AddText(where, "", 28, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            AddLegend(_root, left, new Vector2(60f, 200f), Icons.Pointer, UiTheme.Accent, "Tú");
            AddLegend(_root, left, new Vector2(60f, 155f), Icons.Bed, new Color(0.55f, 0.78f, 1f), "Saco de dormir");
            AddLegend(_root, left, new Vector2(60f, 110f), Icons.Home, new Color(1f, 0.82f, 0.4f), "Tu base");
            AddLegend(_root, left, new Vector2(60f, 65f), Icons.Ruins, RuinsColour, "Ruinas: botín");
            AddLegend(_root, left, new Vector2(60f, 20f), Icons.Close, UiTheme.Bad, "Donde moriste");
            var hint = UiFactory.NewRect("Hint", _root, new Vector2(0.5f, 0f), centre, new Vector2(0f, 16f), new Vector2(600f, 30f));
            UiFactory.AddText(hint, "Toca para cerrar", 22, TextAnchor.MiddleCenter, UiTheme.TextFaint);
            var close = UiFactory.NewRect("Close", _root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -20f), new Vector2(64f, 64f));
            UiFactory.AddImage(close, UiFactory.Soft, UiTheme.Panel);
            UiFactory.AddIcon(close, Icons.Close, Vector2.zero, 30f, UiTheme.Text);

            _root.gameObject.SetActive(false);
        }

        /// <summary>The ruins' pins: weathered concrete, pale against the green.</summary>
        public static readonly Color RuinsColour = new Color(0.86f, 0.84f, 0.78f);

        private static void AddLegend(RectTransform parent, Vector2 anchor, Vector2 at, Sprite icon, Color colour, string text)
        {
            var row = UiFactory.NewRect("Legend " + text, parent, anchor, anchor, at, new Vector2(320f, 40f));
            UiFactory.AddIcon(row, icon, new Vector2(16f, 0f), 30f, colour, new Vector2(0f, 0.5f));
            var label = UiFactory.NewRect("Text", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(270f, 40f));
            UiFactory.AddText(label, text, 26, TextAnchor.MiddleLeft, UiTheme.Text, true);
        }

        /// <summary>Paints the island a few rows a frame, so the game does not stall for it.</summary>
        public IEnumerator Paint()
        {
            if (_texture != null || _painting) yield break;
            _painting = true;
            int n = TexSize;
            float step = Extent / n;
            float half = Extent * 0.5f;
            // Heights first (one row of margin each side for the shading), then the colours.
            var heights = new float[(n + 2) * (n + 2)];
            for (int y = -1; y <= n; y++)
            {
                for (int x = -1; x <= n; x++)
                    heights[(y + 1) * (n + 2) + x + 1] = _shape.Height(-half + (x + 0.5f) * step, -half + (y + 0.5f) * step);
                if (y % 24 == 0) yield return null;
            }
            float H(int x, int y) => heights[(y + 1) * (n + 2) + x + 1];

            var pixels = new Color32[n * n];
            var deep = new Color(0.10f, 0.22f, 0.31f);
            var shallow = new Color(0.25f, 0.50f, 0.55f);
            var foam = new Color(0.78f, 0.80f, 0.72f);
            var wetSand = new Color(0.62f, 0.56f, 0.42f);
            var sand = new Color(0.80f, 0.72f, 0.53f);
            var meadow = new Color(0.50f, 0.56f, 0.31f);
            var grass = new Color(0.38f, 0.48f, 0.26f);
            var wood = new Color(0.22f, 0.34f, 0.19f);
            var rock = new Color(0.56f, 0.54f, 0.50f);
            var pond = new Color(0.27f, 0.50f, 0.60f);
            int gridEvery = n / Grid;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float wx = -half + (x + 0.5f) * step, wz = -half + (y + 0.5f) * step;
                    float h = H(x, y);
                    // Shading: light from the north-west, steeper slopes darker or brighter.
                    float dx = H(x + 1, y) - H(x - 1, y), dz = H(x, y + 1) - H(x, y - 1);
                    float slope = Mathf.Sqrt(dx * dx + dz * dz) / (2f * step);
                    float shade = Mathf.Clamp(1f + (-dx + dz) * 0.09f, 0.68f, 1.22f);
                    Color c;
                    if (h < IslandShape.WaterLevel)
                    {
                        c = Color.Lerp(shallow, deep, Mathf.Clamp01(-h / 7f));
                        // A pale line where the waves meet the shore.
                        bool shore = H(x + 1, y) >= 0f || H(x - 1, y) >= 0f || H(x, y + 1) >= 0f || H(x, y - 1) >= 0f;
                        if (shore) c = Color.Lerp(c, foam, 0.6f);
                        shade = 1f;
                    }
                    else if (_shape.PondAt(wx, wz, out var p) && h < p.Level + 0.05f)
                    {
                        c = pond;
                        shade = 1f;
                    }
                    else
                    {
                        if (h < 0.5f) c = Color.Lerp(wetSand, sand, h / 0.5f);
                        else if (h < 2.2f) c = Color.Lerp(sand, meadow, Mathf.InverseLerp(1.5f, 2.2f, h));
                        else
                        {
                            float forest = _shape.Forest(wx, wz);
                            c = Color.Lerp(meadow, grass, Mathf.InverseLerp(0.3f, 0.45f, forest));
                            c = Color.Lerp(c, wood, Mathf.InverseLerp(0.46f, 0.62f, forest));
                        }
                        c = Color.Lerp(c, rock, Mathf.Clamp01((slope - 0.45f) * 2.5f) + Mathf.Clamp01((h - 17f) / 5f));
                        // The ruins' concrete pads.
                        if (Oxfall.Core.World.Monuments.Inside(_shape.Ground, wx, wz, -2.5f)) c = new Color(0.66f, 0.65f, 0.61f);
                    }
                    c *= shade;
                    // The grid: thin dark lines.
                    if (x % gridEvery == 0 || y % gridEvery == 0) c = Color.Lerp(c, new Color(0.05f, 0.06f, 0.07f), 0.35f);
                    pixels[y * n + x] = new Color(c.r, c.g, c.b, 1f);
                }
                if (y % 16 == 0) yield return null;
            }
            _texture = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "Island map",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _texture.SetPixels32(pixels);
            _texture.Apply(false, true);
            _image.texture = _texture;
            _image.color = Color.white;
            _painting = false;
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            // While it shows, only it takes taps (and the stick and looking stop).
            _modalBefore = _hud.Modal;
            _hud.Modal = _root;
        }

        public void Close()
        {
            if (!IsOpen) return;
            _root.gameObject.SetActive(false);
            if (_hud.Modal == _root) _hud.Modal = _modalBefore;
            _modalBefore = null;
        }

        /// <summary>Where a point on the island falls on the map, in map pixels from its middle.</summary>
        public static Vector2 ToMap(Vector3 world) => new Vector2(world.x, world.z) / Extent * MapPixels;

        /// <summary>The grid square of a point, as "C4".</summary>
        public static string Square(Vector3 world)
        {
            int col = Mathf.Clamp(Mathf.FloorToInt((world.x / Extent + 0.5f) * Grid), 0, Grid - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt((0.5f - world.z / Extent) * Grid), 0, Grid - 1);
            return (char)('A' + col) + (row + 1).ToString();
        }

        /// <summary>Moves the marks: the player (where, facing), and the pins.</summary>
        public void Refresh(Vector3 player, float yaw, IReadOnlyList<Pin> pins)
        {
            if (!IsOpen) return;
            _you.anchoredPosition = Clamp(ToMap(player));
            _you.localRotation = Quaternion.Euler(0f, 0f, -yaw);
            _where.text = "Estás en " + Square(player);
            for (int i = 0; i < pins.Count; i++)
            {
                if (i >= _pins.Count)
                {
                    var rect = UiFactory.NewRect("Pin", _pinLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
                    var back = UiFactory.NewRect("Back", rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f));
                    UiFactory.AddImage(back, UiFactory.Circle, new Color(0.05f, 0.06f, 0.07f, 0.72f));
                    var icon = UiFactory.AddIcon(rect, null, Vector2.zero, 28f, Color.white);
                    var labelRect = UiFactory.NewRect("Label", rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(200f, 26f));
                    var label = UiFactory.AddText(labelRect, "", 20, TextAnchor.MiddleCenter, UiTheme.Text, true);
                    _pins.Add((rect, icon, label));
                }
                var pin = pins[i];
                var view = _pins[i];
                view.Rect.gameObject.SetActive(true);
                view.Rect.anchoredPosition = Clamp(ToMap(pin.Position));
                view.Icon.sprite = pin.Icon;
                view.Icon.color = pin.Colour;
                view.Label.text = pin.Label ?? "";
            }
            for (int i = pins.Count; i < _pins.Count; i++) _pins[i].Rect.gameObject.SetActive(false);
            _you.SetAsLastSibling();
        }

        private static Vector2 Clamp(Vector2 p)
        {
            float edge = MapPixels * 0.5f - 12f;
            return new Vector2(Mathf.Clamp(p.x, -edge, edge), Mathf.Clamp(p.y, -edge, edge));
        }
    }
}
