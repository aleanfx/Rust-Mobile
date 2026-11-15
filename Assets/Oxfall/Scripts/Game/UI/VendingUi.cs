using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The vending machine's shop window: its four offers, each "so many of this for so many of
    /// that", how many times it can still be bought, and COMPRAR. Its owner sees the same rows made
    /// editable: tap the goods to step through what is stocked inside, tap the price to step through
    /// the usual currencies, − and + for the amounts, BORRAR to take an offer down, and LLENAR to
    /// open its slots beside the inventory to stock it and collect the takings.
    /// </summary>
    public sealed class VendingUi
    {
        private const float RowTop = -150f;
        private const float RowPitch = 98f;
        private const float RowHeight = 86f;

        /// <summary>The amounts − and + step through.</summary>
        private static readonly int[] Steps = { 1, 2, 3, 5, 10, 15, 20, 25, 50, 75, 100, 150, 200, 250, 300, 500, 750, 1000 };

        private readonly Hud _hud;
        private readonly Settlement _settlement;
        private readonly GameActions _actions;
        private readonly RectTransform _panel;
        private readonly Text _state;
        private readonly Text _note;
        private readonly RectTransform _stock;
        private readonly Row[] _rows = new Row[Vending.Orders];
        private Inventory _box;
        private int _id;
        private bool _mine;

        public bool IsOpen => _panel.gameObject.activeSelf;
        public int OpenId => _id;

        /// <summary>The owner taps LLENAR: the machine's slots, beside the inventory.</summary>
        public event Action<int> StockRequested;

        /// <summary>Something was bought (the goods' id), for a sound.</summary>
        public event Action<string> Bought;

        private sealed class Row
        {
            public RectTransform Rect;
            public Image SellIcon, PriceIcon;
            public Text SellText, PriceText, Stock, ButtonLabel, Empty;
            public Image ButtonFill;
            public RectTransform SellDown, SellUp, PriceDown, PriceUp, Button, Swap, SellBox, PriceBox;
        }

        public VendingUi(Hud hud, Settlement settlement, GameActions actions)
        {
            _hud = hud;
            _settlement = settlement;
            _actions = actions;
            var half = new Vector2(0.5f, 0.5f);
            var topLeft = new Vector2(0f, 1f);
            _panel = UiFactory.NewRect("Vending", hud.SafeArea, half, half, new Vector2(0f, 10f), new Vector2(1180f, 640f));
            UiFactory.AddImage(_panel, UiFactory.Rounded, UiTheme.PanelSolid);
            hud.Blockers.Add(_panel);
            hud.CloseButton(_panel, Close);

            var badge = UiFactory.NewRect("Badge", _panel, topLeft, topLeft, new Vector2(40f, -30f), new Vector2(96f, 96f));
            UiFactory.AddImage(badge, UiFactory.Soft, UiTheme.Slot);
            UiFactory.AddIcon(badge, Icons.Shop, Vector2.zero, 64f, UiTheme.Accent);
            var title = UiFactory.NewRect("Title", _panel, topLeft, topLeft, new Vector2(160f, -30f), new Vector2(700f, 54f));
            UiFactory.AddText(title, "MÁQUINA EXPENDEDORA", 40, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var state = UiFactory.NewRect("State", _panel, topLeft, topLeft, new Vector2(160f, -84f), new Vector2(860f, 40f));
            _state = UiFactory.AddText(state, "", 26, TextAnchor.MiddleLeft, UiTheme.Accent, true);

            for (int i = 0; i < _rows.Length; i++) _rows[i] = MakeRow(i);

            var note = UiFactory.NewRect("Note", _panel, topLeft, topLeft, new Vector2(40f, -556f), new Vector2(700f, 60f));
            _note = UiFactory.AddText(note, "", 23, TextAnchor.MiddleLeft, UiTheme.TextMuted);
            _note.horizontalOverflow = HorizontalWrapMode.Wrap;
            _stock = Button(_panel, "LLENAR", new Vector2(830f, -546f), new Vector2(310f, 80f), UiTheme.Slot, UiTheme.Text, out _, out _, () =>
            {
                if (_id != 0 && _mine) StockRequested?.Invoke(_id);
            });

            _panel.gameObject.SetActive(false);
            settlement.DeployableChanged += id =>
            {
                if (id == _id && IsOpen) Refresh();
            };
            settlement.DeployableRemoved += id =>
            {
                if (id == _id) Close();
            };
        }

        private Row MakeRow(int index)
        {
            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);
            var row = new Row();
            row.Rect = UiFactory.NewRect("Offer " + index, _panel, topLeft, topLeft, new Vector2(30f, RowTop - index * RowPitch), new Vector2(1120f, RowHeight));
            UiFactory.AddImage(row.Rect, UiFactory.Soft, new Color(1f, 1f, 1f, 0.04f));

            row.SellBox = UiFactory.NewRect("Goods", row.Rect, topLeft, topLeft, new Vector2(8f, -4f), new Vector2(78f, 78f));
            UiFactory.AddImage(row.SellBox, UiFactory.Soft, UiTheme.Slot);
            row.SellIcon = UiFactory.AddImage(UiFactory.NewRect("Icon", row.SellBox, half, half, Vector2.zero, new Vector2(66f, 66f)), null, Color.white);
            row.SellIcon.preserveAspect = true;
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = row.SellBox, OnTap = () => CycleSell(index) });
            row.SellDown = Small(row.Rect, "−", new Vector2(94f, -12f), () => StepSell(index, -1));
            var sell = UiFactory.NewRect("Goods text", row.Rect, topLeft, topLeft, new Vector2(158f, -8f), new Vector2(210f, 70f));
            row.SellText = UiFactory.AddText(sell, "", 26, TextAnchor.MiddleLeft, UiTheme.Text, true);
            row.SellText.horizontalOverflow = HorizontalWrapMode.Wrap;
            row.SellUp = Small(row.Rect, "+", new Vector2(372f, -12f), () => StepSell(index, 1));

            row.Swap = UiFactory.NewRect("Swap", row.Rect, topLeft, topLeft, new Vector2(438f, -18f), new Vector2(50f, 50f));
            UiFactory.AddIcon(row.Swap, Icons.Swap, Vector2.zero, 44f, UiTheme.TextMuted);

            row.PriceBox = UiFactory.NewRect("Price", row.Rect, topLeft, topLeft, new Vector2(496f, -4f), new Vector2(78f, 78f));
            UiFactory.AddImage(row.PriceBox, UiFactory.Soft, UiTheme.Slot);
            row.PriceIcon = UiFactory.AddImage(UiFactory.NewRect("Icon", row.PriceBox, half, half, Vector2.zero, new Vector2(66f, 66f)), null, Color.white);
            row.PriceIcon.preserveAspect = true;
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = row.PriceBox, OnTap = () => CyclePrice(index) });
            row.PriceDown = Small(row.Rect, "−", new Vector2(582f, -12f), () => StepPrice(index, -1));
            var price = UiFactory.NewRect("Price text", row.Rect, topLeft, topLeft, new Vector2(646f, -8f), new Vector2(190f, 70f));
            row.PriceText = UiFactory.AddText(price, "", 26, TextAnchor.MiddleLeft, UiTheme.Text, true);
            row.PriceText.horizontalOverflow = HorizontalWrapMode.Wrap;
            row.PriceUp = Small(row.Rect, "+", new Vector2(840f, -12f), () => StepPrice(index, 1));

            var stock = UiFactory.NewRect("Stock", row.Rect, topLeft, topLeft, new Vector2(840f, -8f), new Vector2(110f, 70f));
            row.Stock = UiFactory.AddText(stock, "", 22, TextAnchor.MiddleCenter, UiTheme.TextMuted, true);
            row.Stock.horizontalOverflow = HorizontalWrapMode.Wrap;

            var empty = UiFactory.NewRect("Empty", row.Rect, topLeft, topLeft, new Vector2(24f, -8f), new Vector2(800f, 70f));
            row.Empty = UiFactory.AddText(empty, "Oferta libre", 26, TextAnchor.MiddleLeft, UiTheme.TextFaint, true);

            row.Button = Button(row.Rect, "COMPRAR", new Vector2(958f, -6f), new Vector2(154f, 74f), UiTheme.Accent, Color.white,
                out row.ButtonLabel, out row.ButtonFill, () => Act(index));
            return row;
        }

        private RectTransform Small(RectTransform parent, string text, Vector2 position, Action onTap) =>
            Button(parent, text, position, new Vector2(60f, 62f), UiTheme.Slot, UiTheme.Text, out _, out _, onTap);

        private RectTransform Button(RectTransform parent, string text, Vector2 position, Vector2 size, Color fill, Color textColour,
            out Text label, out Image back, Action onTap)
        {
            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);
            var button = UiFactory.NewRect(text, parent, topLeft, topLeft, position, size);
            back = UiFactory.AddImage(button, UiFactory.Soft, fill);
            var labelRect = UiFactory.NewRect("Label", button, half, half, Vector2.zero, new Vector2(size.x - 12f, size.y - 10f));
            label = UiFactory.AddText(labelRect, text, size.x < 100f ? 40 : 28, TextAnchor.MiddleCenter, textColour, true);
            _hud.TapTargets.Add(new Hud.TapTarget { Rect = button, OnTap = onTap });
            return button;
        }

        public void Open(int id)
        {
            if (!_settlement.TryGet(id, out var d) || d.Kind != DeployableKind.VendingMachine) return;
            Close();
            _id = id;
            _mine = d.Owner == _actions.Owner;
            _hud.CloseCraftPanel();
            _panel.gameObject.SetActive(true);
            // Opened like a box, so its stock comes from the server.
            if (_actions.Open(id) == ActionResult.Ok)
            {
                _box = _settlement.Container(id);
                if (_box != null) _box.Changed += OnBoxChanged;
            }
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen && _id == 0) return;
            _panel.gameObject.SetActive(false);
            if (_box != null)
            {
                _box.Changed -= OnBoxChanged;
                _box = null;
                _actions.Close();
            }
            _id = 0;
        }

        private void OnBoxChanged(int slot)
        {
            if (IsOpen) Refresh();
        }

        // ---- Buying, and the owner's changes

        /// <summary>The row's button: COMPRAR for a set offer; for the owner, NUEVA on a free one.</summary>
        public void Act(int index)
        {
            if (_id == 0) return;
            var order = Order(index);
            if (order.IsEmpty)
            {
                if (_mine) Set(index, Fresh());
                return;
            }
            if (_mine) Clear(index);
            else Buy(index);
        }

        public void Buy(int index)
        {
            var order = Order(index);
            if (order.IsEmpty) return;
            var result = Vending.CanBuy(_box, _actions.Inventory, order);
            if (result == ActionResult.Ok) result = _actions.Build(BuildOp.Vend(_id, index));
            if (result == ActionResult.Ok)
            {
                _hud.ShowToast($"Comprado: {order.SellAmount} {Name(order.Sell)} por {order.PriceAmount} {Name(order.Price)}");
                Bought?.Invoke(order.Sell);
                Debug.Log($"[Oxfall] Bought {order.SellAmount} {order.Sell} for {order.PriceAmount} {order.Price} at {_id}");
            }
            else _hud.ShowToast(GameActions.Explain(result));
            Refresh();
        }

        /// <summary>The owner clears an offer.</summary>
        public void Clear(int index)
        {
            if (_mine && !Order(index).IsEmpty) Set(index, VendOrder.None);
        }

        private void CycleSell(int index)
        {
            var o = Order(index);
            if (!_mine || o.IsEmpty) return;
            var goods = Stocked();
            if (goods.Count == 0)
            {
                _hud.ShowToast("Llena la máquina primero: toca LLENAR");
                return;
            }
            int at = goods.IndexOf(o.Sell);
            string next = goods[(at + 1) % goods.Count];
            string price = next == o.Price ? NextCurrency(o.Price, next) : o.Price;
            Set(index, new VendOrder(next, o.SellAmount, price, o.PriceAmount));
        }

        private void CyclePrice(int index)
        {
            var o = Order(index);
            if (!_mine || o.IsEmpty) return;
            Set(index, new VendOrder(o.Sell, o.SellAmount, NextCurrency(o.Price, o.Sell), o.PriceAmount));
        }

        private void StepSell(int index, int way)
        {
            var o = Order(index);
            if (!_mine || o.IsEmpty) return;
            Set(index, new VendOrder(o.Sell, Step(o.SellAmount, way), o.Price, o.PriceAmount));
        }

        private void StepPrice(int index, int way)
        {
            var o = Order(index);
            if (!_mine || o.IsEmpty) return;
            Set(index, new VendOrder(o.Sell, o.SellAmount, o.Price, Step(o.PriceAmount, way)));
        }

        /// <summary>The owner sets an offer outright (the debug commands and the web tests use this too).</summary>
        public void Set(int index, VendOrder order)
        {
            if (_id == 0) return;
            var result = _actions.Build(BuildOp.SetOrder(_id, index, order));
            if (result != ActionResult.Ok) _hud.ShowToast(GameActions.Explain(result));
            else Debug.Log($"[Oxfall] Offer {_id}#{index} {order}");
            Refresh();
        }

        /// <summary>A new offer: the first thing stocked (or wood), one at a time, for scrap.</summary>
        private VendOrder Fresh()
        {
            var goods = Stocked();
            string sell = goods.Count > 0 ? goods[0] : ItemIds.Wood;
            string price = sell == ItemIds.Scrap ? ItemIds.MetalFragments : ItemIds.Scrap;
            int amount = 1;
            if (_box != null && _settlement.Catalog.TryGet(sell, out var def) && def.MaxStack > 1)
                amount = Mathf.Clamp(Mathf.Min(_box.Count(sell), 100), 1, Vending.MaxAmount);
            return new VendOrder(sell, Step(amount, 0), price, 10);
        }

        private static int Step(int amount, int way)
        {
            int at = 0;
            while (at < Steps.Length - 1 && Steps[at] < amount) at++;
            if (way == 0) return Steps[at];
            if (way < 0 && Steps[at] > amount) way = 0;
            return Steps[Mathf.Clamp(at + way, 0, Steps.Length - 1)];
        }

        private static string NextCurrency(string current, string not)
        {
            var list = Vending.Currencies;
            int at = Array.IndexOf(list, current);
            for (int i = 1; i <= list.Length; i++)
            {
                string next = list[(at + i + list.Length) % list.Length];
                if (next != not) return next;
            }
            return current;
        }

        /// <summary>The different things inside the machine, in slot order.</summary>
        private List<string> Stocked()
        {
            var goods = new List<string>();
            if (_box == null) return goods;
            for (int i = 0; i < _box.SlotCount; i++)
                if (!_box[i].IsEmpty && !goods.Contains(_box[i].ItemId)) goods.Add(_box[i].ItemId);
            return goods;
        }

        private VendOrder Order(int index)
        {
            var orders = _settlement.Orders(_id);
            return orders == null || index < 0 || index >= orders.Count ? VendOrder.None : orders[index];
        }

        private string Name(string id) => id != null && _settlement.Catalog.TryGet(id, out var def) ? def.DisplayName : id;

        // ---- Drawing

        private void Refresh()
        {
            if (_id == 0) return;
            int set = 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                var o = Order(i);
                if (!o.IsEmpty) set++;
                DrawRow(_rows[i], o);
            }
            _state.text = _mine ? "TU TIENDA · toca un objeto para cambiarlo" : set > 0 ? "TOCA COMPRAR: pagas y te llevas lo de la izquierda" : "SIN OFERTAS";
            _state.color = _mine ? UiTheme.Good : UiTheme.Accent;
            _stock.gameObject.SetActive(_mine);
            _note.text = _mine
                ? "Lo que vendes sale de su interior y lo que cobras entra en él. Recógelo con LLENAR."
                : set == 0 ? "Su dueño aún no ha puesto nada a la venta." : "Cualquiera puede comprar, aunque su dueño no esté.";
        }

        private void DrawRow(Row row, VendOrder o)
        {
            bool empty = o.IsEmpty;
            // A buyer never sees the free rows; the owner sees them, to set one up.
            row.Rect.gameObject.SetActive(!empty || _mine);
            row.Empty.gameObject.SetActive(empty);
            row.SellBox.gameObject.SetActive(!empty);
            row.PriceBox.gameObject.SetActive(!empty);
            row.Swap.gameObject.SetActive(!empty);
            row.SellText.gameObject.SetActive(!empty);
            row.PriceText.gameObject.SetActive(!empty);
            bool edit = _mine && !empty;
            row.SellDown.gameObject.SetActive(edit);
            row.SellUp.gameObject.SetActive(edit);
            row.PriceDown.gameObject.SetActive(edit);
            row.PriceUp.gameObject.SetActive(edit);
            // The owner's − and + over the price sit where a buyer reads the stock.
            row.Stock.gameObject.SetActive(!empty && !_mine);
            if (empty)
            {
                row.Empty.text = "Oferta libre";
                row.ButtonLabel.text = "NUEVA";
                row.ButtonFill.color = UiTheme.Slot;
                return;
            }
            Icon(row.SellIcon, o.Sell);
            Icon(row.PriceIcon, o.Price);
            row.SellText.text = $"{o.SellAmount} {Name(o.Sell)}";
            row.PriceText.text = $"{o.PriceAmount} {Name(o.Price)}";
            int left = Vending.Stock(_box, o);
            row.Stock.text = left > 0 ? $"QUEDAN\n{left}" : "AGOTADO";
            row.Stock.color = left > 0 ? UiTheme.TextMuted : UiTheme.Bad;
            if (_mine)
            {
                // The owner reads how many are left under the goods, and takes an offer down here.
                row.SellText.text += left > 0 ? $"\n<size=20>quedan {left}</size>" : "\n<size=20><color=#E0604E>agotado</color></size>";
                row.ButtonLabel.text = "BORRAR";
                row.ButtonFill.color = UiTheme.Slot;
                return;
            }
            bool can = left > 0 && _actions.Inventory.Has(o.Price, o.PriceAmount);
            row.ButtonLabel.text = "COMPRAR";
            row.ButtonFill.color = can ? UiTheme.Accent : UiTheme.Slot;
        }

        private static void Icon(Image image, string itemId)
        {
            var icon = ItemIcons.Get(itemId);
            image.enabled = icon != null;
            if (icon != null && image.sprite != icon) image.sprite = icon;
        }
    }
}
