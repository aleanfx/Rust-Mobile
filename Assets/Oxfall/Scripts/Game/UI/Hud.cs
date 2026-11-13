using System.Collections.Generic;
using System.Text;
using Oxfall.Core.Crafting;
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

        // The warm dark panels and rust accent of the style mock-up.
        internal static readonly Color Panel = new Color(0.08f, 0.063f, 0.05f, 0.62f);
        internal static readonly Color Soft = new Color(0.10f, 0.08f, 0.06f, 0.42f);
        internal static readonly Color Accent = new Color(0.78f, 0.39f, 0.16f, 0.88f);
        internal static readonly Color Ready = new Color(0.40f, 0.52f, 0.26f, 0.92f);
        internal static readonly Color Title = new Color(0.85f, 0.73f, 0.54f);
        internal static readonly Color Muted = new Color(0.72f, 0.68f, 0.62f);
        internal static readonly Color Sheet = new Color(0.08f, 0.063f, 0.05f, 0.9f);

        private RectTransform _safeArea;
        private Rect _appliedSafeArea;
        private Text _fps;
        private Text _clock;
        private Text _resources;
        private Text _toast;
        private float _toastTimer;
        private Text _quality;

        private int _frames;
        private float _elapsed;
        private float _worstFrame;
        private int _windows;

        public Canvas Canvas { get; private set; }
        public RectTransform JoystickBase { get; private set; }
        public RectTransform JoystickKnob { get; private set; }
        public Vector2 JoystickHome { get; private set; }
        public RectTransform AttackButton { get; private set; }
        public RectTransform JumpButton { get; private set; }
        public RectTransform QualityButton { get; private set; }
        public Image AttackImage { get; private set; }
        public Image JumpImage { get; private set; }

        /// <summary>The area clear of notches and rounded corners; everything on screen hangs from it.</summary>
        public RectTransform SafeArea => _safeArea;

        private Text _attackLabel;
        private Color _attackIdle = Soft;

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
            var clock = UiFactory.NewRect("Clock", _safeArea, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -62f), new Vector2(420f, 44f));
            _clock = UiFactory.AddText(clock, "", 30, TextAnchor.UpperLeft, Title);

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
            UiFactory.AddImage(JoystickBase, UiFactory.Ring, new Color(0.95f, 0.92f, 0.88f, 0.5f));
            JoystickKnob = UiFactory.NewRect("Knob", JoystickBase, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f));
            UiFactory.AddImage(JoystickKnob, UiFactory.Circle, new Color(0.95f, 0.92f, 0.88f, 0.45f));

            // Bottom right: attack (hold to keep hitting) and jump.
            AttackButton = UiFactory.NewRect("Attack", _safeArea, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-210f, 230f), new Vector2(230f, 230f));
            AttackImage = UiFactory.AddImage(AttackButton, UiFactory.Circle, Soft);
            var attackLabel = UiFactory.NewRect("Label", AttackButton, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 60f));
            _attackLabel = UiFactory.AddText(attackLabel, "GOLPEAR", 34, TextAnchor.MiddleCenter, Color.white);

            JumpButton = UiFactory.NewRect("Jump", _safeArea, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-450f, 140f), new Vector2(160f, 160f));
            JumpImage = UiFactory.AddImage(JumpButton, UiFactory.Circle, Soft);
            var jumpLabel = UiFactory.NewRect("Label", JumpButton, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 50f));
            UiFactory.AddText(jumpLabel, "SALTAR", 28, TextAnchor.MiddleCenter, Color.white);

            // Right side, above attack: opens the crafting panel.
            CraftButton = UiFactory.NewRect("Craft", _safeArea, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-120f, 470f), new Vector2(170f, 170f));
            UiFactory.AddImage(CraftButton, UiFactory.Circle, Soft);
            var craftLabel = UiFactory.NewRect("Label", CraftButton, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170f, 50f));
            UiFactory.AddText(craftLabel, "FABRICAR", 24, TextAnchor.MiddleCenter, Color.white);
            TapTargets.Add(new TapTarget { Rect = CraftButton, OnTap = ToggleCraftPanel });

            // Above attack, only in building mode: which piece to build. Attack places it.
            var centre = new Vector2(0.5f, 0.5f);
            PieceButton = UiFactory.NewRect("Piece", _safeArea, new Vector2(1f, 0f), centre, new Vector2(-400f, 410f), new Vector2(320f, 90f));
            UiFactory.AddImage(PieceButton, UiFactory.Rounded, Panel);
            var pieceLabel = UiFactory.NewRect("Label", PieceButton, centre, centre, Vector2.zero, new Vector2(310f, 60f));
            _pieceLabel = UiFactory.AddText(pieceLabel, "", 26, TextAnchor.MiddleCenter, Color.white);
            TapTargets.Add(new TapTarget { Rect = PieceButton, OnTap = () => PieceTapped?.Invoke() });

            PieceButton.gameObject.SetActive(false);

            // Left of jump, only while looking at something to use (a door): ABRIR or CERRAR.
            UseButton = UiFactory.NewRect("Use", _safeArea, new Vector2(1f, 0f), centre, new Vector2(-680f, 230f), new Vector2(180f, 180f));
            UiFactory.AddImage(UseButton, UiFactory.Circle, Soft);
            var useLabel = UiFactory.NewRect("Label", UseButton, centre, centre, Vector2.zero, new Vector2(180f, 50f));
            _useLabel = UiFactory.AddText(useLabel, "", 30, TextAnchor.MiddleCenter, Color.white);
            TapTargets.Add(new TapTarget { Rect = UseButton, OnTap = () => UseTapped?.Invoke() });
            UseButton.gameObject.SetActive(false);

            // Under the crosshair: what is aimed at, and what the hammer would do to it.
            var aim = UiFactory.NewRect("Aim info", _safeArea, centre, centre, new Vector2(0f, -86f), new Vector2(1100f, 50f));
            _aimInfo = UiFactory.AddText(aim, "", 28, TextAnchor.MiddleCenter, Color.white);
        }

        public void SetPressed(Image image, bool pressed)
        {
            // Only on change: touching a graphic's colour makes its canvas rebuild.
            var target = pressed ? Accent : image == AttackImage ? _attackIdle : Soft;
            if (image.color != target) image.color = target;
        }

        /// <summary>Something on screen that does one thing when tapped.</summary>
        public sealed class TapTarget
        {
            public RectTransform Rect;
            public System.Action OnTap;
        }

        /// <summary>Everything tappable besides the stick, attack and jump. Hidden ones are skipped.</summary>
        public readonly List<TapTarget> TapTargets = new List<TapTarget>();

        /// <summary>A screen over everything (death): while it shows, only its own buttons work.</summary>
        public RectTransform Modal;

        /// <summary>Panels and bars where a touch that hits no button still must not move or look.</summary>
        public readonly List<RectTransform> Blockers = new List<RectTransform>();

        public bool Blocks(Vector2 screenPoint)
        {
            foreach (var rect in Blockers)
                if (rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null))
                    return true;
            return false;
        }

        public event System.Action PieceTapped;
        public event System.Action UseTapped;
        public event System.Action CraftPanelOpened;

        public RectTransform PieceButton { get; private set; }
        public RectTransform UseButton { get; private set; }
        private Text _pieceLabel;
        private Text _useLabel;
        private Text _aimInfo;

        /// <summary>The button above attack with this label (DEMOLER, "Pieza: Pared"), or hidden for null.</summary>
        public void SetPieceButton(string label)
        {
            bool show = label != null;
            if (PieceButton.gameObject.activeSelf != show) PieceButton.gameObject.SetActive(show);
            if (show && _pieceLabel.text != label) _pieceLabel.text = label;
        }

        /// <summary>The use button (ABRIR, CERRAR), or hidden for null.</summary>
        public void SetUseButton(string label)
        {
            bool show = label != null;
            if (UseButton.gameObject.activeSelf != show) UseButton.gameObject.SetActive(show);
            if (show && _useLabel.text != label) _useLabel.text = label;
        }

        /// <summary>A line under the crosshair about what is aimed at; empty or null clears it.</summary>
        public void SetAimInfo(string text)
        {
            text = text ?? "";
            if (_aimInfo.text != text) _aimInfo.text = text;
        }

        /// <summary>
        /// Placing something (the building plan or a campfire in hand): with a piece name the piece
        /// button shows, and attack greens while the piece fits.
        /// </summary>
        public void SetBuildMode(bool on, string pieceName)
        {
            SetPieceButton(on && pieceName != null ? "Pieza: " + pieceName : null);
            if (!on) SetPlaceValid(false);
        }

        /// <summary>What the attack button does with what is in hand: GOLPEAR, COLOCAR or COMER.</summary>
        public void SetAttackLabel(string label) => _attackLabel.text = label;

        /// <summary>Greens the attack button (COLOCAR) while the piece fits where it is aimed.</summary>
        public void SetPlaceValid(bool valid)
        {
            _attackIdle = valid ? Ready : Soft;
            if (AttackImage.color != Accent && AttackImage.color != _attackIdle) AttackImage.color = _attackIdle;
        }

        public RectTransform CraftButton { get; private set; }
        public RectTransform CraftPanel { get; private set; }
        public bool CraftPanelOpen => CraftPanel != null && CraftPanel.gameObject.activeSelf;

        private readonly List<Recipe> _craftRecipes = new List<Recipe>();
        private readonly List<Image> _craftButtons = new List<Image>();
        private Inventory _craftInventory;

        /// <summary>The crafting panel: one row per recipe with what it costs and a button to make it.</summary>
        public void BuildCraftPanel(RecipeBook book, ItemCatalog catalog, Inventory inventory, System.Action<Recipe> onCraft)
        {
            _craftInventory = inventory;
            var half = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);
            const float rowHeight = 96f;

            CraftPanel = UiFactory.NewRect("Craft panel", _safeArea, half, half, new Vector2(0f, 20f),
                new Vector2(1040f, 120f + book.All.Count * rowHeight));
            UiFactory.AddImage(CraftPanel, UiFactory.Rounded, Sheet);
            Blockers.Add(CraftPanel);

            var title = UiFactory.NewRect("Title", CraftPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(600f, 60f));
            UiFactory.AddText(title, "FABRICAR", 40, TextAnchor.MiddleCenter, Title);

            var close = UiFactory.NewRect("Close", CraftPanel, topRight, topRight, new Vector2(-18f, -18f), new Vector2(84f, 72f));
            UiFactory.AddImage(close, UiFactory.Rounded, Soft);
            var closeLabel = UiFactory.NewRect("Label", close, half, half, Vector2.zero, new Vector2(80f, 60f));
            UiFactory.AddText(closeLabel, "X", 36, TextAnchor.MiddleCenter, Color.white);
            TapTargets.Add(new TapTarget { Rect = close, OnTap = ToggleCraftPanel });

            for (int i = 0; i < book.All.Count; i++)
            {
                var recipe = book.All[i];
                float y = -110f - i * rowHeight;

                var nameRect = UiFactory.NewRect("Name", CraftPanel, topLeft, topLeft, new Vector2(36f, y), new Vector2(380f, 80f));
                UiFactory.AddText(nameRect, catalog.Get(recipe.OutputItemId).DisplayName, 32, TextAnchor.MiddleLeft, Color.white);

                var cost = new StringBuilder();
                foreach (var ingredient in recipe.Ingredients)
                {
                    if (cost.Length > 0) cost.Append(" + ");
                    cost.Append(ingredient.Amount).Append(' ').Append(catalog.Get(ingredient.ItemId).DisplayName);
                }
                var costRect = UiFactory.NewRect("Cost", CraftPanel, topLeft, topLeft, new Vector2(420f, y), new Vector2(400f, 80f));
                UiFactory.AddText(costRect, cost.ToString(), 26, TextAnchor.MiddleLeft, Muted);

                var button = UiFactory.NewRect("Make", CraftPanel, topRight, topRight, new Vector2(-30f, y - 4f), new Vector2(190f, 72f));
                _craftButtons.Add(UiFactory.AddImage(button, UiFactory.Rounded, Soft));
                var label = UiFactory.NewRect("Label", button, half, half, Vector2.zero, new Vector2(180f, 60f));
                UiFactory.AddText(label, "HACER", 30, TextAnchor.MiddleCenter, Color.white);

                _craftRecipes.Add(recipe);
                TapTargets.Add(new TapTarget { Rect = button, OnTap = () => onCraft(recipe) });
            }

            CraftPanel.gameObject.SetActive(false);
        }

        public void ToggleCraftPanel()
        {
            if (CraftPanel == null) return;
            bool open = !CraftPanel.gameObject.activeSelf;
            CraftPanel.gameObject.SetActive(open);
            if (open) CraftPanelOpened?.Invoke();
            RefreshCraftPanel();
        }

        public void CloseCraftPanel()
        {
            if (CraftPanelOpen) CraftPanel.gameObject.SetActive(false);
        }

        /// <summary>Greens the button of every recipe the player can afford right now.</summary>
        public void RefreshCraftPanel()
        {
            if (!CraftPanelOpen) return;
            for (int i = 0; i < _craftRecipes.Count; i++)
            {
                var target = _craftRecipes[i].CanAfford(_craftInventory) ? Ready : Soft;
                if (_craftButtons[i].color != target) _craftButtons[i].color = target;
            }
        }

        public void SetQualityLabel(string tierName) => _quality.text = "Gráficos: " + tierName;

        public void SetClock(string text) => _clock.text = text;

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
                // Every 5 s into the log too, so automated runs can read the frame rate.
                if (++_windows % 10 == 0) Debug.Log("[Oxfall] " + _fps.text);
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
