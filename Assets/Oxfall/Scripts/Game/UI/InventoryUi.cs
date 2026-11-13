using Oxfall.Core.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The hotbar along the bottom of the screen (tap a slot to hold it, tap it again to empty the
    /// hands) and the inventory panel behind the MOCHILA button: all 30 slots, where tapping one
    /// item and then another slot moves it there. All moves go through the Core
    /// <see cref="Inventory"/>, which either completes a move or changes nothing.
    /// </summary>
    public sealed class InventoryUi
    {
        private const float SlotSize = 118f;
        private const float Gap = 10f;
        private const float Pitch = SlotSize + Gap;
        private const int Columns = Inventory.HotbarSize;

        private static readonly Color Picked = new Color(0.95f, 0.86f, 0.62f, 0.95f);
        private static readonly Color SlotBack = new Color(0.12f, 0.10f, 0.08f, 0.72f);

        private readonly Hud _hud;
        private readonly Inventory _inventory;
        private readonly Hotbar _hotbar;
        private readonly SlotView[] _bar = new SlotView[Inventory.HotbarSize];
        private readonly SlotView[] _grid;

        private RectTransform _barRoot;
        private RectTransform _panel;
        private Text _name;
        private Text _detail;
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
            public Image Frame;
            public Image Icon;
            public Text Label;
            public Text Amount;
        }

        public InventoryUi(Hud hud, Inventory inventory, Hotbar hotbar)
        {
            _hud = hud;
            _inventory = inventory;
            _hotbar = hotbar;
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
                if (slot != _picked) _inventory.Move(_picked, slot);
                _picked = -1;
            }
            Refresh();
        }

        /// <summary>Redraws every slot. Called when the inventory or the chosen hotbar slot changes.</summary>
        public void Refresh()
        {
            if (_picked >= 0 && _inventory[_picked].IsEmpty) _picked = -1;
            for (int i = 0; i < _bar.Length; i++)
                Draw(_bar[i], _inventory[i], i == _hotbar.Selected ? Hud.Accent : Color.clear);
            if (!IsOpen) return;
            for (int i = 0; i < _grid.Length; i++)
            {
                var frame = i == _picked ? Picked : i == _hotbar.Selected ? Hud.Accent : Color.clear;
                Draw(_grid[i], _inventory[i], frame);
            }
            DrawDetails();
        }

        private void Draw(SlotView view, ItemStack stack, Color frame)
        {
            if (view.Frame.color != frame) view.Frame.color = frame;
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
            _detail.text = def.Description + "\n\nToca otra casilla para moverlo.";
            _quickLabel.text = Inventory.IsHotbarSlot(_picked) ? "A LA MOCHILA" : "A LA BARRA";
        }

        private void QuickMove()
        {
            if (_picked < 0) return;
            if (!_inventory.QuickMove(_picked))
                _hud.ShowToast(Inventory.IsHotbarSlot(_picked) ? "La mochila está llena" : "La barra está llena");
            _picked = -1;
            Refresh();
        }

        private void Split()
        {
            if (_picked < 0) return;
            if (_inventory.Split(_picked) < 0) _hud.ShowToast("No hay casillas libres");
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
            UiFactory.AddImage(BagButton, UiFactory.Rounded, Hud.Panel);
            var label = UiFactory.NewRect("Label", BagButton, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SlotSize, 50f));
            UiFactory.AddText(label, "MOCHILA", 22, TextAnchor.MiddleCenter, Hud.Title);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = BagButton, OnTap = Toggle });
        }

        private void BuildPanel()
        {
            var half = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);
            const float left = 40f, top = -100f;
            float gridWidth = Columns * Pitch - Gap;
            float detailsX = left + gridWidth + 40f;
            const float detailsWidth = 372f;

            _panel = UiFactory.NewRect("Inventory panel", _hud.SafeArea, half, half, new Vector2(0f, 10f),
                new Vector2(detailsX + detailsWidth + 40f, 800f));
            UiFactory.AddImage(_panel, UiFactory.Rounded, Hud.Sheet);
            _hud.Blockers.Add(_panel);

            var title = UiFactory.NewRect("Title", _panel, topLeft, topLeft, new Vector2(left, -22f), new Vector2(500f, 60f));
            UiFactory.AddText(title, "INVENTARIO", 40, TextAnchor.MiddleLeft, Hud.Title);

            var close = UiFactory.NewRect("Close", _panel, topRight, topRight, new Vector2(-18f, -18f), new Vector2(84f, 72f));
            UiFactory.AddImage(close, UiFactory.Rounded, Hud.Soft);
            var closeLabel = UiFactory.NewRect("Label", close, half, half, Vector2.zero, new Vector2(80f, 60f));
            UiFactory.AddText(closeLabel, "X", 36, TextAnchor.MiddleCenter, Color.white);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = close, OnTap = Close });

            // The backpack: four rows above the hotbar row.
            int backpackRows = (_grid.Length - Inventory.HotbarSize) / Columns;
            for (int i = Inventory.HotbarSize; i < _grid.Length; i++)
            {
                int row = (i - Inventory.HotbarSize) / Columns, column = (i - Inventory.HotbarSize) % Columns;
                AddGridSlot(i, new Vector2(left + column * Pitch, top - row * Pitch));
            }
            float barTop = top - backpackRows * Pitch - 34f;
            var barLabel = UiFactory.NewRect("Hotbar label", _panel, topLeft, topLeft, new Vector2(left, barTop + 32f), new Vector2(gridWidth, 30f));
            UiFactory.AddText(barLabel, "BARRA RÁPIDA", 22, TextAnchor.MiddleLeft, Hud.Muted);
            for (int i = 0; i < Inventory.HotbarSize; i++)
                AddGridSlot(i, new Vector2(left + i * Pitch, barTop));

            // Right: what the picked item is, and what can be done with it.
            var name = UiFactory.NewRect("Name", _panel, topLeft, topLeft, new Vector2(detailsX, top), new Vector2(detailsWidth, 50f));
            _name = UiFactory.AddText(name, "", 32, TextAnchor.UpperLeft, Color.white);
            var detail = UiFactory.NewRect("Detail", _panel, topLeft, topLeft, new Vector2(detailsX, top - 60f), new Vector2(detailsWidth, 300f));
            _detail = UiFactory.AddText(detail, "", 24, TextAnchor.UpperLeft, Hud.Muted);
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
            var button = UiFactory.NewRect(name, _panel, topLeft, topLeft, position, new Vector2(width, 90f));
            UiFactory.AddImage(button, UiFactory.Rounded, Hud.Accent);
            var labelRect = UiFactory.NewRect("Label", button, half, half, Vector2.zero, new Vector2(width - 20f, 60f));
            label = UiFactory.AddText(labelRect, text, 30, TextAnchor.MiddleCenter, Color.white);
            return button;
        }

        /// <param name="number">The hotbar key (1 to 6) shown in the corner, or 0 for none.</param>
        private static SlotView MakeSlot(RectTransform parent, Vector2 position, Vector2 anchor, int number)
        {
            var half = new Vector2(0.5f, 0.5f);
            var view = new SlotView();
            view.Rect = UiFactory.NewRect("Slot", parent, anchor, anchor, position, new Vector2(SlotSize, SlotSize));

            // The frame is a slightly larger rounded square behind the slot, shown in a colour when chosen.
            var frame = UiFactory.NewRect("Frame", view.Rect, half, half, Vector2.zero, new Vector2(SlotSize + 10f, SlotSize + 10f));
            view.Frame = UiFactory.AddImage(frame, UiFactory.Rounded, Color.clear);
            var back = UiFactory.NewRect("Back", view.Rect, half, half, Vector2.zero, new Vector2(SlotSize, SlotSize));
            UiFactory.AddImage(back, UiFactory.Rounded, SlotBack);

            var icon = UiFactory.NewRect("Icon", view.Rect, half, half, new Vector2(0f, 4f), new Vector2(SlotSize - 14f, SlotSize - 14f));
            view.Icon = UiFactory.AddImage(icon, null, Color.white);
            view.Icon.preserveAspect = true;
            view.Icon.enabled = false;

            var label = UiFactory.NewRect("Name", view.Rect, half, half, Vector2.zero, new Vector2(SlotSize - 8f, SlotSize - 8f));
            view.Label = UiFactory.AddText(label, "", 20, TextAnchor.MiddleCenter, Color.white);
            view.Label.horizontalOverflow = HorizontalWrapMode.Wrap;

            var amount = UiFactory.NewRect("Amount", view.Rect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-8f, 4f), new Vector2(SlotSize - 12f, 34f));
            view.Amount = UiFactory.AddText(amount, "", 24, TextAnchor.LowerRight, Color.white);

            if (number > 0)
            {
                var key = UiFactory.NewRect("Key", view.Rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -4f), new Vector2(30f, 28f));
                UiFactory.AddText(key, number.ToString(), 20, TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.45f));
            }
            return view;
        }
    }
}
