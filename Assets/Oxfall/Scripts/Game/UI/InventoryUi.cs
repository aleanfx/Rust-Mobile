using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The hotbar along the bottom of the screen (tap a slot to hold it, tap it again to empty the
    /// hands) and the inventory panel behind the MOCHILA button: all 30 slots, where tapping one
    /// item and then another slot moves it there. Moves go through <see cref="GameActions"/>: the
    /// shared rules alone, the server online.
    /// </summary>
    public sealed class InventoryUi
    {
        private const float SlotSize = 118f;
        private const float Gap = 10f;
        private const float Pitch = SlotSize + Gap;
        private const int Columns = Inventory.HotbarSize;

        // The slot held in hand is framed in the accent; one picked up to move, in white.
        private static Color Picked => UiTheme.Text;
        private static Color Held => UiTheme.Accent;

        private readonly Hud _hud;
        private readonly Inventory _inventory;
        private readonly Hotbar _hotbar;
        private readonly GameActions _actions;
        private readonly SlotView[] _bar = new SlotView[Inventory.HotbarSize];
        private readonly SlotView[] _grid;

        private RectTransform _barRoot;
        private RectTransform _panel;
        private Text _name;
        private Text _detail;
        private Image _bigIcon;
        private RectTransform _art;
        private RectTransform _quickButton;
        private Text _quickLabel;
        private RectTransform _splitButton;
        private int _picked = -1;

        public bool IsOpen => _panel.gameObject.activeSelf;
        public RectTransform Panel => _panel;
        public RectTransform BagButton { get; private set; }

        private sealed class SlotView
        {
            public RectTransform Rect;
            public Image Back;
            public Image Frame;
            public Image Icon;
            public Text Label;
            public Text Amount;
        }

        public InventoryUi(Hud hud, Inventory inventory, Hotbar hotbar, GameActions actions)
        {
            _hud = hud;
            _inventory = inventory;
            _hotbar = hotbar;
            _actions = actions;
            _grid = new SlotView[inventory.SlotCount];
            BuildBar();
            BuildPanel();
            _hud.CraftPanelOpened += Close;
            Refresh();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else
            {
                _hud.CloseCraftPanel();
                _picked = -1;
                _panel.gameObject.SetActive(true);
                _barRoot.gameObject.SetActive(false);
                Refresh();
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            _panel.gameObject.SetActive(false);
            _barRoot.gameObject.SetActive(true);
            _picked = -1;
            Refresh();
        }

        /// <summary>Taps a slot of the panel: picks an item, or moves the picked one there.</summary>
        public void TapSlot(int slot)
        {
            if (_picked < 0)
            {
                if (!_inventory[slot].IsEmpty) _picked = slot;
            }
            else
            {
                if (slot != _picked) _actions.MoveSlot(_picked, slot);
                _picked = -1;
            }
            Refresh();
        }

        /// <summary>Redraws every slot. Called when the inventory or the chosen hotbar slot changes.</summary>
        public void Refresh()
        {
            if (_picked >= 0 && _inventory[_picked].IsEmpty) _picked = -1;
            for (int i = 0; i < _bar.Length; i++)
                Draw(_bar[i], _inventory[i], i == _hotbar.Selected ? Held : Color.clear);
            if (!IsOpen) return;
            for (int i = 0; i < _grid.Length; i++)
            {
                var frame = i == _picked ? Picked : i == _hotbar.Selected ? Held : Color.clear;
                Draw(_grid[i], _inventory[i], frame);
            }
            DrawDetails();
        }

        private void Draw(SlotView view, ItemStack stack, Color frame)
        {
            if (view.Frame.color != frame) view.Frame.color = frame;
            var back = frame == Color.clear ? UiTheme.Slot : UiTheme.SlotHover;
            if (view.Back.color != back) view.Back.color = back;
            if (stack.IsEmpty)
            {
                view.Icon.enabled = false;
                view.Label.text = "";
                view.Amount.text = "";
                return;
            }
            var def = _inventory.Catalog.Get(stack.ItemId);
            var icon = ItemIcons.Get(stack.ItemId);
            view.Icon.enabled = icon != null;
            if (icon != null && view.Icon.sprite != icon) view.Icon.sprite = icon;
            // Until the icons are ready (or if one failed), the name stands in.
            view.Label.text = icon == null ? def.DisplayName : "";
            view.Amount.text = def.MaxStack > 1 ? stack.Amount.ToString() : "";
        }

        private void DrawDetails()
        {
            bool any = _picked >= 0;
            _quickButton.gameObject.SetActive(any);
            _splitButton.gameObject.SetActive(any && _inventory[_picked].Amount > 1);
            _art.gameObject.SetActive(any);
            if (!any)
            {
                _name.text = "";
                _detail.text = "Toca un objeto y luego otra casilla para moverlo.\n\n" +
                               "Lo que pongas en la barra rápida se puede llevar en la mano.";
                return;
            }
            var stack = _inventory[_picked];
            var def = _inventory.Catalog.Get(stack.ItemId);
            _name.text = def.DisplayName + (stack.Amount > 1 ? "  x" + stack.Amount : "");
            var icon = ItemIcons.Get(stack.ItemId);
            _bigIcon.sprite = icon;
            _bigIcon.enabled = icon != null;
            _detail.text = def.Description + "\n\nToca otra casilla para moverlo.";
            _quickLabel.text = Inventory.IsHotbarSlot(_picked) ? "A LA MOCHILA" : "A LA BARRA";
        }

        private void QuickMove()
        {
            if (_picked < 0) return;
            if (_actions.QuickMove(_picked) == ActionResult.NoSpace)
                _hud.ShowToast(Inventory.IsHotbarSlot(_picked) ? "La mochila está llena" : "La barra está llena");
            _picked = -1;
            Refresh();
        }

        private void Split()
        {
            if (_picked < 0) return;
            if (_actions.Split(_picked) == ActionResult.NoSpace) _hud.ShowToast("No hay casillas libres");
            Refresh();
        }

        private void BuildBar()
        {
            var bottom = new Vector2(0.5f, 0f);
            float width = Columns * Pitch - Gap;
            _barRoot = UiFactory.NewRect("Hotbar", _hud.SafeArea, bottom, bottom, new Vector2(0f, 16f), new Vector2(width, SlotSize));
            _hud.Blockers.Add(_barRoot);
            for (int i = 0; i < _bar.Length; i++)
            {
                int slot = i;
                _bar[i] = MakeSlot(_barRoot, new Vector2(i * Pitch, 0f), Vector2.zero, slot + 1);
                _hud.TapTargets.Add(new Hud.TapTarget { Rect = _bar[i].Rect, OnTap = () => _hotbar.Select(slot) });
            }

            // Left of the hotbar: opens the inventory. The joystick rests above it, clear of it.
            BagButton = UiFactory.NewRect("Bag", _hud.SafeArea, bottom, Vector2.zero,
                new Vector2(-width * 0.5f - 16f - SlotSize, 16f), new Vector2(SlotSize, SlotSize));
            UiFactory.AddImage(BagButton, UiFactory.Soft, UiTheme.Panel);
            UiFactory.AddIcon(BagButton, Icons.Bag, new Vector2(0f, 12f), 58f, UiTheme.Text);
            var label = UiFactory.NewRect("Label", BagButton, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(SlotSize, 28f));
            UiFactory.AddText(label, "MOCHILA", 20, TextAnchor.MiddleCenter, UiTheme.TextMuted, true);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = BagButton, OnTap = Toggle });
        }

        private void BuildPanel()
        {
            var half = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            const float left = 40f, top = -100f;
            float gridWidth = Columns * Pitch - Gap;
            float detailsX = left + gridWidth + 40f;
            const float detailsWidth = 372f;

            _panel = UiFactory.NewRect("Inventory panel", _hud.SafeArea, half, half, new Vector2(0f, 10f),
                new Vector2(detailsX + detailsWidth + 40f, 800f));
            UiFactory.AddImage(_panel, UiFactory.Rounded, UiTheme.PanelSolid);
            _hud.Blockers.Add(_panel);

            var title = UiFactory.NewRect("Title", _panel, topLeft, topLeft, new Vector2(left, -24f), new Vector2(500f, 54f));
            UiFactory.AddText(title, "INVENTARIO", 42, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var accent = UiFactory.NewRect("Accent", _panel, topLeft, topLeft, new Vector2(left, -80f), new Vector2(56f, 4f));
            UiFactory.AddImage(accent, null, UiTheme.Accent);
            _hud.CloseButton(_panel, Close);

            // The backpack: four rows above the hotbar row.
            int backpackRows = (_grid.Length - Inventory.HotbarSize) / Columns;
            for (int i = Inventory.HotbarSize; i < _grid.Length; i++)
            {
                int row = (i - Inventory.HotbarSize) / Columns, column = (i - Inventory.HotbarSize) % Columns;
                AddGridSlot(i, new Vector2(left + column * Pitch, top - row * Pitch));
            }
            float barTop = top - backpackRows * Pitch - 34f;
            var barLabel = UiFactory.NewRect("Hotbar label", _panel, topLeft, topLeft, new Vector2(left, barTop + 32f), new Vector2(gridWidth, 30f));
            UiFactory.AddText(barLabel, "BARRA RÁPIDA", 22, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            for (int i = 0; i < Inventory.HotbarSize; i++)
                AddGridSlot(i, new Vector2(left + i * Pitch, barTop));

            // Right: what the picked item is, and what can be done with it.
            var pane = UiFactory.NewRect("Details", _panel, topLeft, topLeft, new Vector2(detailsX - 20f, top + 10f), new Vector2(detailsWidth + 40f, 680f));
            UiFactory.AddImage(pane, UiFactory.Soft, new Color(1f, 1f, 1f, 0.035f));
            _art = UiFactory.NewRect("Art", _panel, topLeft, topLeft, new Vector2(detailsX, top - 6f), new Vector2(150f, 150f));
            UiFactory.AddImage(_art, UiFactory.Soft, UiTheme.Slot);
            var glow = UiFactory.NewRect("Glow", _art, half, half, Vector2.zero, new Vector2(170f, 170f));
            UiFactory.AddImage(glow, Icons.Glow, UiTheme.Accent.WithAlpha(0.18f));
            _bigIcon = UiFactory.AddIcon(_art, null, Vector2.zero, 132f, Color.white);
            var name = UiFactory.NewRect("Name", _panel, topLeft, topLeft, new Vector2(detailsX, top - 170f), new Vector2(detailsWidth, 50f));
            _name = UiFactory.AddText(name, "", 36, TextAnchor.UpperLeft, UiTheme.Text, true);
            var detail = UiFactory.NewRect("Detail", _panel, topLeft, topLeft, new Vector2(detailsX, top - 224f), new Vector2(detailsWidth, 200f));
            _detail = UiFactory.AddText(detail, "", 24, TextAnchor.UpperLeft, UiTheme.TextMuted);
            _detail.horizontalOverflow = HorizontalWrapMode.Wrap;

            _quickButton = MakeButton("Quick move", new Vector2(detailsX, barTop + 100f), detailsWidth, "", out _quickLabel);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = _quickButton, OnTap = QuickMove });
            _splitButton = MakeButton("Split", new Vector2(detailsX, barTop - 14f), detailsWidth, "DIVIDIR", out _);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = _splitButton, OnTap = Split });

            _panel.gameObject.SetActive(false);
        }

        private void AddGridSlot(int slot, Vector2 position)
        {
            var topLeft = new Vector2(0f, 1f);
            _grid[slot] = MakeSlot(_panel, position, topLeft, Inventory.IsHotbarSlot(slot) ? slot + 1 : 0);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = _grid[slot].Rect, OnTap = () => TapSlot(slot) });
        }

        private RectTransform MakeButton(string name, Vector2 position, float width, string text, out Text label)
        {
            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);
            var button = UiFactory.NewRect(name, _panel, topLeft, topLeft, position, new Vector2(width, 84f));
            UiFactory.AddImage(button, UiFactory.Soft, UiTheme.Accent);
            var labelRect = UiFactory.NewRect("Label", button, half, half, Vector2.zero, new Vector2(width - 20f, 60f));
            label = UiFactory.AddText(labelRect, text, 30, TextAnchor.MiddleCenter, Color.white, true);
            return button;
        }

        /// <param name="number">The hotbar key (1 to 6) shown in the corner, or 0 for none.</param>
        private static SlotView MakeSlot(RectTransform parent, Vector2 position, Vector2 anchor, int number)
        {
            var half = new Vector2(0.5f, 0.5f);
            var view = new SlotView();
            view.Rect = UiFactory.NewRect("Slot", parent, anchor, anchor, position, new Vector2(SlotSize, SlotSize));

            var back = UiFactory.NewRect("Back", view.Rect, half, half, Vector2.zero, new Vector2(SlotSize, SlotSize));
            view.Back = UiFactory.AddImage(back, UiFactory.Soft, UiTheme.Slot);

            var icon = UiFactory.NewRect("Icon", view.Rect, half, half, new Vector2(0f, 4f), new Vector2(SlotSize - 14f, SlotSize - 14f));
            view.Icon = UiFactory.AddImage(icon, null, Color.white);
            view.Icon.preserveAspect = true;
            view.Icon.enabled = false;

            var label = UiFactory.NewRect("Name", view.Rect, half, half, Vector2.zero, new Vector2(SlotSize - 8f, SlotSize - 8f));
            view.Label = UiFactory.AddText(label, "", 20, TextAnchor.MiddleCenter, Color.white);
            view.Label.horizontalOverflow = HorizontalWrapMode.Wrap;

            var amount = UiFactory.NewRect("Amount", view.Rect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 4f), new Vector2(SlotSize - 12f, 34f));
            view.Amount = UiFactory.AddText(amount, "", 26, TextAnchor.LowerRight, UiTheme.Text, true);

            if (number > 0)
            {
                var key = UiFactory.NewRect("Key", view.Rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -4f), new Vector2(30f, 28f));
                UiFactory.AddText(key, number.ToString(), 20, TextAnchor.UpperLeft, UiTheme.TextFaint, true);
            }

            // The frame: an outline over the slot, in a colour when chosen.
            var frame = UiFactory.NewRect("Frame", view.Rect, half, half, Vector2.zero, new Vector2(SlotSize, SlotSize));
            view.Frame = UiFactory.AddImage(frame, UiFactory.Outline, Color.clear);
            return view;
        }
    }
}
