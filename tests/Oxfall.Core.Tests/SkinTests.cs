using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Tests
{
    /// <summary>Skins: bought with Oxcoins, worn by their item, shown to others, and kept through a wipe.</summary>
    public class SkinTests
    {
        private const int Seed = 20261115;
        private static readonly string[] Tokens = { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" };

        [Test]
        public void The_catalogue_has_unique_ids_for_real_items_and_prices_by_rarity()
        {
            var catalog = ItemCatalog.CreateDefault();
            Assert.AreEqual(SkinCatalog.All.Count, SkinCatalog.All.Select(s => s.Id).Distinct().Count());
            foreach (var skin in SkinCatalog.All)
            {
                Assert.IsTrue(catalog.TryGet(skin.ItemId, out _), skin.Id + " is for a real item");
                Assert.AreEqual(SkinCatalog.PriceOf(skin.Rarity), skin.Price);
                Assert.IsFalse(skin.Id.Contains("@"), "ids never hold the tag mark");
                Assert.Greater(skin.Tints.Count, 0);
            }
        }

        [Test]
        public void Buying_spends_coins_and_puts_the_skin_on()
        {
            var locker = new Locker(400);
            Assert.AreEqual(ActionResult.Ok, locker.Buy("rifle_carbon"));
            Assert.AreEqual(100, locker.Coins);
            Assert.AreEqual("rifle_carbon", locker.EquippedFor(ItemIds.SemiRifle));
            Assert.AreEqual(ActionResult.AlreadyOwned, locker.Buy("rifle_carbon"));
            Assert.AreEqual(ActionResult.NotEnoughCoins, locker.Buy("rifle_gold"));
            Assert.AreEqual(100, locker.Coins, "a failed buy costs nothing");
            Assert.AreEqual(ActionResult.Invalid, locker.Buy("no_such_skin"));
        }

        [Test]
        public void Only_owned_skins_of_that_item_can_be_worn_and_they_come_off()
        {
            var locker = new Locker(2000);
            locker.Buy("revolver_silver");
            locker.Buy("revolver_gold");
            Assert.AreEqual("revolver_gold", locker.EquippedFor(ItemIds.Revolver));
            Assert.AreEqual(ActionResult.Ok, locker.Equip(ItemIds.Revolver, "revolver_silver"));
            Assert.AreEqual("revolver_silver", locker.EquippedFor(ItemIds.Revolver));
            Assert.AreEqual(ActionResult.NotOwned, locker.Equip(ItemIds.SemiRifle, "rifle_gold"));
            Assert.AreEqual(ActionResult.Invalid, locker.Equip(ItemIds.SemiRifle, "revolver_silver"), "wrong item");
            Assert.AreEqual(ActionResult.Ok, locker.Equip(ItemIds.Revolver, ""));
            Assert.AreEqual("", locker.EquippedFor(ItemIds.Revolver));
        }

        [Test]
        public void Tags_carry_the_skin_with_the_item_id()
        {
            Assert.AreEqual("semi_auto_rifle@rifle_gold", SkinCatalog.Tag(ItemIds.SemiRifle, "rifle_gold"));
            Assert.AreEqual(ItemIds.SemiRifle, SkinCatalog.Tag(ItemIds.SemiRifle, ""));
            Assert.AreEqual(ItemIds.SemiRifle, SkinCatalog.Untag("semi_auto_rifle@rifle_gold", out var skin));
            Assert.AreEqual("rifle_gold", skin);
            Assert.AreEqual(ItemIds.Revolver, SkinCatalog.Untag(ItemIds.Revolver, out skin));
            Assert.AreEqual("", skin);
            Assert.AreEqual("", SkinCatalog.Untag("", out _));
        }

        [Test]
        public void Lockers_save_and_load_and_open_with_welcome_coins()
        {
            var lockers = new Lockers();
            var ana = lockers.For(Tokens[0]);
            Assert.AreEqual(SkinCatalog.WelcomeCoins, ana.Coins);
            ana.AddCoins(2000);
            ana.Buy("facemask_gold");
            ana.Buy("shirt_sand");
            lockers.For(Tokens[1]);

            var loaded = new Lockers();
            Assert.IsTrue(loaded.Load(lockers.Save(), out var why), why);
            Assert.AreEqual(2, loaded.Count);
            var back = loaded.For(Tokens[0]);
            Assert.AreEqual(ana.Coins, back.Coins);
            Assert.IsTrue(back.Owns("facemask_gold") && back.Owns("shirt_sand"));
            Assert.AreEqual("facemask_gold", back.EquippedFor(ItemIds.MetalFacemask));
            Assert.IsFalse(loaded.Load(new byte[] { 1, 2 }, out _), "junk is refused");
            Assert.AreEqual(2, loaded.Count, "and the lockers stay as they were");
        }

        [Test]
        public void The_server_sells_tags_and_keeps_lockers_through_a_wipe()
        {
            var world = new ServerWorld(Seed);
            world.Join(Protocol.Version, "Ana", Tokens[0], 0f, out var ana, out _, out _);
            Assert.AreEqual(ActionResult.Ok, world.ShopAct(ana.Id, ShopOp.Buy, "", "shirt_midnight"));
            Assert.IsTrue(world.LockerNews.Contains(ana.Id));
            Assert.IsTrue(world.Lockers.Dirty);

            var worn = new[] { "", ItemIds.BurlapShirt, ItemIds.BurlapTrousers, "" };
            world.TagWorn(ana.Id, worn);
            Assert.AreEqual("burlap_shirt@shirt_midnight", worn[1]);
            Assert.AreEqual(ItemIds.BurlapTrousers, worn[2]);
            Assert.AreEqual("", worn[0]);
            Assert.AreEqual(ItemIds.Revolver, world.TagHeld(ana.Id, ItemIds.Revolver));

            // A wipe is a fresh island; the lockers file comes back into it.
            var saved = world.Lockers.Save();
            var wiped = new ServerWorld(Seed);
            Assert.IsTrue(wiped.Lockers.Load(saved, out _));
            wiped.Join(Protocol.Version, "Ana", Tokens[0], 0f, out var again, out _, out _);
            Assert.IsTrue(wiped.TryGetLocker(again.Id, out var locker));
            Assert.IsTrue(locker.Owns("shirt_midnight"));
            Assert.AreEqual(SkinCatalog.WelcomeCoins - 300, locker.Coins);
        }

        [Test]
        public void Test_coins_need_dev_mode()
        {
            var world = new ServerWorld(Seed);
            world.Join(Protocol.Version, "Ana", Tokens[0], 0f, out var ana, out _, out _);
            Assert.AreNotEqual(ActionResult.Ok, world.Act(ana.Id, ActKind.DevGive, ServerWorld.DevCoinsKit, 0, null));
            world.DevMode = true;
            Assert.AreEqual(ActionResult.Ok, world.Act(ana.Id, ActKind.DevGive, ServerWorld.DevCoinsKit, 0, null));
            world.TryGetLocker(ana.Id, out var locker);
            Assert.AreEqual(SkinCatalog.WelcomeCoins + 1000, locker.Coins);
        }
    }
}
