using System.Text;
using Oxfall.Core.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The on-screen layer: joystick, buttons, resource counts, FPS and short messages. Built in
    /// code over the phone's safe area (clear of notches and rounded corners).
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public const float JoystickRadius = 130f;

        private static readonly Color Panel = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color Soft = new Color(1f, 1f, 1f, 0.22f);

        private RectTransform _safeArea;
        private Rect _appliedSafeArea;
        private Text _fps;
        private Text _resources;
        private Text _toast;
        private float _toastTimer;
        private Text _quality;

        private int _frames;
        private float _elapsed;
        private float _worstFrame;

        public Canvas Canvas { get; private set; }
        public RectTransform JoystickBase { get; private set; }
        public RectTransform JoystickKnob { get; private set; }
        public Vector2 JoystickHome { get; private set; }
        public RectTransform AttackButton { get; private set; }
        public RectTransform JumpButton { get; private set; }
        public RectTransform QualityButton { get; private set; }
        public Image AttackImage { get; private set; }
        public Image JumpImage { get; private set; }

        public static Hud Create()
        {
            var go = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler));
            var hud = go.AddComponent<Hud>();
            hud.Build();
            return hud;
        }

        private void Build()
        {
            Canvas = GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            Canvas.pixelPerfect = false;

            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            _safeArea = UiFactory.NewRect("Safe area", transform, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            ApplySafeArea();

            // Top left: frame rate.
            var fps = UiFactory.NewRect("FPS", _safeArea, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -18f), new Vector2(420f, 44f));
            _fps = UiFactory.AddText(fps, "", 30, TextAnchor.UpperLeft, Color.white);

            // Top centre: what the player is carrying.
            var resPanel = UiFactory.NewRect("Resources panel", _safeArea, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(820f, 64f));
            UiFactory.AddImage(resPanel, UiFactory.Rounded, Panel);
            var res = UiFactory.NewRect("Resources", resPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 60f));
            _resources = UiFactory.AddText(res, "", 32, TextAnchor.MiddleCenter, Color.white);

            // Top right: graphics quality, tap to change.
            QualityButton = UiFactory.NewRect("Quality", _safeArea, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -14f), new Vector2(300f, 70f));
            UiFactory.AddImage(QualityButton, UiFactory.Rounded, Panel);
            var q = UiFactory.NewRect("Label", QualityButton, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(290f, 60f));
            _quality = UiFactory.AddText(q, "", 28, TextAnchor.MiddleCenter, Color.white);

            // Centre: a small dot to aim with.
            var dot = UiFactory.NewRect("Crosshair", _safeArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            UiFactory.AddImage(dot, UiFactory.Circle, new Color(1f, 1f, 1f, 0.8f));

            // Lower centre: short messages ("+15 Madera").
            var toast = UiFactory.NewRect("Toast", _safeArea, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 250f), new Vector2(900f, 60f));
            _toast = UiFactory.AddText(toast, "", 38, TextAnchor.MiddleCenter, Color.white);

            // Bottom left: the movement stick. It follows the thumb, and rests here.
            JoystickHome = new Vector2(300f, 270f);
            JoystickBase = UiFactory.NewRect("Joystick", _safeArea, Vector2.zero, new Vector2(0.5f, 0.5f), JoystickHome, new Vector2(JoystickRadius * 2f, JoystickRadius * 2f));
            UiFactory.AddImage(JoystickBase, UiFactory.Ring, new Color(1f, 1f, 1f, 0.55f));
            JoystickKnob = UiFactory.NewRect("Knob", JoystickBase, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f));
            UiFactory.AddImage(JoystickKnob, UiFactory.Circle, new Color(1f, 1f, 1f, 0.5f));

            // Bottom right: attack (hold to keep hitting) and jump.
            AttackButton = UiFactory.NewRect("Attack", _safeArea, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-210f, 230f), new Vector2(230f, 230f));
            AttackImage = UiFactory.AddImage(AttackButton, UiFactory.Circle, Soft);
            var attackLabel = UiFactory.NewRect("Label", AttackButton, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 60f));
            UiFactory.AddText(attackLabel, "GOLPEAR", 34, TextAnchor.MiddleCenter, Color.white);

            JumpButton = UiFactory.NewRect("Jump", _safeArea, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-450f, 140f), new Vector2(160f, 160f));
            JumpImage = UiFactory.AddImage(JumpButton, UiFactory.Circle, Soft);
            var jumpLabel = UiFactory.NewRect("Label", JumpButton, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 50f));
            UiFactory.AddText(jumpLabel, "SALTAR", 28, TextAnchor.MiddleCenter, Color.white);
        }

        public void SetPressed(Image image, bool pressed)
        {
            // Only on change: touching a graphic's colour makes its canvas rebuild.
            var target = pressed ? new Color(1f, 1f, 1f, 0.45f) : Soft;
            if (image.color != target) image.color = target;
        }

        public void SetQualityLabel(string tierName) => _quality.text = "Gráficos: " + tierName;

        public void ShowToast(string message)
        {
            _toast.text = message;
            _toastTimer = 1.6f;
            _toast.color = Color.white;
        }

        public void RefreshResources(Inventory inventory, ItemCatalog catalog)
        {
            var sb = new StringBuilder();
            Append(sb, inventory, catalog, ItemIds.Wood);
            Append(sb, inventory, catalog, ItemIds.Stone);
            Append(sb, inventory, catalog, ItemIds.MetalOre);
            Append(sb, inventory, catalog, ItemIds.Cloth);
            _resources.text = sb.ToString();
        }

        private static void Append(StringBuilder sb, Inventory inventory, ItemCatalog catalog, string id)
        {
            if (sb.Length > 0) sb.Append("     ");
            sb.Append(catalog.Get(id).DisplayName).Append(' ').Append(inventory.Count(id));
        }

        private void Update()
        {
            if (Screen.safeArea != _appliedSafeArea) ApplySafeArea();

            // Frame rate over half a second, with the slowest frame in that window: the slow frames
            // are what a player feels as stutter.
            float dt = Time.unscaledDeltaTime;
            _frames++;
            _elapsed += dt;
            if (dt > _worstFrame) _worstFrame = dt;
            if (_elapsed >= 0.5f)
            {
                _fps.text = $"{Mathf.RoundToInt(_frames / _elapsed)} FPS   peor {Mathf.RoundToInt(_worstFrame * 1000f)} ms";
                _frames = 0;
                _elapsed = 0f;
                _worstFrame = 0f;
            }

            if (_toastTimer > 0f)
            {
                _toastTimer -= dt;
                var c = _toast.color;
                c.a = Mathf.Clamp01(_toastTimer / 0.4f);
                _toast.color = c;
            }
        }

        private void ApplySafeArea()
        {
            var area = Screen.safeArea;
            _appliedSafeArea = area;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            _safeArea.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            _safeArea.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            _safeArea.offsetMin = Vector2.zero;
            _safeArea.offsetMax = Vector2.zero;
        }
    }
}
