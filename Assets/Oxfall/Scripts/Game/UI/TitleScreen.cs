using System;
using System.Collections.Generic;
using Oxfall.Core.World;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The first screen: the island turning slowly behind a dark veil, the name, and the way in.
    /// JUGAR EN LÍNEA joins the server (a moment of CONECTANDO while it answers), JUGAR SOLO plays
    /// alone; the survivor's name can be changed, and the next weekly wipe counts down on the right.
    /// Like the HUD, it reads taps itself, so it needs no event system.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        private const float FadeSeconds = 0.6f;

        private readonly List<(RectTransform Rect, Action OnTap)> _buttons = new List<(RectTransform, Action)>();
        private CanvasGroup _group;
        private RectTransform _connecting;
        private RectTransform _spinner;
        private Text _connectingText;
        private Text _status;
        private Text _error;
        private Text _name;
        private Text _wipe;
        private Image _playFill;
        private string _playerName;
        private bool _closing;
        private float _fade;
        private float _wipeTimer;
        private TouchScreenKeyboard _keyboard;

        /// <summary>Play on the server.</summary>
        public event Action PlayOnline;

        /// <summary>Play alone.</summary>
        public event Action PlaySolo;

        /// <summary>The survivor's name was changed (already cleaned up a little).</summary>
        public event Action<string> NameChanged;

        public bool Connecting => _connecting.gameObject.activeSelf;

        public static TitleScreen Create(string playerName, string serverInfo)
        {
            var go = new GameObject("Title screen", typeof(Canvas), typeof(CanvasScaler));
            var title = go.AddComponent<TitleScreen>();
            title._playerName = playerName;
            title.Build(serverInfo);
            return title;
        }

        private void Build(string serverInfo)
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            _group = gameObject.AddComponent<CanvasGroup>();

            var root = UiFactory.NewRect("Root", transform, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UiFactory.Fill(root);

            // The island shows through on the right; the left darkens for the words.
            var shade = UiFactory.NewRect("Shade", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            shade.anchorMin = Vector2.zero;
            shade.anchorMax = new Vector2(0.78f, 1f);
            shade.offsetMin = shade.offsetMax = Vector2.zero;
            UiFactory.AddImage(shade, Gradient(true), new Color(0.03f, 0.032f, 0.036f, 0.94f));
            var floor = UiFactory.NewRect("Floor", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            floor.anchorMin = Vector2.zero;
            floor.anchorMax = new Vector2(1f, 0.35f);
            floor.offsetMin = floor.offsetMax = Vector2.zero;
            UiFactory.AddImage(floor, Gradient(false), new Color(0.03f, 0.032f, 0.036f, 0.8f));

            var safe = UiFactory.Inset("Safe", root, 0f);
            var r = Screen.safeArea;
            if (Screen.width > 0 && Screen.height > 0)
            {
                safe.anchorMin = new Vector2(r.xMin / Screen.width, r.yMin / Screen.height);
                safe.anchorMax = new Vector2(r.xMax / Screen.width, r.yMax / Screen.height);
            }

            // The name: big and plain, an ember line, what kind of game it is.
            var left = new Vector2(0f, 0.5f);
            var logo = UiFactory.NewRect("Logo", safe, left, new Vector2(0f, 0.5f), new Vector2(120f, 250f), new Vector2(900f, 200f));
            var logoText = UiFactory.AddText(logo, "OXFALL", 200, TextAnchor.MiddleLeft, UiTheme.Text, true);
            logoText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var line = UiFactory.NewRect("Line", safe, left, new Vector2(0f, 0.5f), new Vector2(126f, 140f), new Vector2(150f, 8f));
            UiFactory.AddImage(line, null, UiTheme.Accent);
            var sub = UiFactory.NewRect("Subtitle", safe, left, new Vector2(0f, 0.5f), new Vector2(296f, 141f), new Vector2(600f, 50f));
            UiFactory.AddText(sub, "SURVIVAL ONLINE", 40, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);

            // The way in.
            var play = Button(safe, "JUGAR EN LÍNEA", new Vector2(120f, 10f), new Vector2(620f, 112f), UiTheme.Accent,
                new Color(0.08f, 0.06f, 0.05f), 50, () => PlayOnline?.Invoke());
            _playFill = play.GetComponent<Image>();
            var statusRect = UiFactory.NewRect("Status", safe, left, new Vector2(0f, 0.5f), new Vector2(124f, -70f), new Vector2(900f, 40f));
            _status = UiFactory.AddText(statusRect, serverInfo, 26, TextAnchor.MiddleLeft, UiTheme.TextMuted);
            var errorRect = UiFactory.NewRect("Error", safe, left, new Vector2(0f, 0.5f), new Vector2(124f, -106f), new Vector2(900f, 40f));
            _error = UiFactory.AddText(errorRect, "", 26, TextAnchor.MiddleLeft, UiTheme.Bad, true);

            Button(safe, "JUGAR SOLO", new Vector2(120f, -180f), new Vector2(620f, 96f), UiTheme.Panel, UiTheme.Text, 40,
                () => PlaySolo?.Invoke());

            // Who you are on the server: tap to change.
            var nameChip = UiFactory.NewRect("Name", safe, left, new Vector2(0f, 0.5f), new Vector2(120f, -300f), new Vector2(620f, 76f));
            UiFactory.AddImage(nameChip, UiFactory.Soft, new Color(1f, 1f, 1f, 0.06f));
            var nameLabel = UiFactory.NewRect("Label", nameChip, left, new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(220f, 60f));
            UiFactory.AddText(nameLabel, "SUPERVIVIENTE", 24, TextAnchor.MiddleLeft, UiTheme.TextFaint, true);
            var nameValue = UiFactory.NewRect("Value", nameChip, left, new Vector2(0f, 0.5f), new Vector2(230f, 0f), new Vector2(300f, 60f));
            _name = UiFactory.AddText(nameValue, _playerName, 34, TextAnchor.MiddleLeft, UiTheme.Text, true);
            UiFactory.AddIcon(nameChip, Icons.Pencil, new Vector2(-40f, 0f), 30f, UiTheme.TextMuted, new Vector2(1f, 0.5f));
            _buttons.Add((nameChip, EditName));

            // Right: when this island ends, as the genre's players always want to know.
            var right = new Vector2(1f, 1f);
            var card = UiFactory.NewRect("Wipe", safe, right, right, new Vector2(-70f, -70f), new Vector2(470f, 236f));
            UiFactory.AddImage(card, UiFactory.Soft, UiTheme.Panel);
            var accent = UiFactory.NewRect("Accent", card, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(6f, 236f));
            UiFactory.AddImage(accent, null, UiTheme.Accent);
            var wipeTitle = UiFactory.NewRect("Title", card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -20f), new Vector2(420f, 40f));
            UiFactory.AddText(wipeTitle, "PRÓXIMO WIPE", 26, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            var wipeValue = UiFactory.NewRect("Value", card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -64f), new Vector2(420f, 80f));
            _wipe = UiFactory.AddText(wipeValue, "", 66, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var wipeWhen = UiFactory.NewRect("When", card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -150f), new Vector2(420f, 70f));
            var when = UiFactory.AddText(wipeWhen, "Cada jueves a las 15:00 (Venezuela)\nLa isla empieza de cero", 24,
                TextAnchor.UpperLeft, UiTheme.TextMuted);
            when.lineSpacing = 1.1f;
            ShowWipe();

            var version = UiFactory.NewRect("Version", safe, Vector2.zero, Vector2.zero, new Vector2(124f, 40f), new Vector2(800f, 36f));
            UiFactory.AddText(version, "Versión de prueba · " + Application.version, 22, TextAnchor.MiddleLeft, UiTheme.TextFaint);

            // Connecting: the screen dims and a ring turns until the server answers.
            _connecting = UiFactory.NewRect("Connecting", root, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UiFactory.Fill(_connecting);
            UiFactory.AddImage(_connecting, null, new Color(0.02f, 0.022f, 0.025f, 0.72f));
            var centre = new Vector2(0.5f, 0.5f);
            _spinner = UiFactory.NewRect("Spinner", _connecting, centre, centre, new Vector2(0f, 60f), new Vector2(120f, 120f));
            UiFactory.AddImage(_spinner, Arc(), UiTheme.Accent);
            var connectingRect = UiFactory.NewRect("Text", _connecting, centre, centre, new Vector2(0f, -50f), new Vector2(1000f, 60f));
            _connectingText = UiFactory.AddText(connectingRect, "CONECTANDO…", 44, TextAnchor.MiddleCenter, UiTheme.Text, true);
            var cancel = Button(_connecting, "CANCELAR", new Vector2(0f, -150f), new Vector2(300f, 80f), UiTheme.Panel, UiTheme.TextMuted, 30,
                () => Cancelled?.Invoke(), centre);
            cancel.pivot = centre;
            _connecting.gameObject.SetActive(false);
        }

        /// <summary>The player gave up waiting for the server.</summary>
        public event Action Cancelled;

        public void SetServerInfo(string text) => _status.text = text;

        public void ShowError(string message)
        {
            _error.text = message ?? "";
            ShowConnecting(false);
        }

        public void ShowConnecting(bool on, string text = "CONECTANDO…")
        {
            _connecting.gameObject.SetActive(on);
            _connectingText.text = text;
            if (on) _error.text = "";
        }

        /// <summary>Fades away and is gone.</summary>
        public void Close()
        {
            _closing = true;
            _fade = 0f;
        }

        private RectTransform Button(RectTransform parent, string label, Vector2 position, Vector2 size, Color fill, Color text, int fontSize,
            Action onTap, Vector2? anchor = null)
        {
            var a = anchor ?? new Vector2(0f, 0.5f);
            var rect = UiFactory.NewRect(label, parent, a, new Vector2(0f, 0.5f), position, size);
            UiFactory.AddImage(rect, UiFactory.Soft, fill);
            var labelRect = UiFactory.NewRect("Label", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(36f, 0f),
                new Vector2(size.x - 60f, size.y));
            UiFactory.AddText(labelRect, label, fontSize, anchor.HasValue ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, text, true);
            if (anchor.HasValue) labelRect.anchoredPosition = new Vector2(30f, 0f);
            _buttons.Add((rect, onTap));
            return rect;
        }

        private void EditName()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string typed = Net.BrowserPrompt.Ask("Tu nombre de superviviente", _playerName);
            if (typed != null) SetName(typed);
#else
            if (TouchScreenKeyboard.isSupported)
                _keyboard = TouchScreenKeyboard.Open(_playerName, TouchScreenKeyboardType.Default, false, false, false, false, "Tu nombre", 16);
#endif
        }

        private void SetName(string typed)
        {
            string clean = Core.Net.Protocol.CleanName(typed);
            _playerName = clean;
            _name.text = clean;
            NameChanged?.Invoke(clean);
        }

        private void ShowWipe()
        {
            var now = DateTime.UtcNow;
            var left = WipeSchedule.NextWipe(now) - now;
            _wipe.text = left.TotalHours >= 24 ? $"{left.Days}d {left.Hours}h {left.Minutes:00}m" : $"{left.Hours}h {left.Minutes:00}m {left.Seconds:00}s";
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_closing)
            {
                _fade += dt / FadeSeconds;
                _group.alpha = 1f - Mathf.SmoothStep(0f, 1f, _fade);
                if (_fade >= 1f) Destroy(gameObject);
                return;
            }

            _wipeTimer -= dt;
            if (_wipeTimer <= 0f)
            {
                _wipeTimer = 1f;
                ShowWipe();
            }
            if (_connecting.gameObject.activeSelf) _spinner.Rotate(0f, 0f, -300f * dt);
            // The way in breathes, gently.
            float glow = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
            _playFill.color = Color.Lerp(UiTheme.Accent, new Color(1f, 0.6f, 0.32f), glow * 0.35f);

            if (_keyboard != null && _keyboard.status != TouchScreenKeyboard.Status.Visible)
            {
                if (_keyboard.status == TouchScreenKeyboard.Status.Done) SetName(_keyboard.text);
                _keyboard = null;
            }

            if (TappedAt(out var point))
            {
                foreach (var (rect, onTap) in _buttons)
                {
                    if (!rect.gameObject.activeInHierarchy) continue;
                    // While connecting only the overlay's own button answers.
                    if (_connecting.gameObject.activeSelf && !rect.IsChildOf(_connecting)) continue;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(rect, point, null)) continue;
                    onTap();
                    break;
                }
            }
            if (!_connecting.gameObject.activeSelf && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                PlayOnline?.Invoke();
        }

        private static bool TappedAt(out Vector2 point)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.phase != TouchPhase.Ended) continue;
                point = touch.position;
                return true;
            }
            if (Input.touchCount == 0 && Input.GetMouseButtonUp(0))
            {
                point = Input.mousePosition;
                return true;
            }
            point = default;
            return false;
        }

        /// <summary>A fade from solid to clear: left to right, or bottom to top.</summary>
        private static Sprite Gradient(bool horizontal)
        {
            const int n = 128;
            var tex = new Texture2D(horizontal ? n : 1, horizontal ? 1 : n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                float a = 1f - Mathf.SmoothStep(0f, 1f, horizontal ? Mathf.InverseLerp(0.35f, 1f, t) : t);
                if (horizontal) tex.SetPixel(i, 0, new Color(1f, 1f, 1f, a));
                else tex.SetPixel(0, i, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Three quarters of a ring, fading along its length: the spinner.</summary>
        private static Sprite Arc()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            float c = (size - 1) * 0.5f, outer = size * 0.48f, inner = size * 0.40f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
                float angle = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 1f) % 1f;
                float tail = angle < 0.75f ? angle / 0.75f : 0f;
                pixels[y * size + x] = new Color(1f, 1f, 1f, ring * tail);
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
