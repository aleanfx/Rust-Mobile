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
    /// hands) and the inventory panel behind the MOCHILA button: the 30 slots carried and, down the
    /// left, the four worn (head, chest, legs, feet) with how much they protect; tapping one item
    /// and then another slot moves it there. With a box or furnace open, its slots stand on
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

        // A recycler's slots that only give out, a shade of the accent.
        private static Color OutputTint => Color.Lerp(UiTheme.Slot, UiTheme.Accent, 0.18f);

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
        // Where the box's slots start, and how many rows of them a bag or crate shows (only as many
        // as it is filled to: nothing goes into one, so its line and TOMAR TODO sit right under them).
        private float _gridTop;
        private float _barTop;
        private int _lootRows;
        private readonly System.Collections.Generic.Dictionary<string, float> _upkeepCost = new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>The player taps PERMISOS on an open tool cupboard.</summary>
        public event System.Action<int> CupboardRequested;
        private float _lootX;
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
            /// <summary>A wear slot's outline of what goes there, shown while it is empty.</summary>
            public Image Placeholder;
            /// <summary>For a tool or weapon, how much of it is left: a bar up its left side.</summary>
            public RectTransform ConditionTrack;
            public Image Condition;
        }

        private Text _protection;

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
            bool furnace = d.Kind == DeployableKind.Furnace, bag = Deployables.IsLoot(d.Kind), fire = d.Kind == DeployableKind.Campfire;
            bool recycler = d.Kind == DeployableKind.Recycler;
            bool cupboard = d.Kind == DeployableKind.ToolCupboard;
            _lootTitle.text = furnace ? "HORNO" : d.Kind == DeployableKind.SupplyDrop ? "SUMINISTROS" : bag ? "BOLSA" : fire ? "FOGATA"
                : recycler ? "RECICLADORA" : cupboard ? "ARMARIO" : "CAJA";
            _lootRows = 0;
            for (int i = 0; i < _boxSlots.Length; i++) _boxSlots[i].Rect.gameObject.SetActive(i < box.SlotCount);
            // A furnace's two rows leave room under them for how it burns and its switch (a bag or a
            // crate sets its own when it is drawn).
            float under = _gridTop - 2 * Pitch - 6f;
            _lootStatus.rectTransform.anchoredPosition = new Vector2(_lootX, under);
            _lightButton.anchoredPosition = new Vector2(_lootX, under - 78f);
            // A furnace's or a recycler's switch; on a dead player's bag the same button takes everything.
            // A cupboard's goes back to who may build round it.
            _lightButton.gameObject.SetActive(furnace || bag || recycler || cupboard);
            _lootStatus.gameObject.SetActive(furnace || bag || fire || recycler || cupboard);
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
                var moving = _pickedInBox ? _boxInventory[_picked] : _inventory[_picked];
                if (!_inventory.CanHold(slot, moving)) _hud.ShowToast("Ahí va ropa para " + PartName(_inventory.WearOf(slot)));
                else if (!_pickedInBox && !_inventory.CanHold(_picked, _inventory[slot]))
                    _hud.ShowToast("Ahí va ropa para " + PartName(_inventory.WearOf(_picked)));
                else if (_pickedInBox) Report(_actions.Take(_picked, slot));
                else if (slot != _picked) _actions.MoveSlot(_picked, slot);
                _picked = -1;
            }
            Refresh();
        }

        private static string PartName(WearSlot wear) =>
            wear == WearSlot.Head ? "la cabeza" : wear == WearSlot.Chest ? "el pecho" : wear == WearSlot.Legs ? "las piernas" : "los pies";

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
            if (_boxKind == DeployableKind.ToolCupboard)
            {
                CupboardRequested?.Invoke(_box);
                return;
            }
            if (Deployables.IsLoot(_boxKind))
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
            int armour = Mathf.RoundToInt(_inventory.Protection * 100f);
            _protection.text = armour + "%";
            _protection.color = armour > 0 ? UiTheme.Text : UiTheme.TextFaint;
            if (_box != 0) DrawBox();
            else DrawDetails();
        }

        private void DrawBox()
        {
            for (int i = 0; i < _boxInventory.SlotCount && i < _boxSlots.Length; i++)
                Draw(_boxSlots[i], _boxInventory[i], i == _picked && _pickedInBox ? Picked : Color.clear);
            bool furnace = _boxKind == DeployableKind.Furnace;
            if (Deployables.IsLoot(_boxKind))
            {
                int stacks = 0, last = -1;
                for (int i = 0; i < _boxInventory.SlotCount; i++)
                    if (!_boxInventory[i].IsEmpty)
                    {
                        stacks++;
                        last = i;
                    }
                LayoutLoot(Mathf.Clamp(last / BoxColumns + 1, 1, MaxBoxSlots / BoxColumns));
                _lightLabel.text = "TOMAR TODO";
                _lightFill.color = UiTheme.Accent;
                bool crate = _boxKind == DeployableKind.SupplyDrop;
                _lootStatus.text = stacks > _boxSlots.Length
                    ? $"Y {stacks - _boxSlots.Length} más debajo · {(crate ? "la caja desaparece en unos minutos" : "la bolsa se pudre en unos minutos")}"
                    : crate ? "Lanzado desde el avión · para el primero que llegue"
                    : "Lo que llevaba al morir · la bolsa se pudre en unos minutos";
                _lootStatus.color = UiTheme.TextMuted;
            }
            if (furnace)
            {
                bool lit = _settlement.IsLit(_box);
                _lightLabel.text = lit ? "APAGAR" : "ENCENDER";
                _lightFill.color = lit ? UiTheme.Panel : UiTheme.Accent;
                _lootStatus.text = lit
                    ? $"Ardiendo · madera {_boxInventory.Count(ItemIds.Wood)} · mineral {_boxInventory.Count(ItemIds.MetalOre) + _boxInventory.Count(ItemIds.SulfurOre)}"
                    : "Mete madera y mineral (metal o azufre), y enciéndelo";
                _lootStatus.color = lit ? UiTheme.Accent : UiTheme.TextMuted;
            }
            if (_boxKind == DeployableKind.Recycler) DrawRecycler();
            if (_boxKind == DeployableKind.ToolCupboard)
            {
                _lightLabel.text = "PERMISOS";
                _lightFill.color = UiTheme.Slot;
                CupboardUi.Describe(_settlement, _box, _upkeepCost, out string head, out string perDay, out _, out var colour, out _);
                _lootStatus.text = perDay.Length == 0 ? "Madera, piedra o metal: pagan lo que construyas aquí" : head + "\nAl día: " + perDay;
                _lootStatus.color = colour;
            }
            if (_boxKind == DeployableKind.Campfire)
            {
                int raw = _boxInventory.Count(ItemIds.RawMeat);
                _lootStatus.text = raw > 0 ? $"Asando · {raw} de carne cruda" : "Pon carne cruda al fuego: se asa sola";
                _lootStatus.color = raw > 0 ? UiTheme.Accent : UiTheme.TextMuted;
            }
            // Nothing goes into a bag: it only empties.
            bool any = _picked >= 0 && (_pickedInBox || !Deployables.IsLoot(_boxKind));
            _lootQuick.gameObject.SetActive(any);
            // A full crate's TOMAR TODO shares the bottom spot with the button that moves one stack.
            if (Deployables.IsLoot(_boxKind) && _lootRows > 3) _lightButton.gameObject.SetActive(!any);
            if (any)
                _lootQuickLabel.text = _pickedInBox ? "AL INVENTARIO" : furnace ? "AL HORNO" : _boxKind == DeployableKind.Campfire ? "AL FUEGO"
                    : _boxKind == DeployableKind.Recycler ? "A RECICLAR" : _boxKind == DeployableKind.ToolCupboard ? "AL ARMARIO" : "A LA CAJA";
        }

        /// <summary>
        /// Shows a bag's or crate's slots down to its last filled row (the rows never shrink while it
        /// is open, so nothing jumps under a finger), its line and TOMAR TODO right under them; with
        /// three rows there is room only for the button, with four it takes the bottom spot.
        /// </summary>
        private void LayoutLoot(int rows)
        {
            if (rows <= _lootRows) return;
            _lootRows = rows;
            for (int i = 0; i < _boxSlots.Length; i++)
                _boxSlots[i].Rect.gameObject.SetActive(i < _boxInventory.SlotCount && i < rows * BoxColumns);
            float under = _gridTop - rows * Pitch - 6f;
            _lootStatus.gameObject.SetActive(rows <= 2);
            _lootStatus.rectTransform.anchoredPosition = new Vector2(_lootX, under);
            _lightButton.anchoredPosition = new Vector2(_lootX, rows <= 2 ? under - 78f : rows == 3 ? under : _barTop - 14f);
        }

        /// <summary>
        /// A recycler: its switch; what the picked item would come out as (or what is left to do,
        /// or how it works); and its bottom row, which only gives out, shaded.
        /// </summary>
        private void DrawRecycler()
        {
            bool running = _settlement.IsLit(_box);
            _lightLabel.text = running ? "APAGAR" : "ENCENDER";
            _lightFill.color = running ? UiTheme.Panel : UiTheme.Accent;
            var recycling = _settlement.Recycling;
            int fillEnd = _boxInventory.SlotCount - _boxInventory.OutputSlots;
            string picked = _picked < 0 ? null : _pickedInBox ? _boxInventory[_picked].ItemId : _inventory[_picked].ItemId;
            if (picked != null && !(_pickedInBox && _picked >= fillEnd))
            {
                string name = _inventory.Catalog.Get(picked).DisplayName;
                bool takes = recycling.Takes(picked);
                _lootStatus.text = takes ? name + " da " + Describe(recycling.Yield(picked)) : name + " no se recicla";
                _lootStatus.color = takes ? UiTheme.Text : UiTheme.TextMuted;
            }
            else
            {
                int waiting = 0;
                for (int i = 0; i < fillEnd; i++)
                    if (!_boxInventory[i].IsEmpty && recycling.Takes(_boxInventory[i].ItemId)) waiting += _boxInventory[i].Amount;
                _lootStatus.text = running ? $"Reciclando · quedan {waiting}" : "Arriba lo que reciclas · abajo sale la chatarra";
                _lootStatus.color = running ? UiTheme.Accent : UiTheme.TextMuted;
            }
            for (int i = fillEnd; i < _boxInventory.SlotCount && i < _boxSlots.Length; i++)
                if (!(_pickedInBox && i == _picked)) _boxSlots[i].Back.color = OutputTint;
        }

        /// <summary>"10 chatarra y 13 fragmentos de metal".</summary>
        private string Describe(System.Collections.Generic.IReadOnlyList<Oxfall.Core.Crafting.Ingredient> yields)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < yields.Count; i++)
            {
                if (i > 0) text.Append(i == yields.Count - 1 ? " y " : ", ");
                text.Append(yields[i].Amount).Append(' ').Append(_inventory.Catalog.Get(yields[i].ItemId).DisplayName.ToLowerInvariant());
            }
            return text.ToString();
        }

        private void Draw(SlotView view, ItemStack stack, Color frame)
        {
            if (view.Frame.color != frame) view.Frame.color = frame;
            if (view.Placeholder != null && view.Placeholder.enabled != stack.IsEmpty) view.Placeholder.enabled = stack.IsEmpty;
            var back = frame == Color.clear ? UiTheme.Slot : UiTheme.SlotHover;
            if (view.Back.color != back) view.Back.color = back;
            bool wears = !stack.IsEmpty && Durability.Wears(stack.ItemId);
            if (view.ConditionTrack.gameObject.activeSelf != wears) view.ConditionTrack.gameObject.SetActive(wears);
            if (wears)
            {
                float left = Durability.Left(stack);
                view.Condition.rectTransform.anchorMax = new Vector2(1f, left);
                var tint = ConditionColour(left);
                if (view.Condition.color != tint) view.Condition.color = tint;
            }
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
            bool attire = def.Category == ItemCategory.Attire;
            bool worn = _inventory.IsWearSlot(_picked);
            _detail.text = def.Description
                           + (attire ? $"\n\nProtege {Mathf.RoundToInt(def.Protection * 100f)} % de cada golpe, flecha o mordisco." : "")
                           + (Durability.Wears(stack.ItemId)
                               ? $"\n\nEstado: {Mathf.CeilToInt(Durability.Left(stack) * 100f)} %. Se gasta con el uso y al final se rompe."
                               : "")
                           + "\n\nToca otra casilla para moverlo.";
            _quickLabel.text = worn ? "QUITAR" : attire ? "PONER" : Inventory.IsHotbarSlot(_picked) ? "A LA MOCHILA" : "A LA BARRA";
        }

        private void QuickMove()
        {
            if (_picked < 0) return;
            // Clothing goes on (or comes off) rather than across.
            if (_inventory.IsWearSlot(_picked) || (_inventory.Catalog.TryGet(_inventory[_picked].ItemId, out var def) && def.Category == ItemCategory.Attire))
            {
                if (_actions.Wear(_picked) == ActionResult.NoSpace) _hud.ShowToast("La mochila está llena");
                _picked = -1;
                Refresh();
                return;
            }
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
            // The worn clothes down the left, then the backpack and hotbar, then the details.
            float gridLeft = left + Pitch + 26f;
            float gridWidth = Columns * Pitch - Gap;
            float detailsX = gridLeft + gridWidth + 40f;
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
            int backpackRows = (_inventory.CarrySlots - Inventory.HotbarSize) / Columns;
            for (int i = Inventory.HotbarSize; i < _inventory.CarrySlots; i++)
            {
                int row = (i - Inventory.HotbarSize) / Columns, column = (i - Inventory.HotbarSize) % Columns;
                AddGridSlot(i, new Vector2(gridLeft + column * Pitch, top - row * Pitch));
            }
            float barTop = top - backpackRows * Pitch - 34f;
            var barLabel = UiFactory.NewRect("Hotbar label", _panel, topLeft, topLeft, new Vector2(gridLeft, barTop + 32f), new Vector2(gridWidth, 30f));
            UiFactory.AddText(barLabel, "BARRA RÁPIDA", 22, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            for (int i = 0; i < Inventory.HotbarSize; i++)
                AddGridSlot(i, new Vector2(gridLeft + i * Pitch, barTop));

            // What is worn, head to feet beside the backpack's rows, each empty one showing what goes
            // there; under them, beside the hotbar, how much of a blow it all takes.
            var wearLabel = UiFactory.NewRect("Wear label", _panel, topLeft, topLeft, new Vector2(left, top + 32f), new Vector2(SlotSize, 30f));
            UiFactory.AddText(wearLabel, "ROPA", 22, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            var silhouettes = new[] { Icons.Hood, Icons.Shirt, Icons.Trousers, Icons.Boot };
            for (int w = 0; w < Inventory.WearSlots; w++)
            {
                int slot = _inventory.SlotFor((WearSlot)(w + 1));
                AddGridSlot(slot, new Vector2(left, top - w * Pitch));
                _grid[slot].Placeholder = UiFactory.AddIcon(_grid[slot].Rect, silhouettes[w], Vector2.zero, 64f, UiTheme.TextFaint.WithAlpha(0.35f));
                _grid[slot].Placeholder.transform.SetSiblingIndex(1);
            }
            var armour = UiFactory.NewRect("Protection", _panel, topLeft, topLeft, new Vector2(left, barTop), new Vector2(SlotSize, SlotSize));
            UiFactory.AddImage(armour, UiFactory.Soft, UiTheme.Slot.WithAlpha(0.5f));
            UiFactory.AddIcon(armour, Icons.Shield, new Vector2(0f, 22f), 46f, UiTheme.TextMuted);
            var protection = UiFactory.NewRect("Value", armour, half, half, new Vector2(0f, -26f), new Vector2(SlotSize, 40f));
            _protection = UiFactory.AddText(protection, "0%", 32, TextAnchor.MiddleCenter, UiTheme.Text, true);

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
            _gridTop = gridTop;
            _lootX = x;
            _barTop = barTop;
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

            // How much of a tool or weapon is left, up its left side (hidden for anything that never wears).
            var leftMiddle = new Vector2(0f, 0.5f);
            view.ConditionTrack = UiFactory.NewRect("Condition", view.Rect, leftMiddle, leftMiddle, new Vector2(7f, 0f), new Vector2(7f, SlotSize - 26f));
            UiFactory.AddImage(view.ConditionTrack, null, new Color(0f, 0f, 0f, 0.4f));
            var fill = UiFactory.NewRect("Fill", view.ConditionTrack, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            view.Condition = UiFactory.AddImage(fill, null, UiTheme.Good);
            view.ConditionTrack.gameObject.SetActive(false);

            // The frame: an outline over the slot, in a colour when chosen.
            var frame = UiFactory.NewRect("Frame", view.Rect, half, half, Vector2.zero, new Vector2(SlotSize, SlotSize));
            view.Frame = UiFactory.AddImage(frame, UiFactory.Outline, Color.clear);
            return view;
        }

        /// <summary>A tool's condition bar: green while it is good, orange as it wears, red near the end.</summary>
        private static Color ConditionColour(float left) =>
            left > 0.5f ? UiTheme.Good : left > 0.2f ? UiTheme.Accent : UiTheme.Bad;
    }
}
