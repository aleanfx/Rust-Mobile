using UnityEngine;

namespace Oxfall.Game.UI
{
    /// <summary>
    /// The look of every screen in one place: "ember and ash". Graphite panels that let the world
    /// show through, warm off-white text, one ember-orange accent for whatever is chosen or ready,
    /// moss green for "you can", and a condensed typeface (Barlow Condensed, OFL) for a clean,
    /// military-manual feel that stays readable small on a phone.
    /// </summary>
    public static class UiTheme
    {
        // Surfaces
        public static readonly Color Panel = new Color(0.075f, 0.082f, 0.09f, 0.82f);
        public static readonly Color PanelSolid = new Color(0.075f, 0.082f, 0.09f, 0.94f);
        public static readonly Color Slot = new Color(0.16f, 0.17f, 0.185f, 0.78f);
        public static readonly Color SlotHover = new Color(0.22f, 0.235f, 0.25f, 0.9f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Button = new Color(0.075f, 0.082f, 0.09f, 0.55f);
        public static readonly Color ButtonPressed = new Color(0.95f, 0.47f, 0.18f, 0.9f);
        public static readonly Color Veil = new Color(0.02f, 0.022f, 0.025f, 0.6f);

        // Text
        public static readonly Color Text = new Color(0.93f, 0.91f, 0.87f);
        public static readonly Color TextMuted = new Color(0.62f, 0.64f, 0.66f);
        public static readonly Color TextFaint = new Color(1f, 1f, 1f, 0.38f);

        // Signals
        public static readonly Color Accent = new Color(1f, 0.48f, 0.18f);
        public static readonly Color AccentSoft = new Color(1f, 0.48f, 0.18f, 0.22f);
        public static readonly Color Good = new Color(0.50f, 0.72f, 0.29f);
        public static readonly Color GoodFill = new Color(0.38f, 0.58f, 0.22f, 0.92f);
        public static readonly Color Bad = new Color(0.90f, 0.29f, 0.30f);

        // Vitals
        public static readonly Color Health = new Color(0.85f, 0.30f, 0.27f);
        public static readonly Color Food = new Color(0.91f, 0.64f, 0.24f);
        public static readonly Color Water = new Color(0.29f, 0.64f, 0.87f);

        private static Font _regular;
        private static Font _bold;

        /// <summary>Barlow Condensed SemiBold, or the built-in font if it is missing.</summary>
        public static Font Regular => _regular != null ? _regular : (_regular = Load("Fonts/BarlowCondensed-SemiBold"));

        /// <summary>Barlow Condensed Bold, for titles, buttons and numbers.</summary>
        public static Font Bold => _bold != null ? _bold : (_bold = Load("Fonts/BarlowCondensed-Bold"));

        private static Font Load(string resource)
        {
            var font = Resources.Load<Font>(resource);
            return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
