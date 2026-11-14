using System.Collections.Generic;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The on-screen layer, built in code over the phone's safe area (clear of notches and rounded
    /// corners). Kept to what a survivor needs at a glance, the way the PC game does it: a compass
    /// and the clock up top, vitals in a corner, the belt along the bottom, pickups sliding in at
    /// the side, and the thumb controls. Everything else lives in panels opened on demand.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public const float JoystickRadius = 130f;

        // Kept for the panels that still use the older names.
        internal static Color Panel => UiTheme.Panel;
        internal static Color Soft => UiTheme.Button;
        internal static Color Accent => UiTheme.Accent;
        internal static Color Ready => UiTheme.GoodFill;
        internal static Color Title => UiTheme.Text;
        internal static Color Muted => UiTheme.TextMuted;
        internal static Color Sheet => UiTheme.PanelSolid;

        private RectTransform _safeArea;
        private Rect _appliedSafeArea;
        private Text _fps;
        private Text _clock;
        private Image _clockIcon;
        private bool _clockNight;
        private RectTransform _toastChip;
        private Text _toast;
        private CanvasGroup _toastGroup;
        private float _toastTimer;
        private RectTransform _aimChip;
        private Text _aimInfo;
        private RectTransform _settings;
        private readonly Image[] _qualityButtons = new Image[3];
        private Text _fpsToggleLabel;
        private bool _showFps = true;
        private Compass _compass;
        private RectTransform _online;
        private Image _onlineDot;
        private Text _onlineText;
        private PickupFeed _feed;
        private CraftUi _craft;

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
        /// <summary>The settings button (top right): graphics level and the frame counter.</summary>
        public RectTransform QualityButton { get; private set; }
        public Image AttackImage { get; private set; }
        public Image JumpImage { get; private set; }
        public RectTransform CraftButton { get; private set; }
        public RectTransform PieceButton { get; private set; }
        public RectTransform UseButton { get; private set; }
        public RectTransform LockButton { get; private set; }
        /// <summary>With a gun in hand that is not full: reload it now.</summary>
        public RectTransform ReloadButton { get; private set; }

        /// <summary>The compass was tapped (or M pressed): open or close the map.</summary>
        public event System.Action MapTapped;
        public void ToggleMap() => MapTapped?.Invoke();

        /// <summary>The area clear of notches and rounded corners; everything on screen hangs from it.</summary>
        public RectTransform SafeArea => _safeArea;

        /// <summary>Whose heading the compass shows (the player's head).</summary>
        public Transform HeadingSource;

        private Text _attackLabel;
        private Image _attackIcon;
        private Color _attackIdle = UiTheme.Button;
        private Text _pieceLabel;
        private Text _useLabel;

        public event System.Action PieceTapped;
        public event System.Action UseTapped;
        public event System.Action LockTapped;
        public event System.Action ReloadTapped;
        public event System.Action CraftPanelOpened;
        /// <summary>A graphics level was chosen in settings: 0 low, 1 medium, 2 high.</summary>
        public event System.Action<int> QualityChosen;

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

            var centre = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);
            var bottomRight = new Vector2(1f, 0f);

            // Top left: the time of day on a small chip; the vitals hang under it (VitalsUi).
            var clockChip = UiFactory.NewRect("Clock", _safeArea, topLeft, topLeft, new Vector2(24f, -20f), new Vector2(176f, 48f));
            UiFactory.AddImage(clockChip, UiFactory.Soft, UiTheme.Panel);
            _clockIcon = UiFactory.AddIcon(clockChip, Icons.Sun, new Vector2(28f, 0f), 28f, UiTheme.Food, new Vector2(0f, 0.5f));
            var clockText = UiFactory.NewRect("Time", clockChip, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(54f, 0f), new Vector2(116f, 44f));
            _clock = UiFactory.AddText(clockText, "--:--", 30, TextAnchor.MiddleLeft, UiTheme.Text, true);

            // Beside the clock, when playing on a server: who is online.
            _online = UiFactory.NewRect("Online", _safeArea, topLeft, topLeft, new Vector2(212f, -20f), new Vector2(150f, 48f));
            UiFactory.AddImage(_online, UiFactory.Soft, UiTheme.Panel);
            var dotRect = UiFactory.NewRect("Dot", _online, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22f, 0f), new Vector2(14f, 14f));
            _onlineDot = UiFactory.AddImage(dotRect, UiFactory.Circle, UiTheme.Good);
            var onlineText = UiFactory.NewRect("Text", _online, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(38f, 0f), new Vector2(110f, 44f));
            _onlineText = UiFactory.AddText(onlineText, "", 26, TextAnchor.MiddleLeft, UiTheme.Text, true);
            _online.gameObject.SetActive(false);

            // Top centre: the compass; tapping it opens the map.
            _compass = Compass.Create(_safeArea);
            TapTargets.Add(new TapTarget { Rect = _compass.Root, OnTap = ToggleMap });
            TapTargets.Add(new TapTarget { Rect = _compass.MapChip, OnTap = ToggleMap });

            // Top right: settings, with the frame counter beside it.
            QualityButton = UiFactory.NewRect("Settings", _safeArea, topRight, topRight, new Vector2(-24f, -20f), new Vector2(64f, 64f));
            UiFactory.AddImage(QualityButton, UiFactory.Soft, UiTheme.Panel);
            UiFactory.AddIcon(QualityButton, Icons.Gear, Vector2.zero, 36f, UiTheme.Text);
            TapTargets.Add(new TapTarget { Rect = QualityButton, OnTap = ToggleSettings });
            var fps = UiFactory.NewRect("FPS", _safeArea, topRight, topRight, new Vector2(-100f, -20f), new Vector2(260f, 64f));
            _fps = UiFactory.AddText(fps, "", 22, TextAnchor.MiddleRight, UiTheme.TextFaint);

            // Right, under settings: what was just picked up or made.
            _feed = PickupFeed.Create(_safeArea);

            // Centre: a small dot to aim with, ringed so it shows on snow and sky alike.
            var ring = UiFactory.NewRect("Crosshair ring", _safeArea, centre, centre, Vector2.zero, new Vector2(12f, 12f));
            UiFactory.AddImage(ring, UiFactory.Circle, new Color(0f, 0f, 0f, 0.35f));
            var dot = UiFactory.NewRect("Crosshair", _safeArea, centre, centre, Vector2.zero, new Vector2(7f, 7f));
            UiFactory.AddImage(dot, UiFactory.Circle, new Color(1f, 1f, 1f, 0.9f));

            // A blow that lands: four short ticks round the crosshair (red when it kills).
            _hitMarker = UiFactory.NewRect("Hit marker", _safeArea, centre, centre, Vector2.zero, new Vector2(60f, 60f));
            _hitMarkerGroup = _hitMarker.gameObject.AddComponent<CanvasGroup>();
            _hitMarkerGroup.alpha = 0f;
            _hitMarkerGroup.blocksRaycasts = false;
            _hitTicks = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                float angle = 45f + i * 90f;
                var dir = Quaternion.Euler(0f, 0f, angle) * Vector3.right;
                var tick = UiFactory.NewRect("Tick", _hitMarker, centre, centre, dir * 17f, new Vector2(15f, 4f));
                tick.localRotation = Quaternion.Euler(0f, 0f, angle);
                _hitTicks[i] = UiFactory.AddImage(tick, null, Color.white);
            }

            // Being hurt: the edges of the screen flush red and fade.
            var hurt = UiFactory.NewRect("Hurt", transform, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UiFactory.Fill(hurt);
            hurt.SetAsFirstSibling();
            _hurt = UiFactory.AddImage(hurt, UiFactory.Vignette, new Color(0.75f, 0.05f, 0.04f, 0f));
            _hurt.type = Image.Type.Simple;
            _hurt.enabled = false;

            // Under the crosshair: what is aimed at (the hammer's view of a piece).
            _aimChip = Chip("Aim info", centre, new Vector2(0f, -78f), 26, out _aimInfo);
            _aimChip.gameObject.SetActive(false);

            // Above the belt: hints and warnings.
            _toastChip = Chip("Toast", new Vector2(0.5f, 0f), new Vector2(0f, 168f), 30, out _toast);
            _toastChip.gameObject.AddComponent<Canvas>();
            _toastGroup = _toastChip.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0f;

            // Bottom left: the movement stick. It follows the thumb, and rests here.
            JoystickHome = new Vector2(300f, 300f);
            JoystickBase = UiFactory.NewRect("Joystick", _safeArea, Vector2.zero, centre, JoystickHome, new Vector2(JoystickRadius * 2f, JoystickRadius * 2f));
            UiFactory.AddImage(JoystickBase, UiFactory.Ring, new Color(1f, 1f, 1f, 0.32f));
            JoystickKnob = UiFactory.NewRect("Knob", JoystickBase, centre, centre, Vector2.zero, new Vector2(112f, 112f));
            UiFactory.AddImage(JoystickKnob, UiFactory.Circle, new Color(1f, 1f, 1f, 0.38f));

            // Bottom right: attack (hold to keep hitting), with what it does under the held item.
            AttackButton = UiFactory.NewRect("Attack", _safeArea, bottomRight, centre, new Vector2(-200f, 230f), new Vector2(220f, 220f));
            AttackImage = UiFactory.AddImage(AttackButton, UiFactory.Circle, UiTheme.Button);
            AddRing(AttackButton, 220f);
            _attackIcon = UiFactory.AddIcon(AttackButton, Icons.Hand, new Vector2(0f, 18f), 92f, UiTheme.Text);
            var attackLabel = UiFactory.NewRect("Label", AttackButton, centre, centre, new Vector2(0f, -58f), new Vector2(200f, 40f));
            _attackLabel = UiFactory.AddText(attackLabel, "GOLPEAR", 28, TextAnchor.MiddleCenter, UiTheme.Text, true);

            JumpButton = UiFactory.NewRect("Jump", _safeArea, bottomRight, centre, new Vector2(-440f, 130f), new Vector2(148f, 148f));
            JumpImage = UiFactory.AddImage(JumpButton, UiFactory.Circle, UiTheme.Button);
            AddRing(JumpButton, 148f);
            UiFactory.AddIcon(JumpButton, Icons.Jump, Vector2.zero, 60f, UiTheme.Text);

            // Above attack: crafting.
            CraftButton = UiFactory.NewRect("Craft", _safeArea, bottomRight, centre, new Vector2(-118f, 456f), new Vector2(132f, 132f));
            UiFactory.AddImage(CraftButton, UiFactory.Circle, UiTheme.Button);
            AddRing(CraftButton, 132f);
            UiFactory.AddIcon(CraftButton, Icons.Hammer, new Vector2(0f, 10f), 56f, UiTheme.Text);
            var craftLabel = UiFactory.NewRect("Label", CraftButton, centre, centre, new Vector2(0f, -38f), new Vector2(130f, 30f));
            UiFactory.AddText(craftLabel, "FABRICAR", 20, TextAnchor.MiddleCenter, UiTheme.TextMuted, true);
            TapTargets.Add(new TapTarget { Rect = CraftButton, OnTap = ToggleCraftPanel });

            // Above attack, while placing or with the hammer: the next piece, or DEMOLER.
            PieceButton = UiFactory.NewRect("Piece", _safeArea, bottomRight, centre, new Vector2(-400f, 400f), new Vector2(300f, 76f));
            UiFactory.AddImage(PieceButton, UiFactory.Rounded, UiTheme.Panel);
            var pieceLabel = UiFactory.NewRect("Label", PieceButton, centre, centre, Vector2.zero, new Vector2(290f, 60f));
            _pieceLabel = UiFactory.AddText(pieceLabel, "", 28, TextAnchor.MiddleCenter, UiTheme.Text, true);
            TapTargets.Add(new TapTarget { Rect = PieceButton, OnTap = () => PieceTapped?.Invoke() });
            PieceButton.gameObject.SetActive(false);

            // Left of jump, only while looking at something to use (a door): ABRIR or CERRAR.
            UseButton = UiFactory.NewRect("Use", _safeArea, bottomRight, centre, new Vector2(-660f, 220f), new Vector2(160f, 160f));
            UiFactory.AddImage(UseButton, UiFactory.Circle, UiTheme.Accent.WithAlpha(0.85f));
            _useIcon = UiFactory.AddIcon(UseButton, Icons.Door, new Vector2(0f, 16f), 58f, Color.white);
            var useLabel = UiFactory.NewRect("Label", UseButton, centre, centre, new Vector2(0f, -40f), new Vector2(160f, 36f));
            _useLabel = UiFactory.AddText(useLabel, "", 26, TextAnchor.MiddleCenter, Color.white, true);
            TapTargets.Add(new TapTarget { Rect = UseButton, OnTap = () => UseTapped?.Invoke() });
            UseButton.gameObject.SetActive(false);

            // Above use, on a code lock the player knows: its keypad (lock, unlock, new code, take off).
            LockButton = UiFactory.NewRect("Lock", _safeArea, bottomRight, centre, new Vector2(-660f, 392f), new Vector2(116f, 116f));
            UiFactory.AddImage(LockButton, UiFactory.Circle, UiTheme.Button);
            AddRing(LockButton, 116f);
            _lockIcon = UiFactory.AddIcon(LockButton, Icons.Lock, new Vector2(0f, 10f), 46f, UiTheme.Text);
            var lockLabel = UiFactory.NewRect("Label", LockButton, centre, centre, new Vector2(0f, -32f), new Vector2(116f, 26f));
            UiFactory.AddText(lockLabel, "CANDADO", 18, TextAnchor.MiddleCenter, UiTheme.TextMuted, true);
            TapTargets.Add(new TapTarget { Rect = LockButton, OnTap = () => LockTapped?.Invoke() });
            LockButton.gameObject.SetActive(false);

            // Between jump and attack, with a gun in hand that has room for more: reload now.
            ReloadButton = UiFactory.NewRect("Reload", _safeArea, bottomRight, centre, new Vector2(-410f, 312f), new Vector2(112f, 112f));
            UiFactory.AddImage(ReloadButton, UiFactory.Circle, UiTheme.Button);
            AddRing(ReloadButton, 112f);
            UiFactory.AddIcon(ReloadButton, Icons.Reload, new Vector2(0f, 10f), 46f, UiTheme.Text);
            var reloadLabel = UiFactory.NewRect("Label", ReloadButton, centre, centre, new Vector2(0f, -31f), new Vector2(112f, 26f));
            UiFactory.AddText(reloadLabel, "RECARGAR", 17, TextAnchor.MiddleCenter, UiTheme.TextMuted, true);
            TapTargets.Add(new TapTarget { Rect = ReloadButton, OnTap = () => ReloadTapped?.Invoke() });
            ReloadButton.gameObject.SetActive(false);

            BuildSettings();
        }

        /// <summary>Shows or hides the reload button.</summary>
        public void SetReloadButton(bool show)
        {
            if (ReloadButton.gameObject.activeSelf != show) ReloadButton.gameObject.SetActive(show);
        }

        // ---- Small pieces

        /// <summary>A dark rounded chip that grows to fit its one line of text.</summary>
        private RectTransform Chip(string name, Vector2 anchor, Vector2 position, int size, out Text text)
        {
            var chip = UiFactory.NewRect(name, _safeArea, anchor, new Vector2(0.5f, 0.5f), position, new Vector2(200f, size + 22f));
            UiFactory.AddImage(chip, UiFactory.Rounded, UiTheme.Panel);
            var layout = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(26, 26, 8, 8);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var fitter = chip.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var label = UiFactory.NewRect("Text", chip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            text = UiFactory.AddText(label, "", size, TextAnchor.MiddleCenter, UiTheme.Text);
            return chip;
        }

        /// <summary>A faint ring around a round button, so it reads over bright ground too.</summary>
        private static void AddRing(RectTransform button, float size)
        {
            var ring = UiFactory.NewRect("Ring", button, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            UiFactory.AddImage(ring, UiFactory.Ring, new Color(1f, 1f, 1f, 0.22f));
        }

        public void SetPressed(Image image, bool pressed)
        {
            // Only on change: touching a graphic's colour makes its canvas rebuild.
            var target = pressed ? UiTheme.ButtonPressed : image == AttackImage ? _attackIdle : UiTheme.Button;
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

        // ---- Buttons that change with what is in hand

        /// <summary>The button above attack with this label (DEMOLER, "Pieza: Pared"), or hidden for null.</summary>
        public void SetPieceButton(string label)
        {
            bool show = label != null;
            if (PieceButton.gameObject.activeSelf != show) PieceButton.gameObject.SetActive(show);
            if (show && _pieceLabel.text != label) _pieceLabel.text = label;
        }

        /// <summary>The use button (ABRIR, CERRAR, ARMARIO) with a pictogram (a door if none), or hidden for null.</summary>
        public void SetUseButton(string label, Sprite icon = null)
        {
            bool show = label != null;
            if (UseButton.gameObject.activeSelf != show) UseButton.gameObject.SetActive(show);
            if (!show) return;
            if (_useLabel.text != label) _useLabel.text = label;
            var sprite = icon != null ? icon : Icons.Door;
            if (_useIcon.sprite != sprite) _useIcon.sprite = sprite;
        }

        private Image _useIcon;
        private Image _lockIcon;
        private RectTransform _hitMarker;
        private CanvasGroup _hitMarkerGroup;
        private Image[] _hitTicks;
        private float _hitTimer;
        private Image _hurt;
        private float _hurtAlpha;

        /// <summary>A blow of this player's landed (and killed, if <paramref name="kill"/>).</summary>
        public void ShowHitMarker(bool kill)
        {
            _hitTimer = kill ? 0.6f : 0.3f;
            foreach (var tick in _hitTicks) tick.color = kill ? UiTheme.Bad : Color.white;
            _hitMarker.localScale = Vector3.one * (kill ? 1.35f : 1f);
            _hitMarkerGroup.alpha = 1f;
        }

        /// <summary>This player was hurt: the screen's edges flush red, more for a harder blow.</summary>
        public void ShowHurt(float damage)
        {
            _hurtAlpha = Mathf.Max(_hurtAlpha, Mathf.Clamp01(0.35f + damage / 50f));
            _hurt.enabled = true;
        }

        /// <summary>The lock button, shown on a lock the player may manage, its pictogram locked or open.</summary>
        public void SetLockButton(bool show, bool locked)
        {
            if (LockButton.gameObject.activeSelf != show) LockButton.gameObject.SetActive(show);
            if (!show) return;
            var sprite = locked ? Icons.Lock : Icons.Unlock;
            if (_lockIcon.sprite != sprite) _lockIcon.sprite = sprite;
            var colour = locked ? UiTheme.Good : UiTheme.Accent;
            if (_lockIcon.color != colour) _lockIcon.color = colour;
        }

        /// <summary>A line under the crosshair about what is aimed at; empty or null hides it.</summary>
        public void SetAimInfo(string text)
        {
            bool show = !string.IsNullOrEmpty(text);
            if (_aimChip.gameObject.activeSelf != show) _aimChip.gameObject.SetActive(show);
            if (show && _aimInfo.text != text) _aimInfo.text = text;
        }

        /// <summary>
        /// Placing something (the building plan or a campfire in hand): with a piece name the piece
        /// button shows, and attack greens while the piece fits.
        /// </summary>
        public void SetBuildMode(bool on, string pieceName)
        {
            SetPieceButton(on && pieceName != null ? "PIEZA: " + pieceName.ToUpperInvariant() : null);
            if (!on) SetPlaceValid(false);
        }

        /// <summary>What the attack button does with what is in hand: GOLPEAR, COLOCAR, COMER, MEJORAR.</summary>
        public void SetAttackLabel(string label) => _attackLabel.text = label;

        /// <summary>The picture on the attack button: the held item's icon, or an open hand.</summary>
        public void SetAttackIcon(Sprite icon)
        {
            var sprite = icon != null ? icon : Icons.Hand;
            if (_attackIcon.sprite != sprite) _attackIcon.sprite = sprite;
            // Item icons are full-colour pictures; the hand is a white pictogram.
            _attackIcon.color = icon != null ? Color.white : UiTheme.Text;
            _attackIcon.rectTransform.sizeDelta = icon != null ? new Vector2(118f, 118f) : new Vector2(92f, 92f);
        }

        /// <summary>Greens the attack button (COLOCAR) while the piece fits where it is aimed.</summary>
        public void SetPlaceValid(bool valid)
        {
            _attackIdle = valid ? UiTheme.GoodFill : UiTheme.Button;
            if (AttackImage.color != UiTheme.ButtonPressed && AttackImage.color != _attackIdle) AttackImage.color = _attackIdle;
        }

        // ---- Crafting (the panel itself is CraftUi)

        public RectTransform CraftPanel => _craft?.Panel;
        public bool CraftPanelOpen => _craft != null && _craft.IsOpen;
        public bool SettingsOpen => _settings != null && _settings.gameObject.activeSelf;

        public void BuildCraftPanel(RecipeBook book, ItemCatalog catalog, Inventory inventory, CraftingQueue queue,
            System.Action<Recipe> onCraft, System.Func<Recipe, bool> knows, System.Action<Recipe> onLearn)
        {
            _craft = new CraftUi(this, book, catalog, inventory, queue, onCraft, knows, onLearn);
        }

        /// <summary>Whether a workbench is near the player (the crafting panel asks when it redraws).</summary>
        public System.Func<bool> NearWorkbench
        {
            set { if (_craft != null) _craft.NearWorkbench = value; }
        }

        /// <summary>Opens the crafting panel (at a workbench: on the weapons tab).</summary>
        public void OpenCraftPanel(int tab = -1)
        {
            if (_craft == null) return;
            if (!_craft.IsOpen) ToggleCraftPanel();
            if (tab >= 0) _craft.SetTab(tab);
        }

        public void ToggleCraftPanel()
        {
            if (_craft == null) return;
            bool open = !_craft.IsOpen;
            if (open) CloseSettings();
            _craft.SetOpen(open);
            if (open) CraftPanelOpened?.Invoke();
        }

        public void CloseCraftPanel() => _craft?.SetOpen(false);

        /// <summary>Shows one tab of the open crafting panel (0 tools, 1 weapons, 2 base, 3 things).</summary>
        public void CraftTab(int tab) => _craft?.SetTab(tab);

        /// <summary>Redraws what can be afforded (after the inventory changes).</summary>
        public void RefreshCraftPanel() => _craft?.Refresh();

        // ---- Settings

        private void BuildSettings()
        {
            var centre = new Vector2(0.5f, 0.5f);
            var top = new Vector2(0.5f, 1f);
            _settings = UiFactory.NewRect("Settings panel", _safeArea, centre, centre, new Vector2(0f, 20f), new Vector2(720f, 400f));
            UiFactory.AddImage(_settings, UiFactory.Rounded, UiTheme.PanelSolid);
            Blockers.Add(_settings);

            var title = UiFactory.NewRect("Title", _settings, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -28f), new Vector2(400f, 50f));
            UiFactory.AddText(title, "AJUSTES", 40, TextAnchor.MiddleLeft, UiTheme.Text, true);
            CloseButton(_settings, CloseSettings);

            var graphics = UiFactory.NewRect("Graphics", _settings, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -118f), new Vector2(400f, 40f));
            UiFactory.AddText(graphics, "GRÁFICOS", 26, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            string[] names = { "BAJO", "MEDIO", "ALTO" };
            for (int i = 0; i < 3; i++)
            {
                int tier = i;
                var b = UiFactory.NewRect(names[i], _settings, top, top, new Vector2(-212f + i * 212f, -200f), new Vector2(200f, 76f));
                _qualityButtons[i] = UiFactory.AddImage(b, UiFactory.Soft, UiTheme.Slot);
                var l = UiFactory.NewRect("Label", b, centre, centre, Vector2.zero, new Vector2(190f, 60f));
                UiFactory.AddText(l, names[i], 28, TextAnchor.MiddleCenter, UiTheme.Text, true);
                TapTargets.Add(new TapTarget { Rect = b, OnTap = () => QualityChosen?.Invoke(tier) });
            }

            var fpsRow = UiFactory.NewRect("FPS row", _settings, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -300f), new Vector2(400f, 40f));
            UiFactory.AddText(fpsRow, "MOSTRAR FPS", 26, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            var toggle = UiFactory.NewRect("FPS toggle", _settings, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -282f), new Vector2(200f, 76f));
            UiFactory.AddImage(toggle, UiFactory.Soft, UiTheme.Slot);
            var tl = UiFactory.NewRect("Label", toggle, centre, centre, Vector2.zero, new Vector2(190f, 60f));
            _fpsToggleLabel = UiFactory.AddText(tl, "SÍ", 28, TextAnchor.MiddleCenter, UiTheme.Text, true);
            TapTargets.Add(new TapTarget
            {
                Rect = toggle,
                OnTap = () =>
                {
                    _showFps = !_showFps;
                    _fpsToggleLabel.text = _showFps ? "SÍ" : "NO";
                    _fps.text = "";
                },
            });
            _settings.gameObject.SetActive(false);
        }

        /// <summary>The round X in a panel's top right corner.</summary>
        internal void CloseButton(RectTransform panel, System.Action onTap)
        {
            var topRight = new Vector2(1f, 1f);
            var close = UiFactory.NewRect("Close", panel, topRight, topRight, new Vector2(-20f, -20f), new Vector2(68f, 68f));
            UiFactory.AddImage(close, UiFactory.Soft, UiTheme.Slot);
            UiFactory.AddIcon(close, Icons.Close, Vector2.zero, 30f, UiTheme.Text);
            TapTargets.Add(new TapTarget { Rect = close, OnTap = onTap });
        }

        public void ToggleSettings()
        {
            bool open = !_settings.gameObject.activeSelf;
            if (open) CloseCraftPanel();
            _settings.gameObject.SetActive(open);
        }

        private void CloseSettings() => _settings.gameObject.SetActive(false);

        /// <summary>Marks the current graphics level in settings (0 low, 1 medium, 2 high).</summary>
        public void SetQuality(int tier)
        {
            for (int i = 0; i < _qualityButtons.Length; i++)
                _qualityButtons[i].color = i == tier ? UiTheme.Accent.WithAlpha(0.9f) : UiTheme.Slot;
        }

        public void SetQualityLabel(string tierName) { }

        // ---- Status

        /// <summary>The clock, as DayNight words it ("Día 09:30", "Noche 22:10").</summary>
        public void SetClock(string text)
        {
            bool night = text.StartsWith("Noche");
            int space = text.IndexOf(' ');
            _clock.text = space >= 0 ? text.Substring(space + 1) : text;
            if (night == _clockNight && _clockIcon.sprite != null) return;
            _clockNight = night;
            _clockIcon.sprite = night ? Icons.Moon : Icons.Sun;
            _clockIcon.color = night ? new Color(0.72f, 0.8f, 1f) : UiTheme.Food;
        }

        /// <summary>The online chip beside the clock: "3" with a green dot, or hidden for null.</summary>
        public void SetOnline(string text, bool good)
        {
            bool show = text != null;
            if (_online.gameObject.activeSelf != show) _online.gameObject.SetActive(show);
            if (!show) return;
            _onlineText.text = text;
            _onlineDot.color = good ? UiTheme.Good : UiTheme.Accent;
            _online.sizeDelta = new Vector2(Mathf.Max(96f, 52f + _onlineText.preferredWidth), 48f);
        }

        /// <summary>A hint or a warning above the belt, for a moment.</summary>
        public void ShowToast(string message)
        {
            _toast.text = message;
            _toastTimer = 2.2f;
            _toastGroup.alpha = 1f;
        }

        /// <summary>
        /// Items picked up, made or given back: slide in at the side with their icon. The same item
        /// again while its line still shows adds to that line.
        /// </summary>
        public void ShowPickup(string key, Sprite icon, int amount, string name) => _feed.Add(key, icon, amount, name);

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
                string fps = $"{Mathf.RoundToInt(_frames / _elapsed)} FPS · {Mathf.RoundToInt(_worstFrame * 1000f)} ms";
                if (_showFps) _fps.text = fps;
                // Every 5 s into the log too, so automated runs can read the frame rate.
                if (++_windows % 10 == 0) Debug.Log("[Oxfall] " + fps);
                _frames = 0;
                _elapsed = 0f;
                _worstFrame = 0f;
            }

            if (_toastTimer > 0f)
            {
                _toastTimer -= dt;
                _toastGroup.alpha = Mathf.Clamp01(_toastTimer / 0.4f);
            }
            if (_hitTimer > 0f)
            {
                _hitTimer -= dt;
                _hitMarkerGroup.alpha = Mathf.Clamp01(_hitTimer / 0.15f);
            }
            if (_hurt.enabled)
            {
                _hurtAlpha = Mathf.MoveTowards(_hurtAlpha, 0f, dt * 0.9f);
                _hurt.color = new Color(0.75f, 0.05f, 0.04f, _hurtAlpha);
                if (_hurtAlpha <= 0f) _hurt.enabled = false;
            }

            if (HeadingSource != null) _compass.SetHeading(HeadingSource.eulerAngles.y);
            _feed.Tick(dt);
            _craft?.Tick();
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

    /// <summary>
    /// A strip across the top of the screen: the heading in degrees in the middle, cardinal letters
    /// and ticks sliding past as the player turns. On its own canvas, so turning redraws only it.
    /// </summary>
    public sealed class Compass
    {
        private const float Width = 560f;
        private const float PixelsPerDegree = 4f;
        private readonly List<RectTransform> _marks = new List<RectTransform>();
        private readonly List<float> _angles = new List<float>();
        private Text _degrees;
        private float _shown = float.NaN;

        /// <summary>The whole compass, to tap for the map.</summary>
        public RectTransform Root { get; private set; }
        public RectTransform MapChip { get; private set; }

        public static Compass Create(RectTransform parent)
        {
            var c = new Compass();
            var top = new Vector2(0.5f, 1f);
            var root = UiFactory.NewRect("Compass", parent, top, top, new Vector2(0f, -18f), new Vector2(Width, 74f));
            root.gameObject.AddComponent<Canvas>();
            c.Root = root;
            // A small map at its left end: tap for the map.
            var mapChip = UiFactory.NewRect("Map", root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(-8f, 0f), new Vector2(44f, 44f));
            UiFactory.AddImage(mapChip, UiFactory.Soft, UiTheme.Panel.WithAlpha(0.55f));
            UiFactory.AddIcon(mapChip, Icons.Map, Vector2.zero, 28f, UiTheme.Text);
            c.MapChip = mapChip;
            var strip = UiFactory.NewRect("Strip", root, top, top, Vector2.zero, new Vector2(Width, 44f));
            UiFactory.AddImage(strip, UiFactory.Soft, UiTheme.Panel.WithAlpha(0.55f));
            strip.gameObject.AddComponent<RectMask2D>();
            string[] letters = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
            for (int a = 0; a < 360; a += 15)
            {
                bool major = a % 45 == 0;
                RectTransform mark;
                if (major)
                {
                    mark = UiFactory.NewRect(letters[a / 45], strip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 40f));
                    UiFactory.AddText(mark, letters[a / 45], a % 90 == 0 ? 28 : 22, TextAnchor.MiddleCenter,
                        a == 0 ? UiTheme.Accent : UiTheme.Text, true);
                }
                else
                {
                    mark = UiFactory.NewRect("Tick", strip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, 12f));
                    UiFactory.AddImage(mark, null, UiTheme.TextFaint);
                }
                c._marks.Add(mark);
                c._angles.Add(a);
            }
            var pointer = UiFactory.NewRect("Pointer", root, top, top, new Vector2(0f, -40f), new Vector2(3f, 10f));
            UiFactory.AddImage(pointer, null, UiTheme.Accent);
            var degrees = UiFactory.NewRect("Degrees", root, top, top, new Vector2(0f, -50f), new Vector2(120f, 26f));
            c._degrees = UiFactory.AddText(degrees, "", 20, TextAnchor.MiddleCenter, UiTheme.TextMuted, true);
            return c;
        }

        public void SetHeading(float yaw)
        {
            if (!float.IsNaN(_shown) && Mathf.Abs(Mathf.DeltaAngle(_shown, yaw)) < 0.25f) return;
            _shown = yaw;
            float half = Width * 0.5f + 30f;
            for (int i = 0; i < _marks.Count; i++)
            {
                float x = Mathf.DeltaAngle(yaw, _angles[i]) * PixelsPerDegree;
                bool show = Mathf.Abs(x) < half;
                if (_marks[i].gameObject.activeSelf != show) _marks[i].gameObject.SetActive(show);
                if (show) _marks[i].anchoredPosition = new Vector2(x, 0f);
            }
            _degrees.text = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f)).ToString();
        }
    }

    /// <summary>
    /// Pickups on the right edge, newest on top: an icon and "+30 Madera" that slide in, stay a few
    /// seconds and fade. The same item picked up again while shown adds to its line.
    /// </summary>
    public sealed class PickupFeed
    {
        private const int Lines = 5;
        private const float Stay = 3.2f;
        private const float LineHeight = 58f;

        private sealed class Line
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Icon;
            public Text Text;
            public string Key;
            public string Name;
            public int Amount;
            public float Age = 99f;
        }

        private readonly Line[] _lines = new Line[Lines];
        private RectTransform _root;

        public static PickupFeed Create(RectTransform parent)
        {
            var feed = new PickupFeed();
            var topRight = new Vector2(1f, 1f);
            feed._root = UiFactory.NewRect("Pickups", parent, topRight, topRight, new Vector2(-24f, -104f), new Vector2(380f, Lines * LineHeight));
            feed._root.gameObject.AddComponent<Canvas>();
            for (int i = 0; i < Lines; i++)
            {
                var line = new Line();
                line.Rect = UiFactory.NewRect("Pickup", feed._root, topRight, topRight, new Vector2(0f, -i * LineHeight), new Vector2(380f, 50f));
                line.Group = line.Rect.gameObject.AddComponent<CanvasGroup>();
                line.Group.alpha = 0f;
                var back = UiFactory.NewRect("Back", line.Rect, topRight, topRight, Vector2.zero, new Vector2(300f, 50f));
                UiFactory.AddImage(back, UiFactory.Soft, UiTheme.Panel);
                var accent = UiFactory.NewRect("Edge", back, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(4f, 38f));
                UiFactory.AddImage(accent, null, UiTheme.Good);
                line.Icon = UiFactory.AddIcon(back, null, new Vector2(30f, 0f), 42f, Color.white, new Vector2(0f, 0.5f));
                var text = UiFactory.NewRect("Text", back, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(60f, 0f), new Vector2(226f, 46f));
                line.Text = UiFactory.AddText(text, "", 26, TextAnchor.MiddleLeft, UiTheme.Text, true);
                feed._lines[i] = line;
            }
            return feed;
        }

        public void Add(string key, Sprite icon, int amount, string name)
        {
            // Still showing: count it on the same line.
            foreach (var line in _lines)
            {
                if (line.Key != key || line.Age > Stay) continue;
                line.Amount += amount;
                line.Text.text = "+" + line.Amount + " " + line.Name;
                line.Age = 0f;
                line.Group.alpha = 1f;
                if (icon != null) { line.Icon.sprite = icon; line.Icon.enabled = true; }
                return;
            }
            // Newest on top: everything moves down one line.
            for (int i = Lines - 1; i > 0; i--) Copy(_lines[i - 1], _lines[i]);
            var top = _lines[0];
            top.Icon.sprite = icon;
            top.Icon.enabled = icon != null;
            top.Key = key;
            top.Name = name;
            top.Amount = amount;
            top.Text.text = "+" + amount + " " + name;
            top.Age = 0f;
            top.Group.alpha = 1f;
            top.Rect.anchoredPosition = new Vector2(40f, 0f);
        }

        private static void Copy(Line from, Line to)
        {
            to.Icon.sprite = from.Icon.sprite;
            to.Icon.enabled = from.Icon.enabled;
            to.Text.text = from.Text.text;
            to.Key = from.Key;
            to.Name = from.Name;
            to.Amount = from.Amount;
            to.Age = from.Age;
            to.Rect.anchoredPosition = from.Rect.anchoredPosition;
            to.Group.alpha = from.Group.alpha;
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < Lines; i++)
            {
                var line = _lines[i];
                if (line.Age > Stay + 0.5f) continue;
                line.Age += dt;
                float alpha = Mathf.Clamp01((Stay + 0.5f - line.Age) / 0.5f);
                if (!Mathf.Approximately(line.Group.alpha, alpha)) line.Group.alpha = alpha;
                var target = new Vector2(0f, -i * LineHeight);
                if (line.Rect.anchoredPosition != target)
                    line.Rect.anchoredPosition = Vector2.MoveTowards(line.Rect.anchoredPosition, target, 400f * dt);
            }
        }
    }
}
