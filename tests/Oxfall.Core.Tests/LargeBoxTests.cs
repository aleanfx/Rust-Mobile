using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>The large wooden box: twice the room, crafted from wood and some metal, lockable, picked back up empty.</summary>
    public class LargeBoxTests
    {
        private const int Seed = 20261114;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        [Test]
        public void It_holds_twice_a_small_box_and_takes_a_lock()
        {
            Assert.AreEqual(2 * Deployables.Slots(DeployableKind.StorageBox), Deployables.Slots(DeployableKind.LargeBox));
            Assert.IsTrue(CodeLocks.Lockable(DeployableKind.LargeBox));
            Assert.IsTrue(RecipeBook.CreateDefault(Catalog).TryGet("large_wood_box", out var r));
            Assert.AreEqual(ItemIds.LargeBox, r.OutputItemId);
            Assert.IsTrue(Deployables.TryKindOf(ItemIds.LargeBox, out var kind));
            Assert.AreEqual(DeployableKind.LargeBox, kind);
        }

        [Test]
        public void Things_go_into_its_last_slot_and_it_comes_back_empty()
        {
            var w = new Settlement(new IslandGround(Seed), Catalog);
            var box = w.Add(new Deployable(0, DeployableKind.LargeBox, 0f, 1f, 0f, 0f, 1));
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.Wood, 100);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, s, 1, box.Id, 1f, 1f, 0f));
            int wood = -1;
            for (int i = 0; i < s.Inventory.SlotCount; i++) if (s.Inventory[i].ItemId == ItemIds.Wood) wood = i;
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Put(w, s, wood, 23));
            Assert.AreEqual(100, w.Container(box.Id)[23].Amount);
            Assert.AreEqual(ActionResult.NotEmpty, PlayerActions.Build(w, s, 1, BuildOp.Pickup(box.Id), 1f, 1f, 0f, out _));
        }
    }
}
