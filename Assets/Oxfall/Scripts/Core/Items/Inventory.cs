using System;
using System.Collections.Generic;

namespace Oxfall.Core.Items
{
    /// <summary>
    /// A fixed number of slots. The first <see cref="HotbarSize"/> slots are the hotbar.
    /// All changes go through methods that either complete fully or change nothing, so the
    /// server can apply a client's request and trust the result.
    /// </summary>
    public sealed class Inventory
    {
        public const int DefaultSlotCount = 30;
        public const int HotbarSize = 6;

        private readonly ItemStack[] _slots;
        private readonly ItemCatalog _catalog;

        /// <summary>Raised after any change. The argument is the slot index, or -1 when several slots changed.</summary>
        public event Action<int> Changed;

        public int SlotCount => _slots.Length;
        public ItemCatalog Catalog => _catalog;

        public Inventory(ItemCatalog catalog, int slotCount = DefaultSlotCount)
        {
            if (slotCount < HotbarSize) throw new ArgumentOutOfRangeException(nameof(slotCount), $"Need at least {HotbarSize} slots.");
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _slots = new ItemStack[slotCount];
        }

        public ItemStack this[int slot] => _slots[CheckSlot(slot)];

        public int Count(string itemId)
        {
            int total = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (!_slots[i].IsEmpty && _slots[i].ItemId == itemId) total += _slots[i].Amount;
            return total;
        }

        public bool Has(string itemId, int amount) => Count(itemId) >= amount;

        /// <summary>How many of this item would fit right now.</summary>
        public int SpaceFor(string itemId, int skinId = 0)
        {
            var def = _catalog.Get(itemId);
            int space = 0;
            var probe = new ItemStack(itemId, 1, skinId);
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].IsEmpty) space += def.MaxStack;
                else if (_slots[i].CanMergeWith(probe)) space += def.MaxStack - _slots[i].Amount;
            }
            return space;
        }

        /// <summary>
        /// Adds as much as fits: first onto matching stacks, then into empty slots. Tools and
        /// placeables prefer the hotbar; resources prefer the backpack. Returns the amount that did not fit.
        /// </summary>
        public int Add(string itemId, int amount, int skinId = 0)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0) return 0;
            var def = _catalog.Get(itemId);
            var probe = new ItemStack(itemId, 1, skinId);
            int remaining = amount;
            bool changed = false;

            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (!_slots[i].CanMergeWith(probe)) continue;
                int room = def.MaxStack - _slots[i].Amount;
                if (room <= 0) continue;
                int put = Math.Min(room, remaining);
                _slots[i] = _slots[i].WithAmount(_slots[i].Amount + put);
                remaining -= put;
                changed = true;
            }

            foreach (int i in EmptySlotOrder(def.Category))
            {
                if (remaining <= 0) break;
                if (!_slots[i].IsEmpty) continue;
                int put = Math.Min(def.MaxStack, remaining);
                _slots[i] = new ItemStack(itemId, put, skinId);
                remaining -= put;
                changed = true;
            }

            if (changed) Changed?.Invoke(-1);
            return remaining;
        }

        /// <summary>Removes exactly <paramref name="amount"/> or nothing. Takes from the backpack before the hotbar.</summary>
        public bool Remove(string itemId, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0) return true;
            if (Count(itemId) < amount) return false;

            int remaining = amount;
            for (int pass = 0; pass < 2 && remaining > 0; pass++)
            {
                int from = pass == 0 ? HotbarSize : 0;
                int to = pass == 0 ? _slots.Length : HotbarSize;
                for (int i = to - 1; i >= from && remaining > 0; i--)
                {
                    if (_slots[i].IsEmpty || _slots[i].ItemId != itemId) continue;
                    int take = Math.Min(_slots[i].Amount, remaining);
                    _slots[i] = _slots[i].WithAmount(_slots[i].Amount - take);
                    remaining -= take;
                }
            }

            Changed?.Invoke(-1);
            return true;
        }

        /// <summary>Removes up to <paramref name="amount"/> from one slot and returns what was taken.</summary>
        public ItemStack TakeFromSlot(int slot, int amount)
        {
            CheckSlot(slot);
            if (amount <= 0 || _slots[slot].IsEmpty) return ItemStack.Empty;
            int take = Math.Min(amount, _slots[slot].Amount);
            var taken = _slots[slot].WithAmount(take);
            _slots[slot] = _slots[slot].WithAmount(_slots[slot].Amount - take);
            Changed?.Invoke(slot);
            return taken;
        }

        /// <summary>Moves a slot onto another: merges when it can, otherwise swaps.</summary>
        public void Move(int from, int to)
        {
            CheckSlot(from);
            CheckSlot(to);
            if (from == to || _slots[from].IsEmpty) return;

            if (_slots[to].CanMergeWith(_slots[from]))
            {
                int max = _catalog.Get(_slots[to].ItemId).MaxStack;
                int put = Math.Min(max - _slots[to].Amount, _slots[from].Amount);
                if (put > 0)
                {
                    _slots[to] = _slots[to].WithAmount(_slots[to].Amount + put);
                    _slots[from] = _slots[from].WithAmount(_slots[from].Amount - put);
                    Changed?.Invoke(-1);
                    return;
                }
            }

            var tmp = _slots[to];
            _slots[to] = _slots[from];
            _slots[from] = tmp;
            Changed?.Invoke(-1);
        }

        /// <summary>Whether a slot is one of the hotbar's.</summary>
        public static bool IsHotbarSlot(int slot) => slot >= 0 && slot < HotbarSize;

        /// <summary>
        /// Sends a stack across, from the hotbar to the backpack or back: onto matching stacks there
        /// first, then into the first empty slot. Whatever doesn't fit stays where it was. Returns
        /// true if anything moved.
        /// </summary>
        public bool QuickMove(int slot)
        {
            CheckSlot(slot);
            var stack = _slots[slot];
            if (stack.IsEmpty) return false;
            int from = IsHotbarSlot(slot) ? HotbarSize : 0;
            int to = IsHotbarSlot(slot) ? _slots.Length : HotbarSize;
            int max = _catalog.Get(stack.ItemId).MaxStack;
            int remaining = stack.Amount;

            for (int i = from; i < to && remaining > 0; i++)
            {
                if (!_slots[i].CanMergeWith(stack)) continue;
                int put = Math.Min(max - _slots[i].Amount, remaining);
                if (put <= 0) continue;
                _slots[i] = _slots[i].WithAmount(_slots[i].Amount + put);
                remaining -= put;
            }
            for (int i = from; i < to && remaining > 0; i++)
            {
                if (!_slots[i].IsEmpty) continue;
                _slots[i] = stack.WithAmount(remaining);
                remaining = 0;
            }

            if (remaining == stack.Amount) return false;
            _slots[slot] = stack.WithAmount(remaining);
            Changed?.Invoke(-1);
            return true;
        }

        /// <summary>Moves half of a stack (rounded down) into the first empty slot. Returns the new slot or -1.</summary>
        public int Split(int slot)
        {
            CheckSlot(slot);
            var stack = _slots[slot];
            if (stack.IsEmpty || stack.Amount < 2) return -1;
            int target = Array.FindIndex(_slots, s => s.IsEmpty);
            if (target < 0) return -1;
            int half = stack.Amount / 2;
            _slots[slot] = stack.WithAmount(stack.Amount - half);
            _slots[target] = stack.WithAmount(half);
            Changed?.Invoke(-1);
            return target;
        }

        /// <summary>Puts a stack into a slot, replacing what was there. For loading saves and server snapshots.</summary>
        public void SetSlot(int slot, ItemStack stack)
        {
            CheckSlot(slot);
            if (!stack.IsEmpty)
            {
                var def = _catalog.Get(stack.ItemId);
                if (stack.Amount > def.MaxStack)
                    throw new ArgumentException($"{stack.ItemId} stacks to {def.MaxStack}, got {stack.Amount}.", nameof(stack));
            }
            _slots[slot] = stack;
            Changed?.Invoke(slot);
        }

        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
            Changed?.Invoke(-1);
        }

        private IEnumerable<int> EmptySlotOrder(ItemCategory category)
        {
            bool hotbarFirst = category != ItemCategory.Resource;
            if (hotbarFirst)
            {
                for (int i = 0; i < _slots.Length; i++) yield return i;
            }
            else
            {
                for (int i = HotbarSize; i < _slots.Length; i++) yield return i;
                for (int i = 0; i < HotbarSize; i++) yield return i;
            }
        }

        private int CheckSlot(int slot)
        {
            if (slot < 0 || slot >= _slots.Length) throw new ArgumentOutOfRangeException(nameof(slot));
            return slot;
        }
    }
}
