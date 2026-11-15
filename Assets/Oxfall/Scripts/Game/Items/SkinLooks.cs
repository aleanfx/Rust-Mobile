using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using UnityEngine;

namespace Oxfall.Game.Items
{
    /// <summary>
    /// The player's own skin locker on this phone: from the server when online, kept on the phone
    /// when playing alone. Everything that draws an item asks it which skin to wear.
    /// </summary>
    public static class Skins
    {
        private const string PrefsKey = "oxfall.locker";

        /// <summary>The locker as it stands now (never null).</summary>
        public static Locker Current
        {
            get => _current ??= LoadOffline();
            private set => _current = value;
        }

        private static Locker _current;

        /// <summary>Whether the locker comes from a server (true) or lives on this phone.</summary>
        public static bool Online { get; private set; }

        /// <summary>Raised whenever the locker changes: coins, skins bought or worn.</summary>
        public static event Action Changed;

        public static string EquippedFor(string itemId) => Current.EquippedFor(itemId);

        /// <summary>The locker the server sent.</summary>
        public static void FromServer(Locker locker)
        {
            if (locker == null) return;
            Online = true;
            Current = locker;
            Changed?.Invoke();
        }

        /// <summary>Back to the phone's own locker (playing alone).</summary>
        public static void UseOffline()
        {
            if (!Online) return;
            Online = false;
            Current = LoadOffline();
            Changed?.Invoke();
        }

        /// <summary>
        /// A change made on the phone's own locker (playing alone): applied and saved here. Online the
        /// server applies it and sends the locker back.
        /// </summary>
        public static Core.Rules.ActionResult ApplyOffline(ShopOp op, string itemId, string skinId)
        {
            var result = op == ShopOp.Buy ? Current.Buy(skinId) : Current.Equip(itemId, skinId);
            if (result == Core.Rules.ActionResult.Ok) SaveOffline();
            Changed?.Invoke();
            return result;
        }

        /// <summary>Test coins on the phone's own locker.</summary>
        public static void AddOfflineCoins(int amount)
        {
            if (Online) return;
            Current.AddCoins(amount);
            SaveOffline();
            Changed?.Invoke();
        }

        private static Locker LoadOffline()
        {
            try
            {
                string saved = PlayerPrefs.GetString(PrefsKey, "");
                if (saved.Length > 0) return Locker.Read(new NetReader(Convert.FromBase64String(saved)));
            }
            catch (Exception e) when (e is FormatException || e is ProtocolException || e is IndexOutOfRangeException || e is ArgumentException)
            {
                Debug.LogWarning("[Oxfall] Locker on this phone unreadable, starting a new one: " + e.Message);
            }
            return new Locker(SkinCatalog.WelcomeCoins);
        }

        private static void SaveOffline()
        {
            if (Online) return;
            var w = new NetWriter(512);
            Current.Write(w);
            PlayerPrefs.SetString(PrefsKey, Convert.ToBase64String(w.ToArray()));
            PlayerPrefs.Save();
        }
    }

    /// <summary>The materials of a skinned item: its own, with the skin's slots recoloured (made once, then shared).</summary>
    public static class SkinLooks
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");

        private static readonly Dictionary<string, Material[]> Sets = new Dictionary<string, Material[]>();
        private static readonly Dictionary<(Material, string, int), Material> Tinted = new Dictionary<(Material, string, int), Material>();

        /// <summary>A model's materials in a skin; its own materials for no skin (or a skin for another item).</summary>
        public static Material[] For(string itemId, ItemModel model, string skinId)
        {
            if (model?.Materials == null || !SkinCatalog.TryGet(skinId, out var skin) || skin.ItemId != itemId) return model?.Materials;
            if (Sets.TryGetValue(skinId, out var set)) return set;
            set = (Material[])model.Materials.Clone();
            foreach (var tint in skin.Tints)
                if (tint.Slot >= 0 && tint.Slot < set.Length) set[tint.Slot] = Tint(model.Materials[tint.Slot], skin, tint);
            Sets[skinId] = set;
            return set;
        }

        /// <summary>One material in a skin's colour for one slot (for clothes drawn on a figure); the material itself if the skin leaves it be.</summary>
        public static Material Slot(Material source, string skinId, int slot)
        {
            if (source == null || !SkinCatalog.TryGet(skinId, out var skin)) return source;
            foreach (var tint in skin.Tints)
                if (tint.Slot == slot) return Tint(source, skin, tint);
            return source;
        }

        private static Material Tint(Material source, SkinDefinition skin, SkinTint tint)
        {
            var key = (source, skin.Id, tint.Slot);
            if (Tinted.TryGetValue(key, out var made)) return made;
            made = new Material(source) { name = source.name + " (" + skin.Id + ")" };
            var colour = new Color(tint.R, tint.G, tint.B);
            if (made.HasProperty(BaseColor)) made.SetColor(BaseColor, colour);
            if (made.HasProperty(ColorId)) made.SetColor(ColorId, colour);
            if (made.HasProperty(Smoothness)) made.SetFloat(Smoothness, tint.Smoothness);
            if (made.HasProperty(Metallic)) made.SetFloat(Metallic, tint.Metallic);
            // A tint shows truest on plain white: textured materials lose their texture's colour.
            if (made.HasProperty("_BaseMap")) made.SetTexture("_BaseMap", null);
            Tinted[key] = made;
            return made;
        }

        /// <summary>The colour a rarity is shown in (grey, blue, purple, gold).</summary>
        public static Color RarityColour(SkinRarity rarity)
        {
            switch (rarity)
            {
                case SkinRarity.Rare: return new Color(0.33f, 0.62f, 0.95f);
                case SkinRarity.Epic: return new Color(0.68f, 0.42f, 0.95f);
                case SkinRarity.Legendary: return new Color(0.98f, 0.72f, 0.22f);
                default: return new Color(0.7f, 0.72f, 0.74f);
            }
        }

        public static string RarityName(SkinRarity rarity)
        {
            switch (rarity)
            {
                case SkinRarity.Rare: return "RARA";
                case SkinRarity.Epic: return "ÉPICA";
                case SkinRarity.Legendary: return "LEGENDARIA";
                default: return "COMÚN";
            }
        }
    }
}
