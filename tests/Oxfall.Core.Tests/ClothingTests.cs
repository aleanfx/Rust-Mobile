using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Tests
{
    public class ClothingTests
    {
        private const int Seed = 20261113;

        private static Survivor Fresh() => new Survivor(ItemCatalog.CreateDefault());

        [Test]
        public void ClothingIsMadeFromClothAndLeather()
        {
            var catalog = ItemCatalog.CreateDefault();
            var book = RecipeBook.CreateDefault(catalog);
            foreach (var id in new[] { ItemIds.BurlapHeadwrap, ItemIds.BurlapShirt, ItemIds.BurlapTrousers,
                         ItemIds.HideHood, ItemIds.HideVest, ItemIds.HidePants, ItemIds.HideBoots })
            {
                Assert.IsTrue(book.TryGet(id, out var recipe), id);
                var def = catalog.Get(id);
                Assert.AreEqual(ItemCategory.Attire, def.Category, id);
                Assert.AreNotEqual(WearSlot.None, def.Wear, id);
                Assert.Greater(def.Protection, 0f, id);
            }
            Assert.Greater(catalog.Get(ItemIds.HideVest).Protection, catalog.Get(ItemIds.BurlapShirt).Protection, "hide beats burlap");
        }

        [Test]
        public void ClothesGoOnlyWhereTheyBelongAndNeverFillUpByThemselves()
        {
            var s = Fresh();
            var inv = s.Inventory;
            Assert.AreEqual(Inventory.PlayerSlotCount, inv.SlotCount);
            Assert.AreEqual(Inventory.DefaultSlotCount, inv.CarrySlots);
            int chest = inv.SlotFor(WearSlot.Chest), feet = inv.SlotFor(WearSlot.Feet);
            Assert.AreEqual(WearSlot.Chest, inv.WearOf(chest));

            inv.Add(ItemIds.Wood, 30000);
            for (int i = inv.CarrySlots; i < inv.SlotCount; i++) Assert.IsTrue(inv[i].IsEmpty, "wood never lands in a wear slot");
            inv.Clear();

            inv.Add(ItemIds.HideVest, 1);
            inv.Add(ItemIds.Wood, 10);
            int vest = SlotOf(inv, ItemIds.HideVest), wood = SlotOf(inv, ItemIds.Wood);
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.MoveSlot(s, wood, chest), "wood is not worn");
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.MoveSlot(s, vest, feet), "a vest is not boots");
            Assert.AreEqual(ActionResult.Ok, PlayerActions.MoveSlot(s, vest, chest));
            Assert.AreEqual(ItemIds.HideVest, inv[chest].ItemId);
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.MoveSlot(s, chest, wood), "swapping wood onto the chest is refused");
            Assert.AreEqual(ItemIds.HideVest, inv[chest].ItemId);
        }

        [Test]
        public void WearingSwapsWithWhatWasWornAndTakingOffGoesToTheBackpack()
        {
            var s = Fresh();
            var inv = s.Inventory;
            inv.Add(ItemIds.BurlapShirt, 1);
            inv.Add(ItemIds.HideVest, 1);
            int chest = inv.SlotFor(WearSlot.Chest);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Wear(s, SlotOf(inv, ItemIds.BurlapShirt)));
            Assert.AreEqual(ItemIds.BurlapShirt, inv[chest].ItemId);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Wear(s, SlotOf(inv, ItemIds.HideVest)));
            Assert.AreEqual(ItemIds.HideVest, inv[chest].ItemId, "the vest goes on");
            Assert.AreEqual(1, inv.CountCarried(ItemIds.BurlapShirt), "the shirt comes back to where the vest was");
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Wear(s, SlotOf(inv, ItemIds.Rock)), "a rock is not worn");

            Assert.AreEqual(ActionResult.Ok, PlayerActions.Wear(s, chest), "taken off");
            Assert.IsTrue(inv[chest].IsEmpty);
            Assert.GreaterOrEqual(SlotOf(inv, ItemIds.HideVest), Inventory.HotbarSize, "into the backpack");
        }

        [Test]
        public void WhatIsWornTakesItsShareOfEveryHit()
        {
            var s = Fresh();
            Assert.AreEqual(0f, s.Inventory.Protection);
            foreach (var id in new[] { ItemIds.HideHood, ItemIds.HideVest, ItemIds.HidePants, ItemIds.HideBoots })
            {
                s.Inventory.Add(id, 1);
                Assert.AreEqual(ActionResult.Ok, PlayerActions.Wear(s, SlotOf(s.Inventory, id)));
            }
            Assert.AreEqual(0.38f, s.Inventory.Protection, 1e-4f, "a full hide set");
            float done = s.TakeHit(50f);
            Assert.AreEqual(31f, done, 1e-3f);
            Assert.AreEqual(69f, s.Vitals.Health, 1e-3f);
        }

        [Test]
        public void ArmourCountsOnTheServerAndFallsIntoTheBagOnDeath()
        {
            var world = new ServerWorld(Seed);
            var a = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var pa, out _, out _);
            var b = world.Join(Protocol.Version, "Bea", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 0f, out var pb, out _, out _);
            world.Players.Place(pb.Id, new Pose(pa.Pose.X + 1.5f, pa.Pose.Y, pa.Pose.Z, 0f), 0f);
            a.Survivor.Inventory.Add(ItemIds.WoodenSpear, 1);
            b.Survivor.Inventory.Add(ItemIds.HideVest, 1);
            Assert.AreEqual(ActionResult.Ok, world.Act(pb.Id, ActKind.Wear, SlotOf(b.Survivor.Inventory, ItemIds.HideVest), 0, null));
            Assert.IsTrue(world.Strike(pa.Id, pb.Id, ToolKind.Spear, 1f, out float damage, out _));
            Assert.AreEqual(Combat.MeleeDamage(ToolKind.Spear) * (1f - 0.14f), damage, 1e-3f, "the vest takes its share");

            float now = 2f;
            bool killed = false;
            while (!killed) { world.Strike(pa.Id, pb.Id, ToolKind.Spear, now, out _, out killed); now += 1f; }
            Assert.IsTrue(b.Survivor.Inventory.IsEmpty, "everything went into the bag, clothes too");

            var worn = Messages.Worn(a.Survivor.Inventory);
            Assert.AreEqual(4, worn.Length);
            var w = new NetWriter();
            Messages.Outfit(w, 3, new[] { ItemIds.HideHood, "", ItemIds.HidePants, "" });
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Outfit, (MessageType)r.Byte());
            var back = new string[4];
            Messages.ReadOutfit(r, out ushort who, back);
            Assert.AreEqual((ushort)3, who);
            CollectionAssert.AreEqual(new[] { ItemIds.HideHood, "", ItemIds.HidePants, "" }, back);
        }

        private static int SlotOf(Inventory inv, string id)
        {
            for (int i = 0; i < inv.SlotCount; i++)
                if (inv[i].ItemId == id) return i;
            return -1;
        }
    }
}
