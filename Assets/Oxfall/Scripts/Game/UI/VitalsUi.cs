using System;
using Oxfall.Core.Survival;
using UnityEngine;
using UnityEngine.UI;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// Health, food and water as three bars in the top left, a warning when one runs low, and the
    /// screen shown on death with the button to wake up again.
    /// </summary>
    public sealed class VitalsUi
    {
        private const float BarWidth = 300f;
        private const float BarHeight = 30f;

        private static readonly Color HealthColour = new Color(0.66f, 0.22f, 0.18f, 0.95f);
        private static readonly Color FoodColour = new Color(0.74f, 0.53f, 0.20f, 0.95f);
        private static readonly Color WaterColour = new Color(0.22f, 0.46f, 0.68f, 0.95f);

        private readonly Vitals _vitals;
        private readonly Bar _health;
        private readonly Bar _food;
        private readonly Bar _water;
        private readonly RectTransform _death;
        private int _shownHealth = -1, _shownFood = -1, _shownWater = -1;

        /// <summary>Raised when the player taps DESPERTAR on the death screen.</summary>
        public event Action WakeUpTapped;

        private sealed class Bar
        {
            public RectTransform Fill;
            public Text Label;
        }

        public VitalsUi(Hud hud, Vitals vitals)
        {
            _vitals = vitals;
            _health = MakeBar(hud.SafeArea, 0, HealthColour);
            _food = MakeBar(hud.SafeArea, 1, FoodColour);
            _water = MakeBar(hud.SafeArea, 2, WaterColour);

            // Death: a dark veil over everything, a line and one button.
            var half = new Vector2(0.5f, 0.5f);
            _death = UiFactory.NewRect("Death", hud.transform, half, half, Vector2.zero, Vector2.zero);
            UiFactory.Fill(_death);
            UiFactory.AddImage(_death, null, new Color(0.03f, 0.02f, 0.02f, 0.82f));
            hud.Modal = _death;
            var title = UiFactory.NewRect("Title", _death, half, half, new Vector2(0f, 90f), new Vector2(900f, 90f));
            UiFactory.AddText(title, "HAS MUERTO", 72, TextAnchor.MiddleCenter, new Color(0.85f, 0.35f, 0.25f));
            var hint = UiFactory.NewRect("Hint", _death, half, half, new Vector2(0f, 20f), new Vector2(1100f, 50f));
            UiFactory.AddText(hint, "Despiertas en la playa con una roca y una antorcha.", 30, TextAnchor.MiddleCenter, Hud.Muted);
            var button = UiFactory.NewRect("Wake up", _death, half, half, new Vector2(0f, -90f), new Vector2(380f, 100f));
            UiFactory.AddImage(button, UiFactory.Rounded, Hud.Accent);
            var label = UiFactory.NewRect("Label", button, half, half, Vector2.zero, new Vector2(360f, 70f));
            UiFactory.AddText(label, "DESPERTAR", 38, TextAnchor.MiddleCenter, Color.white);
            hud.TapTargets.Insert(0, new Hud.TapTarget { Rect = button, OnTap = () => WakeUpTapped?.Invoke() });
            _death.gameObject.SetActive(false);
            Refresh();
        }

        public void ShowDeath(bool dead) => _death.gameObject.SetActive(dead);

        /// <summary>Redraws the bars when a shown number changes (twice a second is plenty to call it).</summary>
        public void Refresh()
        {
            Draw(_health, _vitals.Health01, Mathf.CeilToInt(_vitals.Health), ref _shownHealth, "Vida");
            Draw(_food, _vitals.Food01, Mathf.CeilToInt(_vitals.Food), ref _shownFood, "Comida");
            Draw(_water, _vitals.Water01, Mathf.CeilToInt(_vitals.Water), ref _shownWater, "Agua");
        }

        private static void Draw(Bar bar, float fraction, int value, ref int shown, string name)
        {
            if (value == shown) return;
            shown = value;
            bar.Fill.sizeDelta = new Vector2(Mathf.Max(0f, (BarWidth - 4f) * Mathf.Clamp01(fraction)), BarHeight - 4f);
            bar.Fill.gameObject.SetActive(fraction > 0.005f);
            bar.Label.text = name + " " + value;
        }

        private static Bar MakeBar(RectTransform parent, int row, Color colour)
        {
            var topLeft = new Vector2(0f, 1f);
            var left = new Vector2(0f, 0.5f);
            var back = UiFactory.NewRect("Bar", parent, topLeft, topLeft, new Vector2(28f, -112f - row * (BarHeight + 10f)),
                new Vector2(BarWidth, BarHeight));
            UiFactory.AddImage(back, UiFactory.Rounded, Hud.Panel);
            var bar = new Bar();
            bar.Fill = UiFactory.NewRect("Fill", back, left, left, new Vector2(2f, 0f), new Vector2(BarWidth - 4f, BarHeight - 4f));
            UiFactory.AddImage(bar.Fill, UiFactory.Rounded, colour);
            var label = UiFactory.NewRect("Label", back, left, left, new Vector2(12f, 0f), new Vector2(BarWidth - 20f, BarHeight));
            bar.Label = UiFactory.AddText(label, "", 22, TextAnchor.MiddleLeft, Color.white);
            return bar;
        }
    }
}
