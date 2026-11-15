using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>The vending machine: its owner stocks it and sets offers; anyone buys with a tap; nobody else reaches in.</summary>
    public class VendingTests
    {
        private const int Seed = 20261114;
        private const int Owner = 1, Buyer = 2;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        private static (Settlement, Deployable, Survivor, Survivor) Shop()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog);
            var m = w.Add(new Deployable(0, DeployableKind.VendingMachine, 0f, 1f, 0f, 0f, Owner));
            var owner = new Survivor(w.Catalog);
            owner.Inventory.Clear();
            var buyer = new Survivor(w.Catalog);
            buyer.Inventory.Clear();
            return (w, m, owner, buyer);
        }

        private static int SlotOf(Survivor s, string item)
        {
            for (int i = 0; i < s.Inventory.SlotCount; i++)
                if (s.Inventory[i].ItemId == item) return i;
            return -1;
        }

        [Test]
        public void It_is_made_at_the_workbench_with_no_blueprint()
        {
            Assert.IsTrue(RecipeBook.CreateDefault(Catalog).TryGet("vending_machine", out var r));
            Assert.IsTrue(r.NeedsWorkbench);
            Assert.AreEqual(0, r.ScrapToLearn);
            Assert.IsTrue(Deployables.TryKindOf(ItemIds.VendingMachine, out var kind));
            Assert.AreEqual(DeployableKind.VendingMachine, kind);
            Assert.AreEqual(Vending.Slots, Deployables.Slots(kind));
        }

        [Test]
        public void The_owner_stocks_it_and_sets_offers_a_stranger_buys_but_cannot_reach_in()
        {
            var (w, m, owner, buyer) = Shop();
            owner.Inventory.Add(ItemIds.Wood, 250);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, owner, Owner, m.Id, 1f, 1f, 0f));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Put(w, owner, SlotOf(owner, ItemIds.Wood), -1));
            var offer = new VendOrder(ItemIds.Wood, 100, ItemIds.Scrap, 10);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, owner, Owner, BuildOp.SetOrder(m.Id, 0, offer), 1f, 1f, 0f, out _));
            Assert.AreEqual(offer, w.Orders(m.Id)[0]);
            Assert.AreEqual(2, Vending.Stock(w.Container(m.Id), offer));

            // A stranger may not set offers or take from it.
            Assert.AreEqual(ActionResult.NotYours, PlayerActions.Build(w, buyer, Buyer, BuildOp.SetOrder(m.Id, 1, offer), 1f, 1f, 0f, out _));
            buyer.Inventory.Add(ItemIds.Scrap, 25);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, buyer, Buyer, m.Id, 1f, 1f, 0f));
            Assert.AreEqual(ActionResult.NotYours, PlayerActions.Take(w, buyer, 0, -1));
            Assert.AreEqual(ActionResult.NotYours, PlayerActions.Put(w, buyer, SlotOf(buyer, ItemIds.Scrap), -1));
            Assert.AreEqual(ActionResult.NotYours, PlayerActions.TakeAll(w, buyer));

            // Buying: twice, then out of stock.
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, buyer, Buyer, BuildOp.Vend(m.Id, 0), 1f, 1f, 0f, out _));
            Assert.AreEqual(100, buyer.Inventory.Count(ItemIds.Wood));
            Assert.AreEqual(15, buyer.Inventory.Count(ItemIds.Scrap));
            Assert.AreEqual(10, w.Container(m.Id).Count(ItemIds.Scrap));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, buyer, Buyer, BuildOp.Vend(m.Id, 0), 1f, 1f, 0f, out _));
            Assert.AreEqual(ActionResult.OutOfStock, PlayerActions.Build(w, buyer, Buyer, BuildOp.Vend(m.Id, 0), 1f, 1f, 0f, out _));
            Assert.AreEqual(50, w.Container(m.Id).Count(ItemIds.Wood));
            Assert.AreEqual(5, buyer.Inventory.Count(ItemIds.Scrap));

            // The owner collects the takings.
            PlayerActions.Close(buyer);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, owner, Owner, m.Id, 1f, 1f, 0f));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.TakeAll(w, owner));
            Assert.AreEqual(20, owner.Inventory.Count(ItemIds.Scrap));
        }

        [Test]
        public void A_buyer_who_cannot_pay_gets_nothing_and_nobody_reaches_from_afar()
        {
            var (w, m, owner, buyer) = Shop();
            w.Container(m.Id).Add(ItemIds.Wood, 100);
            w.SetOrder(m.Id, 2, new VendOrder(ItemIds.Wood, 100, ItemIds.MetalFragments, 50));
            buyer.Inventory.Add(ItemIds.MetalFragments, 49);
            Assert.AreEqual(ActionResult.NotEnoughResources, PlayerActions.Build(w, buyer, Buyer, BuildOp.Vend(m.Id, 2), 1f, 1f, 0f, out _));
            Assert.AreEqual(49, buyer.Inventory.Count(ItemIds.MetalFragments));
            Assert.AreEqual(100, w.Container(m.Id).Count(ItemIds.Wood));
            buyer.Inventory.Add(ItemIds.MetalFragments, 1);
            Assert.AreEqual(ActionResult.TooFar, PlayerActions.Build(w, buyer, Buyer, BuildOp.Vend(m.Id, 2), 30f, 1f, 0f, out _));
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Build(w, buyer, Buyer, BuildOp.Vend(m.Id, 0), 1f, 1f, 0f, out _), "no offer there");
            // The same thing for itself, or nothing, is not an offer.
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Build(w, owner, Owner,
                BuildOp.SetOrder(m.Id, 1, new VendOrder(ItemIds.Wood, 1, ItemIds.Wood, 1)), 1f, 1f, 0f, out _));
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Build(w, owner, Owner,
                BuildOp.SetOrder(m.Id, 1, new VendOrder(ItemIds.Wood, 0, ItemIds.Scrap, 1)), 1f, 1f, 0f, out _));
        }

        [Test]
        public void Offers_go_over_the_wire_and_come_back_for_a_new_copy()
        {
            var (w, m, _, _) = Shop();
            var offer = new VendOrder(ItemIds.Cloth, 20, ItemIds.Scrap, 5);
            w.SetOrder(m.Id, 3, offer);
            var ops = BuildOps.Describe(w);
            var copy = new Settlement(new IslandGround(Seed), Catalog);
            foreach (var op in ops)
            {
                var wr = new NetWriter(256);
                op.Write(wr);
                var back = BuildOp.Read(new NetReader(wr.ToArray()));
                Assert.IsTrue(BuildOps.Apply(copy, back), back.ToString());
            }
            Assert.AreEqual(offer, copy.Orders(m.Id)[3]);
            Assert.IsTrue(copy.Orders(m.Id)[0].IsEmpty);

            // Clearing an offer goes over too.
            var wc = new NetWriter(64);
            BuildOp.SetOrder(m.Id, 3, VendOrder.None).Write(wc);
            Assert.IsTrue(BuildOps.Apply(copy, BuildOp.Read(new NetReader(wc.ToArray()))));
            Assert.IsTrue(copy.Orders(m.Id)[3].IsEmpty);
        }

        [Test]
        public void On_the_server_a_stranger_buys_and_the_stock_shows_in_the_open_machine()
        {
            var world = new ServerWorld(Seed);
            var a = world.Join(Protocol.Version, "Ana", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var ana, out _, out _);
            var b = world.Join(Protocol.Version, "Beto", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 0f, out var beto, out _, out _);
            var at = ana.Pose;
            var m = world.Settlement.Add(new Deployable(0, DeployableKind.VendingMachine, at.X + 1f, at.Y, at.Z, 0f, a.Owner));
            world.Settlement.Container(m.Id).Add(ItemIds.Stone, 300);
            world.Players.Place(ana.Id, new Pose(at.X, at.Y, at.Z, 0f), 0f);
            world.Players.Place(beto.Id, new Pose(at.X + 2f, at.Y, at.Z, 0f), 0f);
            Assert.AreEqual(ActionResult.Ok, world.Build(ana.Id, BuildOp.SetOrder(m.Id, 0, new VendOrder(ItemIds.Stone, 100, ItemIds.Scrap, 3)), out _));
            Assert.AreEqual(ActionResult.NotYours, world.Build(beto.Id, BuildOp.SetOrder(m.Id, 0, VendOrder.None), out _));
            b.Survivor.Inventory.Add(ItemIds.Scrap, 3);
            int stone = b.Survivor.Inventory.Count(ItemIds.Stone);
            Assert.AreEqual(ActionResult.Ok, world.Build(beto.Id, BuildOp.Vend(m.Id, 0), out var done));
            Assert.AreEqual(BuildOpKind.Vend, done.Kind);
            Assert.AreEqual(stone + 100, b.Survivor.Inventory.Count(ItemIds.Stone));
            Assert.AreEqual(3, world.Settlement.Container(m.Id).Count(ItemIds.Scrap));
        }
    }
}
