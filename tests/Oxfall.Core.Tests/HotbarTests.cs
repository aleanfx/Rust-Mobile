using System;
using NUnit.Framework;
using Oxfall.Core.Items;

namespace Oxfall.Core.Tests
{
    public class HotbarTests
    {
        private Inventory _inv;
        private Hotbar _hotbar;

        [SetUp]
        public void SetUp()
        {
            _inv = new Inventory(ItemCatalog.CreateDefault());
            _hotbar = new Hotbar(_inv);
        }

        [Test]
        public void Starts_on_the_first_slot_and_holds_what_is_there()
        {
            _inv.Add(ItemIds.Rock, 1);
            Assert.That(_hotbar.Selected, Is.EqualTo(0));
            Assert.That(_hotbar.Held.ItemId, Is.EqualTo(ItemIds.Rock));
            Assert.That(_hotbar.HeldTool, Is.EqualTo(ToolKind.Rock));
            Assert.That(_hotbar.IsHolding(ItemIds.Rock), Is.True);
        }

        [Test]
        public void An_empty_slot_means_bare_hands()
        {
            _hotbar.Select(3);
            Assert.That(_hotbar.Held.IsEmpty, Is.True);
            Assert.That(_hotbar.HeldDefinition, Is.Null);
            Assert.That(_hotbar.HeldTool, Is.EqualTo(ToolKind.None));
        }

        [Test]
        public void Choosing_the_chosen_slot_again_empties_the_hands()
        {
            _inv.Add(ItemIds.Rock, 1);
            _hotbar.Select(0);
            Assert.That(_hotbar.Selected, Is.EqualTo(Hotbar.Holstered));
            Assert.That(_hotbar.Held.IsEmpty, Is.True);
            _hotbar.Select(0);
            Assert.That(_hotbar.Held.ItemId, Is.EqualTo(ItemIds.Rock));
        }

        [Test]
        public void What_is_held_follows_the_slot_not_the_item()
        {
            _inv.Add(ItemIds.Rock, 1);          // slot 0
            _inv.Add(ItemIds.StoneHatchet, 1);  // slot 1
            _inv.Move(1, 0);                    // swap
            Assert.That(_hotbar.Held.ItemId, Is.EqualTo(ItemIds.StoneHatchet));
            Assert.That(_hotbar.HeldTool, Is.EqualTo(ToolKind.Hatchet));
        }

        [Test]
        public void Resources_are_not_tools()
        {
            _inv.SetSlot(2, new ItemStack(ItemIds.Wood, 100));
            _hotbar.Select(2);
            Assert.That(_hotbar.HeldDefinition.Id, Is.EqualTo(ItemIds.Wood));
            Assert.That(_hotbar.HeldTool, Is.EqualTo(ToolKind.None));
        }

        [Test]
        public void Selection_changes_are_announced_once()
        {
            int calls = 0, last = -2;
            _hotbar.SelectionChanged += s => { calls++; last = s; };
            _hotbar.Select(4);
            Assert.That((calls, last), Is.EqualTo((1, 4)));
            _hotbar.Holster();
            _hotbar.Holster();
            Assert.That((calls, last), Is.EqualTo((2, Hotbar.Holstered)));
        }

        [Test]
        public void Select_item_finds_it_on_the_hotbar_only()
        {
            _inv.Add(ItemIds.Rock, 1);
            _inv.Add(ItemIds.BuildingPlan, 1);  // hotbar slot 1
            _inv.SetSlot(Inventory.HotbarSize, new ItemStack(ItemIds.Torch, 1));
            Assert.That(_hotbar.SelectItem(ItemIds.BuildingPlan), Is.True);
            Assert.That(_hotbar.Selected, Is.EqualTo(1));
            Assert.That(_hotbar.SelectItem(ItemIds.Torch), Is.False);
            Assert.That(_hotbar.Selected, Is.EqualTo(1));
        }

        [Test]
        public void Slots_outside_the_hotbar_are_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _hotbar.Select(Inventory.HotbarSize));
            Assert.Throws<ArgumentOutOfRangeException>(() => _hotbar.Select(-2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Hotbar(_inv, 7));
        }
    }

    public class QuickMoveTests
    {
        private Inventory _inv;

        [SetUp]
        public void SetUp() => _inv = new Inventory(ItemCatalog.CreateDefault());

        [Test]
        public void A_tool_goes_from_the_hotbar_to_the_first_free_backpack_slot()
        {
            _inv.Add(ItemIds.Rock, 1);
            Assert.That(_inv.QuickMove(0), Is.True);
            Assert.That(_inv[0].IsEmpty, Is.True);
            Assert.That(_inv[Inventory.HotbarSize].ItemId, Is.EqualTo(ItemIds.Rock));
        }

        [Test]
        public void A_backpack_stack_goes_to_the_hotbar()
        {
            _inv.SetSlot(10, new ItemStack(ItemIds.Torch, 1));
            Assert.That(_inv.QuickMove(10), Is.True);
            Assert.That(_inv[0].ItemId, Is.EqualTo(ItemIds.Torch));
            Assert.That(_inv[10].IsEmpty, Is.True);
        }

        [Test]
        public void It_merges_onto_matching_stacks_before_using_an_empty_slot()
        {
            _inv.SetSlot(2, new ItemStack(ItemIds.Wood, 900));
            _inv.SetSlot(8, new ItemStack(ItemIds.Wood, 300));
            Assert.That(_inv.QuickMove(8), Is.True);
            Assert.That(_inv[2].Amount, Is.EqualTo(1000));
            Assert.That(_inv[0].Amount, Is.EqualTo(200));
            Assert.That(_inv[8].IsEmpty, Is.True);
            Assert.That(_inv.Count(ItemIds.Wood), Is.EqualTo(1200));
        }

        [Test]
        public void What_does_not_fit_stays_behind()
        {
            for (int i = 0; i < Inventory.HotbarSize; i++) _inv.SetSlot(i, new ItemStack(ItemIds.Stone, 950));
            _inv.SetSlot(12, new ItemStack(ItemIds.Stone, 400));
            Assert.That(_inv.QuickMove(12), Is.True);
            Assert.That(_inv[12].Amount, Is.EqualTo(100));
            Assert.That(_inv.Count(ItemIds.Stone), Is.EqualTo(6 * 950 + 400));
        }

        [Test]
        public void Nothing_moves_when_the_other_side_is_full()
        {
            for (int i = Inventory.HotbarSize; i < _inv.SlotCount; i++) _inv.SetSlot(i, new ItemStack(ItemIds.Cloth, 1000));
            _inv.Add(ItemIds.Rock, 1);
            int changes = 0;
            _inv.Changed += _ => changes++;
            Assert.That(_inv.QuickMove(0), Is.False);
            Assert.That(_inv[0].ItemId, Is.EqualTo(ItemIds.Rock));
            Assert.That(changes, Is.EqualTo(0));
            Assert.That(_inv.QuickMove(1), Is.False);
        }
    }
}
