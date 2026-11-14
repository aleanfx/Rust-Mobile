using System.Collections.Generic;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The crafting panel, laid out like the PC game's: a grid of everything that can be made on the
    /// left (bright when affordable, dim when not), and on the right the chosen item large, what it
    /// needs against what the player has, how long it takes and one big FABRICAR button. What is
    /// being made shows under the grid, and on a small chip above the craft button once the panel is
    /// closed, so the player can go on gathering while it finishes.
    /// </summary>
    public sealed class CraftUi
    {
        private const float Tile = 132f;
        private const float TileGap = 12f;
        private const int Columns = 4;
        private const int MaxIngredients = 4;

        private readonly Hud _hud;
        private readonly ItemCatalog _catalog;
        private readonly Inventory _inventory;
        private readonly CraftingQueue _queue;
        private readonly IReadOnlyList<Recipe> _recipes;

        private readonly List<TileView> _tiles = new List<TileView>();
        private readonly TabView[] _tabs = new TabView[TabNames.Length];
        private int _tab;

        /// <summary>The panel's tabs, like the PC game's categories: tools, weapons, clothing, the base, and things to set down.</summary>
        private static readonly string[] TabNames = { "ÚTILES", "ARMAS", "ROPA", "BASE", "OBJETOS" };

        /// <summary>Which tab an item is made under.</summary>
        public static int TabOf(string itemId)
        {
            switch (itemId)
            {
                case ItemIds.StoneHatchet:
                case ItemIds.StonePickaxe:
                case ItemIds.MetalHatchet:
                case ItemIds.MetalPickaxe:
                case ItemIds.Torch:
                    return 0;
                case ItemIds.WoodenSpear:
                case ItemIds.WoodenBow:
                case ItemIds.Arrow:
                    return 1;
                case ItemIds.BurlapHeadwrap:
                case ItemIds.BurlapShirt:
                case ItemIds.BurlapTrousers:
                case ItemIds.HideHood:
                case ItemIds.HideVest:
                case ItemIds.HidePants:
                case ItemIds.HideBoots:
                    return 2;
                case ItemIds.BuildingPlan:
                case ItemIds.Hammer:
                case ItemIds.WoodenDoor:
                case ItemIds.SheetMetalDoor:
                case ItemIds.CodeLock:
                case ItemIds.ToolCupboard:
                    return 3;
                default:
                    return 4;
            }
        }

        private sealed class TabView
        {
            public RectTransform Rect;
            public Image Back;
            public Image Underline;
            public Text Label;
            public RectTransform Badge;
            public Text BadgeText;
        }
        private readonly IngredientRow[] _rows = new IngredientRow[MaxIngredients];
        private readonly Image[] _queueIcons = new Image[CraftingQueue.MaxJobs];
        private Recipe _selected;

        private Image _bigIcon;
        private Text _bigName;
        private Text _name;
        private Text _description;
        private Text _time;
        private Image _craftButton;
        private Text _craftLabel;
        private RectTransform _queueRoot;
        private RectTransform _queueFill;
        private Text _queueText;

        private RectTransform _chip;
        private Image _chipIcon;
        private RectTransform _chipFill;
        private Text _chipText;
        private int _shownJobs = -1;
        private int _shownSeconds = -1;

        public RectTransform Panel { get; }
        public bool IsOpen => Panel.gameObject.activeSelf;

        private sealed class TileView
        {
            public Recipe Recipe;
            public int Tab;
            public RectTransform Rect;
            public Image Back;
            public Image Frame;
            public Image Icon;
            public Text Label;
        }

        private sealed class IngredientRow
        {
            public RectTransform Rect;
            public Image Icon;
            public Text Name;
            public Text Need;
            public Text Have;
        }

        public CraftUi(Hud hud, RecipeBook book, ItemCatalog catalog, Inventory inventory, CraftingQueue queue,
            System.Action<Recipe> onCraft)
        {
            _hud = hud;
            _catalog = catalog;
            _inventory = inventory;
            _queue = queue;
            _recipes = book.All;

            var centre = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            const float left = 40f;
            float gridWidth = Columns * (Tile + TileGap) - TileGap;
            float detailsX = left + gridWidth + 44f;
            const float detailsWidth = 520f;

            Panel = UiFactory.NewRect("Craft panel", hud.SafeArea, centre, centre, new Vector2(0f, 30f),
                new Vector2(detailsX + detailsWidth + 40f, 740f));
            UiFactory.AddImage(Panel, UiFactory.Rounded, UiTheme.PanelSolid);
            hud.Blockers.Add(Panel);

            var title = UiFactory.NewRect("Title", Panel, topLeft, topLeft, new Vector2(left, -26f), new Vector2(500f, 54f));
            UiFactory.AddText(title, "FABRICAR", 42, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var accent = UiFactory.NewRect("Accent", Panel, topLeft, topLeft, new Vector2(left, -84f), new Vector2(56f, 4f));
            UiFactory.AddImage(accent, null, UiTheme.Accent);
            hud.CloseButton(Panel, () => SetOpen(false));

            // Under the title, the tabs; each shows how many of its things can be made now.
            float tabWidth = (gridWidth - TileGap * (TabNames.Length - 1)) / TabNames.Length;
            for (int t = 0; t < TabNames.Length; t++)
            {
                int index = t;
                var tab = new TabView();
                tab.Rect = UiFactory.NewRect("Tab " + TabNames[t], Panel, topLeft, topLeft, new Vector2(left + t * (tabWidth + TileGap), -104f), new Vector2(tabWidth, 54f));
                tab.Back = UiFactory.AddImage(tab.Rect, UiFactory.Soft, UiTheme.Slot);
                var label = UiFactory.NewRect("Label", tab.Rect, centre, centre, new Vector2(0f, 2f), new Vector2(tabWidth - 8f, 40f));
                tab.Label = UiFactory.AddText(label, TabNames[t], 24, TextAnchor.MiddleCenter, UiTheme.TextMuted, true);
                var underline = UiFactory.NewRect("Underline", tab.Rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(tabWidth - 24f, 4f));
                tab.Underline = UiFactory.AddImage(underline, null, UiTheme.Accent);
                tab.Badge = UiFactory.NewRect("Badge", tab.Rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(8f, 10f), new Vector2(34f, 34f));
                UiFactory.AddImage(tab.Badge, UiFactory.Circle, UiTheme.Good);
                var badgeText = UiFactory.NewRect("Count", tab.Badge, centre, centre, new Vector2(0f, 1f), new Vector2(34f, 30f));
                tab.BadgeText = UiFactory.AddText(badgeText, "", 20, TextAnchor.MiddleCenter, Color.white, true);
                _tabs[t] = tab;
                hud.TapTargets.Add(new Hud.TapTarget { Rect = tab.Rect, OnTap = () => SetTab(index) });
            }

            // Left: one tile per recipe, laid out in its tab's grid.
            var placed = new int[TabNames.Length];
            for (int i = 0; i < _recipes.Count; i++)
            {
                var recipe = _recipes[i];
                int tabOf = TabOf(recipe.OutputItemId);
                int n = placed[tabOf]++;
                int row = n / Columns, column = n % Columns;
                var tile = MakeTile(recipe, new Vector2(left + column * (Tile + TileGap), -176f - row * (Tile + TileGap)));
                tile.Tab = tabOf;
                _tiles.Add(tile);
                hud.TapTargets.Add(new Hud.TapTarget { Rect = tile.Rect, OnTap = () => Select(recipe) });
            }

            // Under the grid: the queue.
            _queueRoot = UiFactory.NewRect("Queue", Panel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(left, 36f), new Vector2(gridWidth, 150f));
            var queueTitle = UiFactory.NewRect("Title", _queueRoot, topLeft, topLeft, Vector2.zero, new Vector2(gridWidth, 34f));
            _queueText = UiFactory.AddText(queueTitle, "", 24, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            var track = UiFactory.NewRect("Track", _queueRoot, topLeft, topLeft, new Vector2(0f, -44f), new Vector2(gridWidth, 8f));
            UiFactory.AddImage(track, UiFactory.Soft, UiTheme.Slot);
            _queueFill = UiFactory.NewRect("Fill", track, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 8f));
            UiFactory.AddImage(_queueFill, UiFactory.Soft, UiTheme.Accent);
            for (int i = 0; i < _queueIcons.Length; i++)
            {
                var slot = UiFactory.NewRect("Job", _queueRoot, topLeft, topLeft, new Vector2(i * 70f, -64f), new Vector2(64f, 64f));
                UiFactory.AddImage(slot, UiFactory.Soft, UiTheme.Slot);
                _queueIcons[i] = UiFactory.AddIcon(slot, null, Vector2.zero, 56f, Color.white);
                slot.gameObject.SetActive(false);
            }

            // Right: the chosen item.
            var details = UiFactory.NewRect("Details", Panel, topLeft, topLeft, new Vector2(detailsX, -26f), new Vector2(detailsWidth, 690f));
            UiFactory.AddImage(details, UiFactory.Soft, new Color(1f, 1f, 1f, 0.035f));
            var art = UiFactory.NewRect("Art", details, topLeft, topLeft, new Vector2(24f, -24f), new Vector2(170f, 170f));
            UiFactory.AddImage(art, UiFactory.Soft, UiTheme.Slot);
            var glow = UiFactory.NewRect("Glow", art, centre, centre, Vector2.zero, new Vector2(190f, 190f));
            UiFactory.AddImage(glow, Icons.Glow, UiTheme.Accent.WithAlpha(0.18f));
            _bigIcon = UiFactory.AddIcon(art, null, Vector2.zero, 150f, Color.white);
            var bigName = UiFactory.NewRect("Fallback", art, centre, centre, Vector2.zero, new Vector2(160f, 160f));
            _bigName = UiFactory.AddText(bigName, "", 24, TextAnchor.MiddleCenter, UiTheme.Text, true);
            _bigName.horizontalOverflow = HorizontalWrapMode.Wrap;

            var name = UiFactory.NewRect("Name", details, topLeft, topLeft, new Vector2(214f, -28f), new Vector2(290f, 56f));
            _name = UiFactory.AddText(name, "", 40, TextAnchor.UpperLeft, UiTheme.Text, true);
            _name.horizontalOverflow = HorizontalWrapMode.Wrap;
            var time = UiFactory.NewRect("Time", details, topLeft, topLeft, new Vector2(214f, -150f), new Vector2(290f, 36f));
            UiFactory.AddIcon(time, Icons.Clock, new Vector2(14f, 0f), 26f, UiTheme.TextMuted, new Vector2(0f, 0.5f));
            var timeText = UiFactory.NewRect("Text", time, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(38f, 0f), new Vector2(250f, 36f));
            _time = UiFactory.AddText(timeText, "", 26, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);

            var description = UiFactory.NewRect("Description", details, topLeft, topLeft, new Vector2(24f, -214f), new Vector2(detailsWidth - 48f, 100f));
            _description = UiFactory.AddText(description, "", 24, TextAnchor.UpperLeft, UiTheme.TextMuted);
            _description.horizontalOverflow = HorizontalWrapMode.Wrap;

            // The ingredients: a small table, like the PC game's (item, needed, owned).
            var header = UiFactory.NewRect("Header", details, topLeft, topLeft, new Vector2(24f, -326f), new Vector2(detailsWidth - 48f, 30f));
            AddCell(header, "MATERIAL", 0f, 260f, TextAnchor.MiddleLeft);
            AddCell(header, "NECESITAS", 270f, 100f, TextAnchor.MiddleRight);
            AddCell(header, "TIENES", 372f, 88f, TextAnchor.MiddleRight);
            for (int i = 0; i < MaxIngredients; i++)
                _rows[i] = MakeRow(details, new Vector2(24f, -362f - i * 52f), detailsWidth - 48f);

            var button = UiFactory.NewRect("Craft", details, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(detailsWidth - 48f, 92f));
            _craftButton = UiFactory.AddImage(button, UiFactory.Soft, UiTheme.Accent);
            var craftLabel = UiFactory.NewRect("Label", button, centre, centre, Vector2.zero, new Vector2(detailsWidth - 60f, 70f));
            _craftLabel = UiFactory.AddText(craftLabel, "FABRICAR", 36, TextAnchor.MiddleCenter, Color.white, true);
            hud.TapTargets.Add(new Hud.TapTarget
            {
                Rect = button,
                OnTap = () =>
                {
                    if (_selected != null) onCraft(_selected);
                    Refresh();
                },
            });

            Panel.gameObject.SetActive(false);

            // On the HUD, above the craft button: what is being made while the panel is closed.
            var bottomRight = new Vector2(1f, 0f);
            _chip = UiFactory.NewRect("Crafting", hud.SafeArea, bottomRight, new Vector2(1f, 0f), new Vector2(-30f, 546f), new Vector2(210f, 60f));
            UiFactory.AddImage(_chip, UiFactory.Soft, UiTheme.Panel);
            _chip.gameObject.AddComponent<Canvas>();
            _chipIcon = UiFactory.AddIcon(_chip, null, new Vector2(32f, 2f), 48f, Color.white, new Vector2(0f, 0.5f));
            var chipText = UiFactory.NewRect("Text", _chip, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(66f, 3f), new Vector2(136f, 40f));
            _chipText = UiFactory.AddText(chipText, "", 26, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var chipTrack = UiFactory.NewRect("Track", _chip, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(194f, 4f));
            UiFactory.AddImage(chipTrack, null, UiTheme.Slot);
            _chipFill = UiFactory.NewRect("Fill", chipTrack, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(0f, 4f));
            UiFactory.AddImage(_chipFill, null, UiTheme.Accent);
            _chip.gameObject.SetActive(false);

            Select(_recipes.Count > 0 ? _recipes[0] : null);
            SetTab(0);
        }

        /// <summary>Shows one tab's things (choosing the first that can be made now, if the choice was elsewhere).</summary>
        public void SetTab(int tab)
        {
            _tab = Mathf.Clamp(tab, 0, TabNames.Length - 1);
            foreach (var tile in _tiles) tile.Rect.gameObject.SetActive(tile.Tab == _tab);
            if (_selected == null || TabOf(_selected.OutputItemId) != _tab)
            {
                Recipe first = null, affordable = null;
                foreach (var tile in _tiles)
                {
                    if (tile.Tab != _tab) continue;
                    if (first == null) first = tile.Recipe;
                    if (affordable == null && tile.Recipe.CanAfford(_inventory)) affordable = tile.Recipe;
                }
                _selected = affordable ?? first;
            }
            Refresh();
        }

        public void SetOpen(bool open)
        {
            if (open == IsOpen) return;
            Panel.gameObject.SetActive(open);
            if (open)
            {
                // Open on something that can be made now, if anything can.
                if (_selected == null || !_selected.CanAfford(_inventory))
                    foreach (var r in _recipes)
                        if (r.CanAfford(_inventory)) { _selected = r; break; }
                SetTab(_selected != null ? TabOf(_selected.OutputItemId) : _tab);
            }
            _shownJobs = -1;
            Tick();
        }

        public void Select(Recipe recipe)
        {
            _selected = recipe;
            if (recipe != null && TabOf(recipe.OutputItemId) != _tab) SetTab(TabOf(recipe.OutputItemId));
            else Refresh();
        }

        /// <summary>Redraws what can be afforded and the chosen recipe (after the inventory changes).</summary>
        public void Refresh()
        {
            if (!IsOpen) return;
            for (int t = 0; t < _tabs.Length; t++)
            {
                var tab = _tabs[t];
                bool on = t == _tab;
                tab.Back.color = on ? UiTheme.SlotHover : UiTheme.Slot;
                tab.Label.color = on ? UiTheme.Text : UiTheme.TextMuted;
                tab.Underline.enabled = on;
                int can = 0;
                foreach (var tile in _tiles)
                    if (tile.Tab == t && tile.Recipe.CanAfford(_inventory)) can++;
                tab.Badge.gameObject.SetActive(can > 0);
                tab.BadgeText.text = can.ToString();
            }
            foreach (var tile in _tiles)
            {
                bool can = tile.Recipe.CanAfford(_inventory);
                bool chosen = tile.Recipe == _selected;
                var icon = ItemIcons.Get(tile.Recipe.OutputItemId);
                if (tile.Icon.sprite != icon) tile.Icon.sprite = icon;
                tile.Icon.enabled = icon != null;
                tile.Label.text = icon == null ? _catalog.Get(tile.Recipe.OutputItemId).DisplayName : "";
                tile.Icon.color = can ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                tile.Back.color = chosen ? UiTheme.SlotHover : UiTheme.Slot;
                tile.Frame.color = chosen ? UiTheme.Accent : can ? UiTheme.Good.WithAlpha(0.55f) : Color.clear;
            }

            if (_selected == null) return;
            var def = _catalog.Get(_selected.OutputItemId);
            var big = ItemIcons.Get(def.Id);
            _bigIcon.sprite = big;
            _bigIcon.enabled = big != null;
            _bigName.text = big == null ? def.DisplayName : "";
            _name.text = def.DisplayName + (_selected.OutputAmount > 1 ? " x" + _selected.OutputAmount : "");
            _description.text = def.Description;
            _time.text = $"{_selected.CraftSeconds:0} s";

            for (int i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                bool show = i < _selected.Ingredients.Count;
                row.Rect.gameObject.SetActive(show);
                if (!show) continue;
                var ingredient = _selected.Ingredients[i];
                int have = _inventory.Count(ingredient.ItemId);
                var icon = ItemIcons.Get(ingredient.ItemId);
                row.Icon.sprite = icon;
                row.Icon.enabled = icon != null;
                row.Name.text = _catalog.Get(ingredient.ItemId).DisplayName;
                row.Need.text = ingredient.Amount.ToString();
                row.Have.text = have.ToString();
                row.Have.color = have >= ingredient.Amount ? UiTheme.Good : UiTheme.Bad;
            }

            bool afford = _selected.CanAfford(_inventory);
            bool full = _queue.Jobs.Count >= CraftingQueue.MaxJobs;
            _craftButton.color = afford && !full ? UiTheme.Accent : UiTheme.Slot;
            _craftLabel.text = full ? "COLA LLENA" : afford ? "FABRICAR" : "FALTAN MATERIALES";
            _craftLabel.color = afford && !full ? Color.white : UiTheme.TextMuted;
        }

        /// <summary>Every frame: the queue's progress, in the panel or on the chip.</summary>
        public void Tick()
        {
            var jobs = _queue.Jobs;
            bool busy = jobs.Count > 0;
            bool chip = busy && !IsOpen;
            if (_chip.gameObject.activeSelf != chip) _chip.gameObject.SetActive(chip);
            if (!busy && _shownJobs == 0) return;

            float progress = busy ? jobs[0].Progress : 0f;
            int seconds = busy ? Mathf.CeilToInt(jobs[0].Recipe.CraftSeconds - jobs[0].Elapsed) : 0;
            if (chip)
            {
                _chipFill.sizeDelta = new Vector2(194f * progress, 4f);
                if (seconds != _shownSeconds || jobs.Count != _shownJobs)
                {
                    var icon = ItemIcons.Get(jobs[0].Recipe.OutputItemId);
                    _chipIcon.sprite = icon;
                    _chipIcon.enabled = icon != null;
                    _chipText.text = jobs.Count > 1 ? $"{seconds} s  · +{jobs.Count - 1}" : $"{seconds} s";
                }
            }
            if (IsOpen)
            {
                float width = _queueRoot.sizeDelta.x;
                _queueFill.sizeDelta = new Vector2(width * progress, 8f);
                if (seconds != _shownSeconds || jobs.Count != _shownJobs)
                {
                    _queueText.text = busy
                        ? $"FABRICANDO {_catalog.Get(jobs[0].Recipe.OutputItemId).DisplayName.ToUpperInvariant()}  ·  {seconds} s"
                        : "NADA EN COLA";
                    for (int i = 0; i < _queueIcons.Length; i++)
                    {
                        bool show = i < jobs.Count;
                        var slot = _queueIcons[i].transform.parent.gameObject;
                        if (slot.activeSelf != show) slot.SetActive(show);
                        if (show) _queueIcons[i].sprite = ItemIcons.Get(jobs[i].Recipe.OutputItemId);
                    }
                    Refresh();
                }
            }
            _shownSeconds = seconds;
            _shownJobs = jobs.Count;
        }

        private TileView MakeTile(Recipe recipe, Vector2 position)
        {
            var topLeft = new Vector2(0f, 1f);
            var centre = new Vector2(0.5f, 0.5f);
            var tile = new TileView { Recipe = recipe };
            tile.Rect = UiFactory.NewRect("Recipe", Panel, topLeft, topLeft, position, new Vector2(Tile, Tile));
            tile.Back = UiFactory.AddImage(tile.Rect, UiFactory.Soft, UiTheme.Slot);
            var frame = UiFactory.NewRect("Frame", tile.Rect, centre, centre, Vector2.zero, new Vector2(Tile, Tile));
            tile.Frame = UiFactory.AddImage(frame, UiFactory.Outline, Color.clear);
            tile.Icon = UiFactory.AddIcon(tile.Rect, null, Vector2.zero, Tile - 20f, Color.white);
            var label = UiFactory.NewRect("Name", tile.Rect, centre, centre, Vector2.zero, new Vector2(Tile - 12f, Tile - 12f));
            tile.Label = UiFactory.AddText(label, "", 22, TextAnchor.MiddleCenter, UiTheme.Text, true);
            tile.Label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return tile;
        }

        private static void AddCell(RectTransform row, string text, float x, float width, TextAnchor anchor)
        {
            var cell = UiFactory.NewRect(text, row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(width, 30f));
            UiFactory.AddText(cell, text, 20, anchor, UiTheme.TextFaint, true);
        }

        private static IngredientRow MakeRow(RectTransform parent, Vector2 position, float width)
        {
            var topLeft = new Vector2(0f, 1f);
            var left = new Vector2(0f, 0.5f);
            var row = new IngredientRow();
            row.Rect = UiFactory.NewRect("Ingredient", parent, topLeft, topLeft, position, new Vector2(width, 46f));
            UiFactory.AddImage(row.Rect, UiFactory.Soft, UiTheme.Slot.WithAlpha(0.5f));
            row.Icon = UiFactory.AddIcon(row.Rect, null, new Vector2(26f, 0f), 40f, Color.white, left);
            var name = UiFactory.NewRect("Name", row.Rect, left, left, new Vector2(56f, 0f), new Vector2(204f, 40f));
            row.Name = UiFactory.AddText(name, "", 26, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var need = UiFactory.NewRect("Need", row.Rect, left, left, new Vector2(270f, 0f), new Vector2(100f, 40f));
            row.Need = UiFactory.AddText(need, "", 26, TextAnchor.MiddleRight, UiTheme.Text, true);
            var have = UiFactory.NewRect("Have", row.Rect, left, left, new Vector2(372f, 0f), new Vector2(88f, 40f));
            row.Have = UiFactory.AddText(have, "", 26, TextAnchor.MiddleRight, UiTheme.Good, true);
            return row;
        }
    }
}
