using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>The skin store as the server runs it: each player's locker, and their skins shown to everyone.</summary>
    public sealed partial class ServerWorld
    {
        /// <summary>The test kit number (<see cref="ActKind.DevGive"/>) that puts 1000 Oxcoins in the locker.</summary>
        public const int DevCoinsKit = 99;

        /// <summary>Every player's locker. Saved apart from the island, so a wipe keeps them.</summary>
        public Lockers Lockers { get; } = new Lockers();

        /// <summary>Players whose locker changed since they were last told (the server clears it).</summary>
        public HashSet<ushort> LockerNews { get; } = new HashSet<ushort>();

        public bool TryGetLocker(ushort playerId, out Locker locker)
        {
            locker = null;
            if (!_online.TryGetValue(playerId, out var record)) return false;
            locker = Lockers.For(record.Token);
            return true;
        }

        /// <summary>A player buys a skin, or puts one on (or "" to take it off) an item.</summary>
        public ActionResult ShopAct(ushort playerId, ShopOp op, string itemId, string skinId)
        {
            if (!TryGetLocker(playerId, out var locker)) return ActionResult.Invalid;
            ActionResult result;
            switch (op)
            {
                case ShopOp.Buy: result = locker.Buy(skinId); break;
                case ShopOp.Equip: result = locker.Equip(itemId, skinId); break;
                default: return ActionResult.Invalid;
            }
            if (result == ActionResult.Ok)
            {
                Lockers.Dirty = true;
                LockerNews.Add(playerId);
            }
            return result;
        }

        /// <summary>The ids a player wears, each tagged with its skin, in place.</summary>
        public void TagWorn(ushort playerId, string[] worn)
        {
            if (!TryGetLocker(playerId, out var locker)) return;
            for (int i = 0; i < worn.Length; i++)
                if (!string.IsNullOrEmpty(worn[i])) worn[i] = SkinCatalog.Tag(worn[i], locker.EquippedFor(worn[i]));
        }

        /// <summary>What a player holds, tagged with its skin.</summary>
        public string TagHeld(ushort playerId, string held)
        {
            if (string.IsNullOrEmpty(held) || !TryGetLocker(playerId, out var locker)) return held;
            return SkinCatalog.Tag(held, locker.EquippedFor(held));
        }
    }
}
