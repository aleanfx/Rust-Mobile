using NUnit.Framework;
using Oxfall.Core.Items;

namespace Oxfall.Core.Tests
{
    public class InventoryTests
    {
        private ItemCatalog _catalog;
        private Inventory _inv;

        [SetUp]
        public void SetUp()
        {
            _catalog = ItemCatalog.CreateDefault();
            _inv = new Inventory(_catalog);
        }

        [Test]
        public void Resources_go_to_the_backpack_before_the_hotbar()
        {
            Assert.That(_inv.Add(ItemIds.Wood, 50), Is.EqualTo(0));
            Assert.That(_inv[Inventory.HotbarSize].ItemId, Is.EqualTo(ItemIds.Wood));
            for (int i = 0; i < Inventory.HotbarSize; i++) Assert.That(_inv[i].IsEmpty, Is.True);
        }

        [Test]
        public void Tools_go_to_the_hotbar_first()
        {
            _inv.Add(ItemIds.Rock, 1);
            Assert.That(_inv[0].ItemId, Is.EqualTo(ItemIds.Rock));
        }

        [Test]
        public void Adding_fills_existing_stacks_then_splits_at_max_stack()
        {
            _inv.Add(ItemIds.Wood, 900);
            _inv.Add(ItemIds.Wood, 300);
            Assert.That(_inv.Count(ItemIds.Wood), Is.EqualTo(1200));
            Assert.That(_inv[Inventory.HotbarSize].Amount, Is.EqualTo(1000));
            Assert.That(_inv[Inventory.HotbarSize + 1].Amount, Is.EqualTo(200));
        }

        [Test]
        public void Tools_never_stack()
        {
            _inv.Add(ItemIds.StoneHatchet, 2);
            Assert.That(_inv[0].Amount, Is.EqualTo(1));
            Assert.That(_inv[1].Amount, Is.EqualTo(1));
        }

        [Test]
        public void Overflow_is_returned_when_full()
        {
            var small = new Inventory(_catalog, Inventory.HotbarSize);
            int left = small.Add(ItemIds.Stone, 7000);
            Assert.That(left, Is.EqualTo(1000));
            Assert.That(small.Count(ItemIds.Stone), Is.EqualTo(6000));
        }

        [Test]
        public void Remove_is_all_or_nothing()
        {
            _inv.Add(ItemIds.Wood, 100);
            Assert.That(_inv.Remove(ItemIds.Wood, 150), Is.False);
            Assert.That(_inv.Count(ItemIds.Wood), Is.EqualTo(100));
            Assert.That(_inv.Remove(ItemIds.Wood, 60), Is.True);
            Assert.That(_inv.Count(ItemIds.Wood), Is.EqualTo(40));
        }

        [Test]
        public void Remove_takes_from_several_stacks()
        {
            _inv.Add(ItemIds.Wood, 1500);
            Assert.That(_inv.Remove(ItemIds.Wood, 1200), Is.True);
            Assert.That(_inv.Count(ItemIds.Wood), Is.EqualTo(300));
        }

        [Test]
        public void Stacks_with_different_skins_do_not_merge()
        {
            _inv.Add(ItemIds.Campfire, 1, skinId: 0);
            _inv.Add(ItemIds.Campfire, 1, skinId: 7);
            Assert.That(_inv[0].SkinId, Is.EqualTo(0));
            Assert.That(_inv[1].SkinId, Is.EqualTo(7));
            Assert.That(_inv.Count(ItemIds.Campfire), Is.EqualTo(2));
        }

        [Test]
        public void Move_merges_matching_stacks_and_swaps_others()
        {
            _inv.SetSlot(10, new ItemStack(ItemIds.Wood, 600));
            _inv.SetSlot(11, new ItemStack(ItemIds.Wood, 600));
            _inv.Move(10, 11);
            Assert.That(_inv[11].Amount, Is.EqualTo(1000));
            Assert.That(_inv[10].Amount, Is.EqualTo(200));

            _inv.SetSlot(12, new ItemStack(ItemIds.Stone, 5));
            _inv.Move(12, 10);
            Assert.That(_inv[10].ItemId, Is.EqualTo(ItemIds.Stone));
            Assert.That(_inv[12].ItemId, Is.EqualTo(ItemIds.Wood));
        }

        [Test]
        public void Split_moves_half_to_an_empty_slot()
        {
            _inv.SetSlot(8, new ItemStack(ItemIds.Stone, 9));
            int target = _inv.Split(8);
            Assert.That(target, Is.Not.EqualTo(-1));
            Assert.That(_inv[8].Amount, Is.EqualTo(5));
            Assert.That(_inv[target].Amount, Is.EqualTo(4));
        }

        [Test]
        public void SetSlot_rejects_more_than_a_stack()
        {
            Assert.Throws<System.ArgumentException>(() => _inv.SetSlot(0, new ItemStack(ItemIds.Rock, 2)));
        }

        [Test]
        public void Changed_fires_on_add()
        {
            int calls = 0;
            _inv.Changed += _ => calls++;
            _inv.Add(ItemIds.Cloth, 10);
            Assert.That(calls, Is.EqualTo(1));
        }
    }
}
