using System;
using System.Collections.Generic;
using Oxfall.Core.Survival;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// Health, food and water under the clock in the top left: an icon, a slim bar and the number,
    /// as the PC game shows them. A vital running low pulses red. Also the screen shown on death,
    /// with the button to wake up again.
    /// </summary>
    public sealed class VitalsUi
    {
        private const float RowWidth = 290f;
        private const float RowHeight = 40f;
        private const float TrackWidth = 178f;
        private const float Low = 0.2f;

        private readonly Vitals _vitals;
        private readonly Row _health;
        private readonly Row _food;
        private readonly Row _water;
        private readonly RectTransform _death;
        private int _shownHealth = -1, _shownFood = -1, _shownWater = -1;

        /// <summary>Raised when the player taps DESPERTAR (on the beach) on the death screen.</summary>
        public event Action WakeUpTapped;

        /// <summary>Raised when the player taps one of their sleeping bags on the death screen (its id).</summary>
        public event Action<int> BagTapped;

        private const int MaxBags = 3;
        private const float ChoiceWidth = 330f;
        private const float ChoiceGap = 26f;

        private sealed class BagButton
        {
            public RectTransform Rect;
            public Image Fill;
            public Text Title;
            public Text State;
            public int Id;
            public Func<float> ReadyIn;
        }

        private readonly BagButton[] _bags = new BagButton[MaxBags];
        private RectTransform _beach;
        private Text _beachLabel;
        private Text _hint;
        private int _bagCount;

        private sealed class Row
        {
            public RectTransform Fill;
            public Image FillImage;
            public Image Icon;
            public Text Value;
            public Color Colour;
            public bool Low;
        }

        public VitalsUi(Hud hud, Vitals vitals)
        {
            _vitals = vitals;
            _health = MakeRow(hud.SafeArea, 0, Icons.Heart, UiTheme.Health);
            _food = MakeRow(hud.SafeArea, 1, Icons.Food, UiTheme.Food);
            _water = MakeRow(hud.SafeArea, 2, Icons.Drop, UiTheme.Water);

            // Death: a dark veil over everything, a title, a line and one button.
            var half = new Vector2(0.5f, 0.5f);
            _death = UiFactory.NewRect("Death", hud.transform, half, half, Vector2.zero, Vector2.zero);
            UiFactory.Fill(_death);
            UiFactory.AddImage(_death, null, new Color(0.03f, 0.02f, 0.02f, 0.86f));
            hud.Modal = _death;
            var glow = UiFactory.NewRect("Glow", _death, half, half, new Vector2(0f, 60f), new Vector2(1400f, 700f));
            UiFactory.AddImage(glow, Icons.Glow, UiTheme.Bad.WithAlpha(0.16f));
            var title = UiFactory.NewRect("Title", _death, half, half, new Vector2(0f, 110f), new Vector2(1000f, 120f));
            UiFactory.AddText(title, "HAS MUERTO", 110, TextAnchor.MiddleCenter, UiTheme.Bad, true);
            var line = UiFactory.NewRect("Line", _death, half, half, new Vector2(0f, 38f), new Vector2(80f, 4f));
            UiFactory.AddImage(line, null, UiTheme.Accent);
            var hint = UiFactory.NewRect("Hint", _death, half, half, new Vector2(0f, -26f), new Vector2(1300f, 50f));
            _hint = UiFactory.AddText(hint, "", 32, TextAnchor.MiddleCenter, UiTheme.TextMuted);
            _beach = UiFactory.NewRect("Wake up", _death, half, half, new Vector2(0f, -130f), new Vector2(ChoiceWidth, 110f));
            UiFactory.AddImage(_beach, UiFactory.Soft, UiTheme.Accent);
            var label = UiFactory.NewRect("Label", _beach, half, half, Vector2.zero, new Vector2(ChoiceWidth - 20f, 70f));
            _beachLabel = UiFactory.AddText(label, "DESPERTAR", 42, TextAnchor.MiddleCenter, Color.white, true);
            hud.TapTargets.Insert(0, new Hud.TapTarget { Rect = _beach, OnTap = () => WakeUpTapped?.Invoke() });

            // The player's sleeping bags, beside the beach: each says when it is ready.
            for (int i = 0; i < MaxBags; i++)
            {
                var bag = new BagButton();
                bag.Rect = UiFactory.NewRect("Bag", _death, half, half, Vector2.zero, new Vector2(ChoiceWidth, 110f));
                bag.Fill = UiFactory.AddImage(bag.Rect, UiFactory.Soft, UiTheme.Slot);
                var left = new Vector2(0f, 0.5f);
                UiFactory.AddIcon(bag.Rect, Icons.Bed, new Vector2(56f, 0f), 64f, UiTheme.Text, left);
                var bagTitle = UiFactory.NewRect("Title", bag.Rect, left, left, new Vector2(104f, 18f), new Vector2(210f, 44f));
                bag.Title = UiFactory.AddText(bagTitle, "", 34, TextAnchor.MiddleLeft, UiTheme.Text, true);
                var bagState = UiFactory.NewRect("State", bag.Rect, left, left, new Vector2(104f, -22f), new Vector2(210f, 34f));
                bag.State = UiFactory.AddText(bagState, "", 24, TextAnchor.MiddleLeft, UiTheme.TextMuted, true);
                var captured = bag;
                hud.TapTargets.Insert(0, new Hud.TapTarget { Rect = bag.Rect, OnTap = () => BagTapped?.Invoke(captured.Id) });
                bag.Rect.gameObject.SetActive(false);
                _bags[i] = bag;
            }
            SetBags(null);
            _death.gameObject.SetActive(false);
            Refresh();
        }

        public void ShowDeath(bool dead) => _death.gameObject.SetActive(dead);

        public bool DeathShown => _death.gameObject.activeSelf;

        /// <summary>
        /// The sleeping bags to offer on the death screen (up to three): their ids and how many
        /// seconds until each is ready. None: only the beach.
        /// </summary>
        private string _cause;

        /// <summary>What killed the player ("Te mató Bea."), shown above the choices; null for nothing said.</summary>
        public void SetCause(string cause) => _cause = cause;

        public void SetBags(IReadOnlyList<(int Id, Func<float> ReadyIn)> bags)
        {
            _bagCount = bags == null ? 0 : Math.Min(bags.Count, MaxBags);
            for (int i = 0; i < MaxBags; i++)
            {
                var bag = _bags[i];
                bool show = i < _bagCount;
                bag.Rect.gameObject.SetActive(show);
                if (!show) continue;
                bag.Id = bags[i].Id;
                bag.ReadyIn = bags[i].ReadyIn;
                bag.Title.text = _bagCount == 1 ? "SACO" : "SACO " + (i + 1);
            }
            // The choices in one row, centred.
            int count = _bagCount + 1;
            float width = count * ChoiceWidth + (count - 1) * ChoiceGap;
            float x = -width * 0.5f + ChoiceWidth * 0.5f;
            _beach.anchoredPosition = new Vector2(x, -130f);
            for (int i = 0; i < _bagCount; i++)
                _bags[i].Rect.anchoredPosition = new Vector2(x + (i + 1) * (ChoiceWidth + ChoiceGap), -130f);
            _beachLabel.text = _bagCount > 0 ? "EN LA PLAYA" : "DESPERTAR";
            _hint.text = (string.IsNullOrEmpty(_cause) ? "" : _cause + "  ") + (_bagCount > 0
                ? "Elige dónde despertar: en la playa o en uno de tus sacos de dormir."
                : "Despiertas en la playa con una roca y una antorcha.") +
                "\nLo que llevabas quedó en una bolsa donde caíste: corre a por ella.";
            TickBags();
        }

        /// <summary>The countdown on each bag (called every frame while dead is plenty cheap: only text that changed is set).</summary>
        private void TickBags()
        {
            for (int i = 0; i < _bagCount; i++)
            {
                var bag = _bags[i];
                float wait = bag.ReadyIn();
                bool ready = wait <= 0f;
                string text = ready ? "LISTO" : $"{(int)wait / 60}:{(int)wait % 60:00}";
                if (bag.State.text != text) bag.State.text = text;
                var fill = ready ? UiTheme.Accent.WithAlpha(0.9f) : UiTheme.Slot;
                if (bag.Fill.color != fill) bag.Fill.color = fill;
                var colour = ready ? Color.white : UiTheme.TextMuted;
                if (bag.State.color != colour) bag.State.color = colour;
            }
        }

        /// <summary>Redraws the bars when a shown number changes (twice a second is plenty to call it).</summary>
        public void Refresh()
        {
            Draw(_health, _vitals.Health01, Mathf.CeilToInt(_vitals.Health), ref _shownHealth);
            Draw(_food, _vitals.Food01, Mathf.CeilToInt(_vitals.Food), ref _shownFood);
            Draw(_water, _vitals.Water01, Mathf.CeilToInt(_vitals.Water), ref _shownWater);
        }

        /// <summary>Every frame: a vital running low pulses its icon and bar.</summary>
        public void Tick()
        {
            if (_bagCount > 0 && _death.gameObject.activeSelf) TickBags();
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 6f);
            Pulse(_health, pulse);
            Pulse(_food, pulse);
            Pulse(_water, pulse);
        }

        private static void Pulse(Row row, float pulse)
        {
            if (!row.Low) return;
            var c = Color.Lerp(row.Colour, UiTheme.Bad, 0.5f);
            c.a = 0.4f + 0.6f * pulse;
            row.Icon.color = c;
            row.FillImage.color = c;
        }

        private static void Draw(Row row, float fraction, int value, ref int shown)
        {
            if (value == shown) return;
            shown = value;
            fraction = Mathf.Clamp01(fraction);
            row.Fill.sizeDelta = new Vector2(TrackWidth * fraction, 8f);
            row.Fill.gameObject.SetActive(fraction > 0.005f);
            row.Value.text = value.ToString();
            bool low = fraction < Low;
            if (low == row.Low) return;
            row.Low = low;
            row.Icon.color = row.Colour;
            row.FillImage.color = row.Colour;
            row.Value.color = low ? UiTheme.Bad : UiTheme.Text;
        }

        private static Row MakeRow(RectTransform parent, int index, Sprite icon, Color colour)
        {
            var topLeft = new Vector2(0f, 1f);
            var left = new Vector2(0f, 0.5f);
            var back = UiFactory.NewRect("Vital", parent, topLeft, topLeft, new Vector2(24f, -80f - index * (RowHeight + 6f)),
                new Vector2(RowWidth, RowHeight));
            UiFactory.AddImage(back, UiFactory.Soft, UiTheme.Panel);
            // Its own canvas: a pulsing bar then redraws only itself, not the whole HUD.
            back.gameObject.AddComponent<Canvas>();
            var row = new Row { Colour = colour };
            row.Icon = UiFactory.AddIcon(back, icon, new Vector2(24f, 0f), 26f, colour, left);
            var track = UiFactory.NewRect("Track", back, left, left, new Vector2(48f, 0f), new Vector2(TrackWidth, 8f));
            UiFactory.AddImage(track, UiFactory.Soft, new Color(1f, 1f, 1f, 0.1f));
            row.Fill = UiFactory.NewRect("Fill", track, left, left, Vector2.zero, new Vector2(TrackWidth, 8f));
            row.FillImage = UiFactory.AddImage(row.Fill, UiFactory.Soft, colour);
            var value = UiFactory.NewRect("Value", back, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(56f, RowHeight));
            row.Value = UiFactory.AddText(value, "", 26, TextAnchor.MiddleRight, UiTheme.Text, true);
            return row;
        }
    }
}
