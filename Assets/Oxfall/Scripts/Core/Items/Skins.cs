using System;
using System.Collections.Generic;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Items
{
    /// <summary>How rare (and dear) a skin is.</summary>
    public enum SkinRarity : byte { Common, Rare, Epic, Legendary }

    /// <summary>One material slot of the item's model recoloured: its colour, shine and metalness (0..1).</summary>
    public readonly struct SkinTint
    {
        public readonly int Slot;
        public readonly float R, G, B, Smoothness, Metallic;

        public SkinTint(int slot, float r, float g, float b, float smoothness = 0.3f, float metallic = 0f)
        {
            Slot = slot;
            R = r; G = g; B = b;
            Smoothness = smoothness;
            Metallic = metallic;
        }
    }

    /// <summary>A look for one item, bought with Oxcoins. Only looks: it never changes what the item does.</summary>
    public sealed class SkinDefinition
    {
        public string Id { get; }
        public string ItemId { get; }
        public string Name { get; }
        public SkinRarity Rarity { get; }
        public int Price { get; }
        public IReadOnlyList<SkinTint> Tints { get; }

        public SkinDefinition(string id, string itemId, string name, SkinRarity rarity, params SkinTint[] tints)
        {
            Id = id;
            ItemId = itemId;
            Name = name;
            Rarity = rarity;
            Price = SkinCatalog.PriceOf(rarity);
            Tints = tints;
        }
    }

    /// <summary>Every skin in the store. Ids are kept forever: they are what lockers remember.</summary>
    public static class SkinCatalog
    {
        /// <summary>What a new player finds in their locker, to try the store.</summary>
        public const int WelcomeCoins = 300;

        public static int PriceOf(SkinRarity rarity)
        {
            switch (rarity)
            {
                case SkinRarity.Rare: return 300;
                case SkinRarity.Epic: return 600;
                case SkinRarity.Legendary: return 1200;
                default: return 150;
            }
        }

        private static SkinTint T(int slot, float r, float g, float b, float smooth = 0.3f, float metal = 0f) => new SkinTint(slot, r, g, b, smooth, metal);

        // Gold, silver, carbon, arctic, crimson, forest, sand and midnight: the house palette.
        // Half metallic: fully metallic reads brown on phones, with no reflections to show the shine.
        private static SkinTint Gold(int slot) => T(slot, 1f, 0.76f, 0.26f, 0.68f, 0.45f);
        private static SkinTint Silver(int slot) => T(slot, 0.9f, 0.92f, 0.94f, 0.72f, 0.45f);
        private static SkinTint Carbon(int slot) => T(slot, 0.07f, 0.075f, 0.08f, 0.62f, 0.2f);
        private static SkinTint Arctic(int slot) => T(slot, 0.88f, 0.9f, 0.92f, 0.35f);
        private static SkinTint Crimson(int slot) => T(slot, 0.55f, 0.07f, 0.06f, 0.4f);
        private static SkinTint Forest(int slot) => T(slot, 0.2f, 0.29f, 0.15f, 0.15f);
        private static SkinTint Sand(int slot) => T(slot, 0.72f, 0.6f, 0.42f, 0.12f);
        private static SkinTint Midnight(int slot) => T(slot, 0.08f, 0.1f, 0.17f, 0.2f);

        public static readonly IReadOnlyList<SkinDefinition> All = new[]
        {
            // Rifle: planks (0), gun metal (1), brass (2).
            new SkinDefinition("rifle_gold", ItemIds.SemiRifle, "Rifle Oro", SkinRarity.Legendary, Carbon(0), Gold(1), Gold(2)),
            new SkinDefinition("rifle_arctic", ItemIds.SemiRifle, "Rifle Ártico", SkinRarity.Epic, Arctic(0), T(1, 0.6f, 0.64f, 0.68f, 0.5f, 0.6f)),
            new SkinDefinition("rifle_carbon", ItemIds.SemiRifle, "Rifle Carbono", SkinRarity.Rare, Carbon(0), Carbon(1)),
            // Revolver and blunderbuss.
            new SkinDefinition("revolver_gold", ItemIds.Revolver, "Revólver Oro", SkinRarity.Epic, Gold(0), Gold(1)),
            new SkinDefinition("revolver_silver", ItemIds.Revolver, "Revólver Plata", SkinRarity.Rare, Silver(0), Silver(1)),
            new SkinDefinition("blunderbuss_crimson", ItemIds.Blunderbuss, "Trabuco Carmesí", SkinRarity.Common, Crimson(0)),
            // Bow and tools.
            new SkinDefinition("bow_ebony", ItemIds.WoodenBow, "Arco Ébano", SkinRarity.Rare, Carbon(0)),
            new SkinDefinition("hatchet_crimson", ItemIds.StoneHatchet, "Hacha Carmesí", SkinRarity.Common, Crimson(0)),
            new SkinDefinition("pickaxe_gold", ItemIds.MetalPickaxe, "Pico Oro", SkinRarity.Epic, Carbon(0), Gold(1)),
            // Clothes: main cloth or hide (0).
            new SkinDefinition("shirt_sand", ItemIds.BurlapShirt, "Camisa Arena", SkinRarity.Common, Sand(0)),
            new SkinDefinition("shirt_midnight", ItemIds.BurlapShirt, "Camisa Medianoche", SkinRarity.Rare, Midnight(0)),
            new SkinDefinition("trousers_forest", ItemIds.BurlapTrousers, "Pantalón Bosque", SkinRarity.Common, Forest(0)),
            new SkinDefinition("hood_arctic", ItemIds.HideHood, "Capucha Ártica", SkinRarity.Rare, Arctic(0)),
            new SkinDefinition("vest_midnight", ItemIds.HideVest, "Chaleco Medianoche", SkinRarity.Rare, Midnight(0)),
            // Metal armour: plate (0).
            new SkinDefinition("facemask_gold", ItemIds.MetalFacemask, "Máscara Oro", SkinRarity.Legendary, Gold(0)),
            new SkinDefinition("facemask_crimson", ItemIds.MetalFacemask, "Máscara Carmesí", SkinRarity.Epic, T(0, 0.5f, 0.08f, 0.06f, 0.55f, 0.7f)),
            new SkinDefinition("chest_gold", ItemIds.MetalChestPlate, "Peto Oro", SkinRarity.Legendary, Gold(0)),
        };

        private static readonly Dictionary<string, SkinDefinition> ById = Index();

        private static Dictionary<string, SkinDefinition> Index()
        {
            var map = new Dictionary<string, SkinDefinition>(StringComparer.Ordinal);
            foreach (var skin in All) map.Add(skin.Id, skin);
            return map;
        }

        public static bool TryGet(string id, out SkinDefinition skin)
        {
            skin = null;
            return !string.IsNullOrEmpty(id) && ById.TryGetValue(id, out skin);
        }

        /// <summary>The skins made for an item, in store order.</summary>
        public static IEnumerable<SkinDefinition> For(string itemId)
        {
            foreach (var skin in All) if (skin.ItemId == itemId) yield return skin;
        }

        /// <summary>
        /// An item id with its skin, as other players are told what someone holds or wears:
        /// "semi_auto_rifle@rifle_gold", or the bare id with no skin.
        /// </summary>
        public static string Tag(string itemId, string skinId) =>
            string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(skinId) ? itemId ?? "" : itemId + "@" + skinId;

        /// <summary>Splits a tagged id back into the item and its skin ("" for none).</summary>
        public static string Untag(string tagged, out string skinId)
        {
            skinId = "";
            if (string.IsNullOrEmpty(tagged)) return tagged ?? "";
            int at = tagged.IndexOf('@');
            if (at < 0) return tagged;
            skinId = tagged.Substring(at + 1);
            return tagged.Substring(0, at);
        }
    }

    /// <summary>
    /// A player's skins: Oxcoins to spend, the skins bought, and which one each item wears. It
    /// belongs to the player, not the island, so it outlives every wipe.
    /// </summary>
    public sealed class Locker
    {
        private readonly HashSet<string> _owned = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _equipped = new Dictionary<string, string>(StringComparer.Ordinal);

        public int Coins { get; private set; }
        public IEnumerable<string> Owned => _owned;
        public int OwnedCount => _owned.Count;

        public Locker(int coins = 0) { Coins = Math.Max(0, coins); }

        public bool Owns(string skinId) => skinId != null && _owned.Contains(skinId);

        /// <summary>The skin an item wears ("" for its own look).</summary>
        public string EquippedFor(string itemId) =>
            itemId != null && _equipped.TryGetValue(itemId, out var skin) ? skin : "";

        public void AddCoins(int amount)
        {
            if (amount <= 0) return;
            Coins = (int)Math.Min(int.MaxValue, (long)Coins + amount);
        }

        /// <summary>Buys a skin with Oxcoins and puts it on its item at once.</summary>
        public ActionResult Buy(string skinId)
        {
            if (!SkinCatalog.TryGet(skinId, out var skin)) return ActionResult.Invalid;
            if (_owned.Contains(skinId)) return ActionResult.AlreadyOwned;
            if (Coins < skin.Price) return ActionResult.NotEnoughCoins;
            Coins -= skin.Price;
            _owned.Add(skinId);
            _equipped[skin.ItemId] = skinId;
            return ActionResult.Ok;
        }

        /// <summary>Puts an owned skin on its item, or with "" takes the item's skin off.</summary>
        public ActionResult Equip(string itemId, string skinId)
        {
            if (string.IsNullOrEmpty(itemId)) return ActionResult.Invalid;
            if (string.IsNullOrEmpty(skinId)) { _equipped.Remove(itemId); return ActionResult.Ok; }
            if (!SkinCatalog.TryGet(skinId, out var skin) || skin.ItemId != itemId) return ActionResult.Invalid;
            if (!_owned.Contains(skinId)) return ActionResult.NotOwned;
            _equipped[itemId] = skinId;
            return ActionResult.Ok;
        }

        public void Write(NetWriter w)
        {
            w.Int(Coins);
            WriteIds(w, _owned);
            w.UShort((ushort)_equipped.Count);
            foreach (var pair in _equipped) { w.String(pair.Key, 64); w.String(pair.Value, 64); }
        }

        /// <summary>Reads a locker; skins no longer in the catalogue are dropped.</summary>
        public static Locker Read(NetReader r)
        {
            var locker = new Locker(r.Int());
            int owned = r.UShort();
            for (int i = 0; i < owned; i++)
            {
                string id = r.String();
                if (SkinCatalog.TryGet(id, out _)) locker._owned.Add(id);
            }
            int equipped = r.UShort();
            for (int i = 0; i < equipped; i++)
            {
                string item = r.String(), skin = r.String();
                if (locker._owned.Contains(skin)) locker._equipped[item] = skin;
            }
            return locker;
        }

        private static void WriteIds(NetWriter w, ICollection<string> ids)
        {
            w.UShort((ushort)ids.Count);
            foreach (var id in ids) w.String(id, 64);
        }
    }

    /// <summary>Every player's locker on a server, found by their token; saved apart from the island.</summary>
    public sealed class Lockers
    {
        private const int SaveVersion = 1;
        private readonly Dictionary<string, Locker> _byToken = new Dictionary<string, Locker>(StringComparer.Ordinal);

        public int Count => _byToken.Count;

        /// <summary>Set whenever a locker changes, until the server saves.</summary>
        public bool Dirty { get; set; }

        /// <summary>The player's locker, opened with the welcome coins the first time.</summary>
        public Locker For(string token)
        {
            token = token ?? "";
            if (!_byToken.TryGetValue(token, out var locker))
            {
                locker = new Locker(SkinCatalog.WelcomeCoins);
                _byToken.Add(token, locker);
                Dirty = true;
            }
            return locker;
        }

        public bool Has(string token) => token != null && _byToken.ContainsKey(token);

        public byte[] Save()
        {
            var w = new NetWriter(4096);
            w.Int(SaveVersion);
            w.Int(_byToken.Count);
            foreach (var pair in _byToken)
            {
                w.String(pair.Key, 128);
                pair.Value.Write(w);
            }
            return w.ToArray();
        }

        public bool Load(byte[] data, out string why)
        {
            why = null;
            try
            {
                var r = new NetReader(data);
                int version = r.Int();
                if (version != SaveVersion) { why = "unknown locker save version " + version; return false; }
                int count = r.Int();
                var loaded = new Dictionary<string, Locker>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    string token = r.String();
                    loaded[token] = Locker.Read(r);
                }
                _byToken.Clear();
                foreach (var pair in loaded) _byToken.Add(pair.Key, pair.Value);
                return true;
            }
            catch (Exception e) when (e is ProtocolException || e is IndexOutOfRangeException || e is ArgumentException)
            {
                why = e.Message;
                return false;
            }
        }
    }
}
