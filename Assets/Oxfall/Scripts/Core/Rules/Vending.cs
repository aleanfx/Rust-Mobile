using System;
using Oxfall.Core.Items;

namespace Oxfall.Core.Rules
{
    /// <summary>One line of a vending machine: so many of one thing for so many of another.</summary>
    public readonly struct VendOrder : IEquatable<VendOrder>
    {
        /// <summary>What the machine gives, and how many each time.</summary>
        public readonly string Sell;
        public readonly int SellAmount;

        /// <summary>What the buyer pays, and how many.</summary>
        public readonly string Price;
        public readonly int PriceAmount;

        public VendOrder(string sell, int sellAmount, string price, int priceAmount)
        {
            Sell = string.IsNullOrEmpty(sell) ? null : sell;
            SellAmount = sellAmount;
            Price = string.IsNullOrEmpty(price) ? null : price;
            PriceAmount = priceAmount;
        }

        public static VendOrder None => default;

        public bool IsEmpty => Sell == null;

        public bool Equals(VendOrder o) => Sell == o.Sell && SellAmount == o.SellAmount && Price == o.Price && PriceAmount == o.PriceAmount;
        public override bool Equals(object obj) => obj is VendOrder o && Equals(o);
        public override int GetHashCode() => ((Sell ?? "").GetHashCode() * 31 + SellAmount) * 31 + (Price ?? "").GetHashCode() * 7 + PriceAmount;

        public override string ToString() => IsEmpty ? "(none)" : $"{SellAmount} {Sell} for {PriceAmount} {Price}";
    }

    /// <summary>
    /// The vending machine: a shop anyone can use with nobody minding it. Its owner stocks it (only
    /// they can open its slots) and sets up to four offers; anyone else sees what is on offer and how
    /// many times each can still be bought, and buys with a tap: the price goes into the machine, the
    /// goods into their pockets. Built at a workbench, no blueprint needed.
    /// </summary>
    public static class Vending
    {
        /// <summary>Offers a machine shows.</summary>
        public const int Orders = 4;

        /// <summary>Slots inside (stock and takings share them).</summary>
        public const int Slots = 12;

        /// <summary>The most of anything one offer sells or asks for.</summary>
        public const int MaxAmount = 1000;

        /// <summary>What a price is usually asked in, in the order the owner's button steps through them.</summary>
        public static readonly string[] Currencies =
        {
            ItemIds.Scrap, ItemIds.MetalFragments, ItemIds.Wood, ItemIds.Stone, ItemIds.Cloth,
            ItemIds.Sulfur, ItemIds.Gunpowder, ItemIds.LowGradeFuel,
        };

        /// <summary>Whether an offer may be set: both items real and different, the amounts in range.</summary>
        public static bool Valid(VendOrder o, ItemCatalog catalog) =>
            !o.IsEmpty && o.Price != null && o.Sell != o.Price && catalog.Contains(o.Sell) && catalog.Contains(o.Price)
            && o.SellAmount >= 1 && o.SellAmount <= MaxAmount && o.PriceAmount >= 1 && o.PriceAmount <= MaxAmount;

        /// <summary>How many times an offer can still be bought from what the machine holds.</summary>
        public static int Stock(Inventory machine, VendOrder o) =>
            machine == null || o.IsEmpty || o.SellAmount <= 0 ? 0 : machine.Count(o.Sell) / o.SellAmount;

        /// <summary>Whether a purchase would go through, changing nothing (see <see cref="Buy"/>).</summary>
        public static ActionResult CanBuy(Inventory machine, Inventory buyer, VendOrder o)
        {
            if (machine == null || o.IsEmpty || o.Price == null) return ActionResult.Invalid;
            if (Stock(machine, o) < 1) return ActionResult.OutOfStock;
            if (!buyer.Has(o.Price, o.PriceAmount)) return ActionResult.NotEnoughResources;
            if (buyer.SpaceFor(o.Sell) < o.SellAmount) return ActionResult.NoSpace;
            if (machine.SpaceFor(o.Price) < o.PriceAmount) return ActionResult.ShopFull;
            return ActionResult.Ok;
        }

        /// <summary>
        /// One purchase: checks there is stock, that the buyer can pay and has room for the goods, and
        /// that the machine has room for the price; then swaps them. All or nothing.
        /// </summary>
        public static ActionResult Buy(Inventory machine, Inventory buyer, VendOrder o)
        {
            var r = CanBuy(machine, buyer, o);
            if (r != ActionResult.Ok) return r;
            buyer.Remove(o.Price, o.PriceAmount);
            machine.Remove(o.Sell, o.SellAmount);
            machine.Add(o.Price, o.PriceAmount);
            buyer.Add(o.Sell, o.SellAmount);
            return ActionResult.Ok;
        }
    }
}
