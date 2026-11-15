using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Game.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The skin store: every skin as a card (its icon in the skin, a rarity stripe, the price or
    /// whether it is worn), the player's Oxcoins at the top, and below the chosen card its name and
    /// one button: COMPRAR, PONER or QUITAR. Skins only change looks, and the panel says so.
    /// </summary>
    public sealed class StoreUi
    {
        private const int Columns = 6;
        private const float CardW = 166f, CardH = 150f, Gap = 12f;

        private sealed class Card
        {
            public SkinDefinition Skin;
            public RectTransform Rect;
            public Image Back;
            public Image Icon;
            public Text Price;
            public Image Stripe;
        }

        private readonly Hud _hud;
        private readonly RectTransform _panel;
        private readonly Text _coins;
        private readonly List<Card> _cards = new List<Card>();
        private readonly Image _selIcon;
        private readonly Text _selName;
        private readonly Text _selInfo;
        private readonly RectTransform _action;
        private readonly Image _actionFill;
        private readonly Text _actionText;
        private SkinDefinition _selected;

        /// <summary>The player asks the store something: buy a skin, or put one on ("" takes it off) an item.</summary>
        public event Action<ShopOp, string, string> Asked;

        public bool IsOpen => _panel.gameObject.activeSelf;

        /// <summary>The HUD button (for tests: its place on screen).</summary>
        public RectTransform Button { get; }

        public StoreUi(Hud hud)
        {
            _hud = hud;
            var topLeft = new Vector2(0f, 1f);
            var half = new Vector2(0.5f, 0.5f);

            Button = UiFactory.NewRect("Store button", hud.SafeArea, topLeft, topLeft, new Vector2(516f, -20f), new Vector2(64f, 48f));
            UiFactory.AddImage(Button, UiFactory.Soft, UiTheme.Panel);
            UiFactory.AddIcon(Button, Icons.Bag, Vector2.zero, 32f, UiTheme.Accent);
            hud.TapTargets.Add(new Hud.TapTarget { Rect = Button, OnTap = Toggle });

            float gridW = Columns * CardW + (Columns - 1) * Gap;
            _panel = UiFactory.NewRect("Store", hud.SafeArea, half, half, new Vector2(0f, 6f), new Vector2(gridW + 80f, 712f));
            UiFactory.AddImage(_panel, UiFactory.Rounded, UiTheme.PanelSolid);
            hud.Blockers.Add(_panel);
            hud.CloseButton(_panel, Close);

            var title = UiFactory.NewRect("Title", _panel, topLeft, topLeft, new Vector2(40f, -26f), new Vector2(400f, 54f));
            UiFactory.AddText(title, "TIENDA", 42, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var note = UiFactory.NewRect("Note", _panel, topLeft, topLeft, new Vector2(40f, -76f), new Vector2(620f, 34f));
            UiFactory.AddText(note, "Skins: solo cambian el aspecto, nunca cómo funciona algo.", 24, TextAnchor.MiddleLeft, UiTheme.TextMuted);

            // The Oxcoins, top right beside the close button.
            var pill = UiFactory.NewRect("Coins", _panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-104f, -32f), new Vector2(250f, 56f));
            UiFactory.AddImage(pill, UiFactory.Soft, UiTheme.Slot);
            var coin = UiFactory.NewRect("Coin", pill, new Vector2(0f, 0.5f), half, new Vector2(32f, 0f), new Vector2(34f, 34f));
            UiFactory.AddImage(coin, UiFactory.Circle, SkinLooks.RarityColour(SkinRarity.Legendary));
            var ox = UiFactory.NewRect("Ox", coin, half, half, Vector2.zero, new Vector2(34f, 34f));
            UiFactory.AddText(ox, "O", 24, TextAnchor.MiddleCenter, new Color(0.35f, 0.22f, 0.05f), true);
            var coins = UiFactory.NewRect("Amount", pill, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(60f, 0f), new Vector2(180f, 44f));
            _coins = UiFactory.AddText(coins, "", 30, TextAnchor.MiddleLeft, UiTheme.Text, true);

            for (int i = 0; i < SkinCatalog.All.Count; i++)
            {
                var skin = SkinCatalog.All[i];
                int col = i % Columns, row = i / Columns;
                var card = new Card { Skin = skin };
                card.Rect = UiFactory.NewRect(skin.Id, _panel, topLeft, topLeft,
                    new Vector2(40f + col * (CardW + Gap), -126f - row * (CardH + Gap)), new Vector2(CardW, CardH));
                card.Back = UiFactory.AddImage(card.Rect, UiFactory.Soft, UiTheme.Slot);
                var glow = UiFactory.NewRect("Glow", card.Rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(CardW - 12f, 60f));
                UiFactory.AddImage(glow, UiFactory.Soft, SkinLooks.RarityColour(skin.Rarity).WithAlpha(0.12f));
                var icon = UiFactory.NewRect("Icon", card.Rect, half, half, new Vector2(0f, 14f), new Vector2(104f, 104f));
                card.Icon = UiFactory.AddImage(icon, null, Color.white);
                card.Icon.preserveAspect = true;
                var stripe = UiFactory.NewRect("Stripe", card.Rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(CardW - 20f, 5f));
                card.Stripe = UiFactory.AddImage(stripe, UiFactory.Soft, SkinLooks.RarityColour(skin.Rarity));
                var price = UiFactory.NewRect("Price", card.Rect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(CardW - 16f, 32f));
                card.Price = UiFactory.AddText(price, "", 23, TextAnchor.MiddleCenter, UiTheme.Text, true);
                var chosen = card;
                hud.TapTargets.Add(new Hud.TapTarget { Rect = card.Rect, OnTap = () => Select(chosen.Skin) });
                _cards.Add(card);
            }

            // The chosen skin: icon, name and rarity, and its one button.
            var bar = UiFactory.NewRect("Chosen", _panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(gridW, 84f));
            UiFactory.AddImage(bar, UiFactory.Soft, UiTheme.Slot.WithAlpha(0.55f));
            var selIcon = UiFactory.NewRect("Icon", bar, new Vector2(0f, 0.5f), half, new Vector2(48f, 0f), new Vector2(72f, 72f));
            _selIcon = UiFactory.AddImage(selIcon, null, Color.white);
            _selIcon.preserveAspect = true;
            var selName = UiFactory.NewRect("Name", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(96f, 14f), new Vector2(560f, 40f));
            _selName = UiFactory.AddText(selName, "", 30, TextAnchor.MiddleLeft, UiTheme.Text, true);
            var selInfo = UiFactory.NewRect("Info", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(96f, -20f), new Vector2(560f, 30f));
            _selInfo = UiFactory.AddText(selInfo, "", 22, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
            _action = UiFactory.NewRect("Action", bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(300f, 64f));
            _actionFill = UiFactory.AddImage(_action, UiFactory.Soft, UiTheme.Accent);
            var actionText = UiFactory.NewRect("Label", _action, half, half, Vector2.zero, new Vector2(290f, 50f));
            _actionText = UiFactory.AddText(actionText, "", 28, TextAnchor.MiddleCenter, Color.white, true);
            hud.TapTargets.Add(new Hud.TapTarget { Rect = _action, OnTap = Act });

            _panel.gameObject.SetActive(false);
            Skins.Changed += Refresh;
            _selected = SkinCatalog.All[0];
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
            Refresh();
        }

        public void Close() => _panel.gameObject.SetActive(false);

        /// <summary>Chooses a card (for tests: by skin id).</summary>
        public void Select(string skinId)
        {
            if (SkinCatalog.TryGet(skinId, out var skin)) Select(skin);
        }

        private void Select(SkinDefinition skin)
        {
            _selected = skin;
            Refresh();
        }

        /// <summary>The chosen card's button (for tests).</summary>
        public void Act()
        {
            var skin = _selected;
            if (skin == null) return;
            var locker = Skins.Current;
            if (!locker.Owns(skin.Id)) Asked?.Invoke(ShopOp.Buy, skin.ItemId, skin.Id);
            else if (locker.EquippedFor(skin.ItemId) == skin.Id) Asked?.Invoke(ShopOp.Equip, skin.ItemId, "");
            else Asked?.Invoke(ShopOp.Equip, skin.ItemId, skin.Id);
        }

        public void Refresh()
        {
            if (!IsOpen) return;
            var locker = Skins.Current;
            _coins.text = locker.Coins.ToString("N0");
            foreach (var card in _cards)
            {
                var skin = card.Skin;
                bool owned = locker.Owns(skin.Id), worn = locker.EquippedFor(skin.ItemId) == skin.Id;
                card.Icon.sprite = ItemIcons.GetExact(SkinCatalog.Tag(skin.ItemId, skin.Id)) ?? ItemIcons.GetExact(skin.ItemId);
                card.Icon.enabled = card.Icon.sprite != null;
                card.Price.text = worn ? "PUESTA" : owned ? "TUYA" : skin.Price.ToString("N0");
                card.Price.color = worn ? UiTheme.Good : owned ? UiTheme.Text : locker.Coins >= skin.Price ? SkinLooks.RarityColour(SkinRarity.Legendary) : UiTheme.TextMuted;
                bool chosen = skin == _selected;
                card.Back.color = chosen ? UiTheme.SlotHover : UiTheme.Slot;
                card.Back.sprite = chosen ? UiFactory.Outline : UiFactory.Soft;
                if (chosen) card.Back.color = SkinLooks.RarityColour(skin.Rarity);
            }

            var sel = _selected;
            if (sel == null) return;
            bool selOwned = locker.Owns(sel.Id), selWorn = locker.EquippedFor(sel.ItemId) == sel.Id;
            _selIcon.sprite = ItemIcons.GetExact(SkinCatalog.Tag(sel.ItemId, sel.Id)) ?? ItemIcons.GetExact(sel.ItemId);
            _selIcon.enabled = _selIcon.sprite != null;
            _selName.text = sel.Name;
            _selInfo.text = SkinLooks.RarityName(sel.Rarity);
            _selInfo.color = SkinLooks.RarityColour(sel.Rarity);
            if (!selOwned)
            {
                bool afford = locker.Coins >= sel.Price;
                _actionText.text = afford ? $"COMPRAR  ·  {sel.Price:N0}" : $"FALTAN {sel.Price - locker.Coins:N0}";
                _actionFill.color = afford ? UiTheme.Accent : UiTheme.Panel;
                _actionText.color = afford ? Color.white : UiTheme.TextMuted;
            }
            else
            {
                _actionText.text = selWorn ? "QUITAR" : "PONER";
                _actionFill.color = selWorn ? UiTheme.Panel : UiTheme.GoodFill;
                _actionText.color = Color.white;
            }
        }
    }
}
