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
        private static readonly Color Panel = new Color(0.08f, 0.063f, 0.05f, 0.62f);
        private static readonly Color Soft = new Color(0.10f, 0.08f, 0.06f, 0.42f);
        private static readonly Color Accent = new Color(0.78f, 0.39f, 0.16f, 0.88f);
        private static readonly Color Ready = new Color(0.40f, 0.52f, 0.26f, 0.92f);

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
            UiFactory.AddImage(JoystickBase, UiFactory.Ring, new Color(0.95f, 0.92f, 0.88f, 0.5f));
            JoystickKnob = UiFactory.NewRect("Knob", JoystickBase, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f));
            UiFactory.AddImage(JoystickKnob, UiFactory.Circle, new Color(0.95f, 0.92f, 0.88f, 0.45f));

            // Bottom right: attack (hold to keep hitting) and jump.
            AttackButton = UiFactory.NewRect("Attack", _safeArea, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-210f, 230f), new Vector2(230f, 230f));
            AttackImage = UiFactory.AddImage(AttackButton, UiFactory.Circle, Soft);
            var attackLabel = UiFactory.NewRect("Label", AttackButton, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 60f));
            UiFactory.AddText(attackLabel, "GOLPEAR", 34, TextAnchor.MiddleCenter, Color.white);

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

            // Left of FABRICAR: enters and leaves building mode.
            var centre = new Vector2(0.5f, 0.5f);
            BuildButton = UiFactory.NewRect("Build", _safeArea, new Vector2(1f, 0f), centre, new Vector2(-310f, 500f), new Vector2(170f, 170f));
            UiFactory.AddImage(BuildButton, UiFactory.Circle, Soft);
            var buildLabel = UiFactory.NewRect("Label", BuildButton, centre, centre, Vector2.zero, new Vector2(170f, 50f));
            UiFactory.AddText(buildLabel, "CONSTRUIR", 23, TextAnchor.MiddleCenter, Color.white);
            TapTargets.Add(new TapTarget { Rect = BuildButton, OnTap = () => BuildTapped?.Invoke() });

            // Bottom centre, only in building mode: which piece, and the button that places it.
            var bottom = new Vector2(0.5f, 0f);
            PieceButton = UiFactory.NewRect("Piece", _safeArea, bottom, bottom, new Vector2(-180f, 40f), new Vector2(340f, 96f));
            UiFactory.AddImage(PieceButton, UiFactory.Rounded, Panel);
            var pieceLabel = UiFactory.NewRect("Label", PieceButton, centre, centre, Vector2.zero, new Vector2(330f, 60f));
            _pieceLabel = UiFactory.AddText(pieceLabel, "", 28, TextAnchor.MiddleCenter, Color.white);
            TapTargets.Add(new TapTarget { Rect = PieceButton, OnTap = () => PieceTapped?.Invoke() });

            PlaceButton = UiFactory.NewRect("Place", _safeArea, bottom, bottom, new Vector2(180f, 40f), new Vector2(300f, 96f));
            _placeImage = UiFactory.AddImage(PlaceButton, UiFactory.Rounded, Panel);
            var placeLabel = UiFactory.NewRect("Label", PlaceButton, centre, centre, Vector2.zero, new Vector2(290f, 60f));
            UiFactory.AddText(placeLabel, "COLOCAR", 30, TextAnchor.MiddleCenter, Color.white);
            TapTargets.Add(new TapTarget { Rect = PlaceButton, OnTap = () => PlaceTapped?.Invoke() });

            PieceButton.gameObject.SetActive(false);
            PlaceButton.gameObject.SetActive(false);
        }

        public void SetPressed(Image image, bool pressed)
        {
            // Only on change: touching a graphic's colour makes its canvas rebuild.
            var target = pressed ? Accent : Soft;
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

        public event System.Action BuildTapped;
        public event System.Action PieceTapped;
        public event System.Action PlaceTapped;

        public RectTransform BuildButton { get; private set; }
        public RectTransform PieceButton { get; private set; }
        public RectTransform PlaceButton { get; private set; }
        private Text _pieceLabel;
        private Image _placeImage;

        /// <summary>Shows or hides the building controls and names the chosen piece.</summary>
        public void SetBuildMode(bool on, string pieceName)
        {
            PieceButton.gameObject.SetActive(on);
            PlaceButton.gameObject.SetActive(on);
            _pieceLabel.text = "Pieza: " + pieceName;
        }

        /// <summary>Greens COLOCAR while the piece fits where it is aimed.</summary>
        public void SetPlaceValid(bool valid)
        {
            var target = valid ? Ready : Panel;
            if (_placeImage.color != target) _placeImage.color = target;
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
            UiFactory.AddImage(CraftPanel, UiFactory.Rounded, new Color(0.08f, 0.063f, 0.05f, 0.9f));

            var title = UiFactory.NewRect("Title", CraftPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(600f, 60f));
            UiFactory.AddText(title, "FABRICAR", 40, TextAnchor.MiddleCenter, new Color(0.85f, 0.73f, 0.54f));

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
                UiFactory.AddText(costRect, cost.ToString(), 26, TextAnchor.MiddleLeft, new Color(0.72f, 0.68f, 0.62f));

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
            CraftPanel.gameObject.SetActive(!CraftPanel.gameObject.activeSelf);
            RefreshCraftPanel();
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
