using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.Building;
using Oxfall.Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The hotbar along the bottom of the screen (tap a slot to hold it, tap it again to empty the
    /// hands) and the inventory panel behind the MOCHILA button: all 30 slots, where tapping one
    /// item and then another slot moves it there. With a box or furnace open, its slots stand on
    /// the right of the panel (as in the PC game's loot screen) and items move between the two the
    /// same way, or all at once with the big button. Moves go through <see cref="GameActions"/>:
    /// the shared rules alone, the server online.
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
        private RectTransform _details;
        private int _picked = -1;

        // The open box or furnace.
        private const int MaxBoxSlots = 12;
        private const int BoxColumns = 3;
        private readonly Settlement _settlement;
        private readonly SlotView[] _boxSlots = new SlotView[MaxBoxSlots];
        private RectTransform _loot;
        private Text _lootTitle;
        private Text _lootStatus;
        private RectTransform _lightButton;
        private Image _lightFill;
        private Text _lightLabel;
        private RectTransform _lootQuick;
        private Text _lootQuickLabel;
        private int _box;
        private DeployableKind _boxKind;
        private Inventory _boxInventory;
        private bool _pickedInBox;

        public bool IsOpen => _panel.gameObject.activeSelf;

        /// <summary>The box or furnace shown beside the inventory (0: none).</summary>
        public int OpenBoxId => _box;
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

        public InventoryUi(Hud hud, Inventory inventory, Hotbar hotbar, GameActions actions, Settlement settlement)
        {
            _hud = hud;
            _inventory = inventory;
            _hotbar = hotbar;
            _actions = actions;
            _settlement = settlement;
            _settlement.DeployableChanged += id =>
            {
                if (id == _box) Refresh();
            };
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
            CloseBox(true);
            _panel.gameObject.SetActive(false);
            _barRoot.gameObject.SetActive(true);
            _picked = -1;
            Refresh();
        }

        /// <summary>Opens the inventory with this box or furnace beside it.</summary>
        public void OpenBox(int id)
        {
            var box = _settlement.Container(id);
            if (box == null) return;
            if (_box != id) CloseBox(true);
            if (!IsOpen) Toggle();
            _box = id;
            _boxInventory = box;
            _boxInventory.Changed += OnBoxChanged;
            _picked = -1;
            _settlement.TryGet(id, out var d);
            _boxKind = d.Kind;
            bool furnace = d.Kind == DeployableKind.Furnace, bag = d.Kind == DeployableKind.Bag, fire = d.Kind == DeployableKind.Campfire;
            _lootTitle.text = furnace ? "HORNO" : bag ? "BOLSA" : fire ? "FOGATA" : "CAJA";
            for (int i = 0; i < _boxSlots.Length; i++) _boxSlots[i].Rect.gameObject.SetActive(i < box.SlotCount);
            // A furnace's switch; on a dead player's bag the same button takes everything.
            _lightButton.gameObject.SetActive(furnace || bag);
            _lootStatus.gameObject.SetActive(furnace || bag || fire);
            _details.gameObject.SetActive(false);
            _loot.gameObject.SetActive(true);
            Refresh();
        }

        /// <summary>Puts the box away (the panel stays). <paramref name="tell"/>: also tell the rules (the player closed it, not the server).</summary>
        public void CloseBox(bool tell)
        {
            if (_box == 0) return;
            if (_boxInventory != null) _boxInventory.Changed -= OnBoxChanged;
            _box = 0;
            _boxInventory = null;
            if (_pickedInBox) _picked = -1;
            _pickedInBox = false;
            _loot.gameObject.SetActive(false);
            _details.gameObject.SetActive(true);
            if (tell) _actions.Close();
            Refresh();
        }

        private void OnBoxChanged(int slot) => Refresh();

        /// <summary>Taps a slot of the panel: picks an item, or moves the picked one there.</summary>
        public void TapSlot(int slot)
        {
            if (_picked < 0)
            {
                if (!_inventory[slot].IsEmpty)
                {
                    _picked = slot;
                    _pickedInBox = false;
                }
            }
            else
            {
                if (_pickedInBox) Report(_actions.Take(_picked, slot));
                else if (slot != _picked) _actions.MoveSlot(_picked, slot);
                _picked = -1;
            }
            Refresh();
        }

        /// <summary>Taps a slot of the open box: picks an item, or moves the picked one there.</summary>
        public void TapBoxSlot(int slot)
        {
            if (_boxInventory == null || slot >= _boxInventory.SlotCount) return;
            if (_picked < 0)
            {
                if (!_boxInventory[slot].IsEmpty)
                {
                    _picked = slot;
                    _pickedInBox = true;
                }
            }
            else
            {
                if (!_pickedInBox) Report(_actions.Put(_picked, slot));
                else if (slot != _picked) _actions.MoveInBox(_picked, slot);
                _picked = -1;
            }
            Refresh();
        }

        /// <summary>The big button with a box open: the picked stack all the way across.</summary>
        public void MoveAcross()
        {
            if (_picked < 0 || _box == 0) return;
            Report(_pickedInBox ? _actions.Take(_picked, -1) : _actions.Put(_picked, -1));
            _picked = -1;
            Refresh();
        }

        /// <summary>Lights the open furnace, or puts it out; on a bag, takes everything.</summary>
        public void ToggleFurnace()
        {
            if (_box == 0) return;
            if (_boxKind == DeployableKind.Bag)
            {
                Report(_actions.TakeAll());
                _picked = -1;
                if (_box != 0) Refresh();
                return;
            }
            bool on = !_settlement.IsLit(_box);
            var result = _actions.Build(BuildOp.Light(_box, on));
            if (result != ActionResult.Ok) _hud.ShowToast(GameActions.Explain(result));
            Refresh();
        }

        private void Report(ActionResult result)
        {
            if (result == ActionResult.NoSpace) _hud.ShowToast("No cabe: no hay sitio libre");
            else if (result != ActionResult.Ok && result != ActionResult.Invalid) _hud.ShowToast(GameActions.Explain(result));
        }

        /// <summary>Redraws every slot. Called when the inventory or the chosen hotbar slot changes.</summary>
        public void Refresh()
        {
            if (_pickedInBox && _boxInventory == null) _picked = -1;
            if (_picked >= 0 && (_pickedInBox ? _boxInventory[_picked] : _inventory[_picked]).IsEmpty) _picked = -1;
            for (int i = 0; i < _bar.Length; i++)
                Draw(_bar[i], _inventory[i], i == _hotbar.Selected ? Held : Color.clear);
            if (!IsOpen) return;
            for (int i = 0; i < _grid.Length; i++)
            {
                var frame = i == _picked && !_pickedInBox ? Picked : i == _hotbar.Selected ? Held : Color.clear;
                Draw(_grid[i], _inventory[i], frame);
            }
            if (_box != 0) DrawBox();
            else DrawDetails();
        }

        private void DrawBox()
        {
            for (int i = 0; i < _boxInventory.SlotCount && i < _boxSlots.Length; i++)
                Draw(_boxSlots[i], _boxInventory[i], i == _picked && _pickedInBox ? Picked : Color.clear);
            bool furnace = _boxKind == DeployableKind.Furnace;
            if (_boxKind == DeployableKind.Bag)
            {
                int stacks = 0;
                for (int i = 0; i < _boxInventory.SlotCount; i++)
                    if (!_boxInventory[i].IsEmpty) stacks++;
                _lightLabel.text = "TOMAR TODO";
                _lightFill.color = UiTheme.Accent;
                _lootStatus.text = stacks > _boxSlots.Length
                    ? $"Y {stacks - _boxSlots.Length} más debajo · la bolsa se pudre en unos minutos"
                    : "Lo que llevaba al morir · la bolsa se pudre en unos minutos";
                _lootStatus.color = UiTheme.TextMuted;
            }
            if (furnace)
            {
                bool lit = _settlement.IsLit(_box);
                _lightLabel.text = lit ? "APAGAR" : "ENCENDER";
                _lightFill.color = lit ? UiTheme.Panel : UiTheme.Accent;
                _lootStatus.text = lit
                    ? $"Ardiendo · madera {_boxInventory.Count(ItemIds.Wood)} · mineral {_boxInventory.Count(ItemIds.MetalOre)}"
                    : "Mete madera y mineral de metal, y enciéndelo";
                _lootStatus.color = lit ? UiTheme.Accent : UiTheme.TextMuted;
            }
            if (_boxKind == DeployableKind.Campfire)
            {
                int raw = _boxInventory.Count(ItemIds.RawMeat);
                _lootStatus.text = raw > 0 ? $"Asando · {raw} de carne cruda" : "Pon carne cruda al fuego: se asa sola";
                _lootStatus.color = raw > 0 ? UiTheme.Accent : UiTheme.TextMuted;
            }
            // Nothing goes into a bag: it only empties.
            bool any = _picked >= 0 && (_pickedInBox || _boxKind != DeployableKind.Bag);
            _lootQuick.gameObject.SetActive(any);
            if (any) _lootQuickLabel.text = _pickedInBox ? "AL INVENTARIO" : furnace ? "AL HORNO" : _boxKind == DeployableKind.Campfire ? "AL FUEGO" : "A LA CAJA";
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
            BuildLoot(pane, detailsX, top, detailsWidth, barTop);
            // The details live in their own layer, hidden while a box is open in their place.
            _details = UiFactory.NewRect("Item details", _panel, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            _details.anchorMin = Vector2.zero;
            _details.anchorMax = Vector2.one;
            _details.offsetMin = _details.offsetMax = Vector2.zero;
            var details = _details;
            _art = UiFactory.NewRect("Art", details, topLeft, topLeft, new Vector2(detailsX, top - 6f), new Vector2(150f, 150f));
            UiFactory.AddImage(_art, UiFactory.Soft, UiTheme.Slot);
            var glow = UiFactory.NewRect("Glow", _art, half, half, Vector2.zero, new Vector2(170f, 170f));
            UiFactory.AddImage(glow, Icons.Glow, UiTheme.Accent.WithAlpha(0.18f));
            _bigIcon = UiFactory.AddIcon(_art, null, Vector2.zero, 132f, Color.white);
            var name = UiFactory.NewRect("Name", details, topLeft, topLeft, new Vector2(detailsX, top - 170f), new Vector2(detailsWidth, 50f));
            _name = UiFactory.AddText(name, "", 36, TextAnchor.UpperLeft, UiTheme.Text, true);
            var detail = UiFactory.NewRect("Detail", details, topLeft, topLeft, new Vector2(detailsX, top - 224f), new Vector2(detailsWidth, 200f));
            _detail = UiFactory.AddText(detail, "", 24, TextAnchor.UpperLeft, UiTheme.TextMuted);
            _detail.horizontalOverflow = HorizontalWrapMode.Wrap;

            _quickButton = MakeButton("Quick move", details, new Vector2(detailsX, barTop + 100f), detailsWidth, "", out _quickLabel, out _);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = _quickButton, OnTap = QuickMove });
            _splitButton = MakeButton("Split", details, new Vector2(detailsX, barTop - 14f), detailsWidth, "DIVIDIR", out _, out _);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = _splitButton, OnTap = Split });

            _panel.gameObject.SetActive(false);
        }

        private void AddGridSlot(int slot, Vector2 position)
        {
            var topLeft = new Vector2(0f, 1f);
            _grid[slot] = MakeSlot(_panel, position, topLeft, Inventory.IsHotbarSlot(slot) ? slot + 1 : 0);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = _grid[slot].Rect, OnTap = () => TapSlot(slot) });
        }

        /// <summary>
        /// The open box's side of the panel, over the details pane: its name, its slots three to a
        /// row, for a furnace a line on how it burns and ENCENDER, and the button that sends the
        /// picked stack across.
        /// </summary>
        private void BuildLoot(RectTransform pane, float x, float top, float width, float barTop)
        {
            var topLeft = new Vector2(0f, 1f);
            _loot = UiFactory.NewRect("Loot", _panel, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            _loot.anchorMin = Vector2.zero;
            _loot.anchorMax = Vector2.one;
            _loot.offsetMin = _loot.offsetMax = Vector2.zero;
            var title = UiFactory.NewRect("Title", _loot, topLeft, topLeft, new Vector2(x, top + 2f), new Vector2(width, 44f));
            _lootTitle = UiFactory.AddText(title, "CAJA", 34, TextAnchor.MiddleLeft, UiTheme.Accent, true);
            float gridTop = top - 52f;
            for (int i = 0; i < MaxBoxSlots; i++)
            {
                int slot = i;
                int row = i / BoxColumns, column = i % BoxColumns;
                _boxSlots[i] = MakeSlot(_loot, new Vector2(x + column * Pitch, gridTop - row * Pitch), topLeft, 0);
                _hud.TapTargets.Add(new Hud.TapTarget { Rect = _boxSlots[i].Rect, OnTap = () => TapBoxSlot(slot) });
            }
            // A furnace's two rows leave room under them for how it burns and its switch.
            float furnaceTop = gridTop - 2 * Pitch - 6f;
            var status = UiFactory.NewRect("Status", _loot, topLeft, topLeft, new Vector2(x, furnaceTop), new Vector2(width, 70f));
            _lootStatus = UiFactory.AddText(status, "", 24, TextAnchor.UpperLeft, UiTheme.TextMuted);
            _lootStatus.horizontalOverflow = HorizontalWrapMode.Wrap;
            _lightButton = MakeButton("Light", _loot, new Vector2(x, furnaceTop - 78f), width, "ENCENDER", out _lightLabel, out _lightFill);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = _lightButton, OnTap = ToggleFurnace });
            _lootQuick = MakeButton("Across", _loot, new Vector2(x, barTop - 14f), width, "", out _lootQuickLabel, out _);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = _lootQuick, OnTap = MoveAcross });
            _loot.gameObject.SetActive(false);
        }

        private RectTransform MakeButton(string name, RectTransform parent, Vector2 position, float width, string text, out Text label,
            out Image fill)
        {
            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);
            var button = UiFactory.NewRect(name, parent, topLeft, topLeft, position, new Vector2(width, 84f));
            fill = UiFactory.AddImage(button, UiFactory.Soft, UiTheme.Accent);
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
