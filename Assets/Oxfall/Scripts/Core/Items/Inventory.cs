using System;
using System.Collections.Generic;

namespace Oxfall.Core.Items
{
    /// <summary>
    /// A fixed number of slots. The first <see cref="HotbarSize"/> slots are the hotbar; a player's
    /// last <see cref="WearSlots"/> are what they wear (head, chest, legs, feet), which only take
    /// clothing for that part of the body and are never filled by picking things up.
    /// All changes go through methods that either complete fully or change nothing, so the
    /// server can apply a client's request and trust the result.
    /// </summary>
    public sealed class Inventory
    {
        public const int DefaultSlotCount = 30;
        public const int HotbarSize = 6;
        public const int WearSlots = 4;

        /// <summary>A player's: the hotbar and backpack, then the four wear slots.</summary>
        public const int PlayerSlotCount = DefaultSlotCount + WearSlots;

        private readonly ItemStack[] _slots;
        private readonly ItemCatalog _catalog;

        /// <summary>Raised after any change. The argument is the slot index, or -1 when several slots changed.</summary>
        public event Action<int> Changed;

        public int SlotCount => _slots.Length;
        public ItemCatalog Catalog => _catalog;

        /// <param name="hasHotbar">A player's inventory (its first slots are the hotbar); a box's has none and fills from the top.</param>
        /// <param name="hasWear">A player's: its last <see cref="WearSlots"/> slots are worn clothing.</param>
        /// <param name="outputSlots">A machine's: its last slots only give out what it makes (nothing is put in them).</param>
        public Inventory(ItemCatalog catalog, int slotCount = DefaultSlotCount, bool hasHotbar = true, bool hasWear = false, int outputSlots = 0)
        {
            if (outputSlots < 0 || outputSlots >= slotCount || (outputSlots > 0 && (hasHotbar || hasWear))) throw new ArgumentOutOfRangeException(nameof(outputSlots));
            if (hasHotbar && slotCount < HotbarSize) throw new ArgumentOutOfRangeException(nameof(slotCount), $"Need at least {HotbarSize} slots.");
            if (hasWear && slotCount < HotbarSize + WearSlots) throw new ArgumentOutOfRangeException(nameof(slotCount));
            if (slotCount < 1) throw new ArgumentOutOfRangeException(nameof(slotCount));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _slots = new ItemStack[slotCount];
            HasHotbar = hasHotbar;
            HasWear = hasWear;
            CarrySlots = hasWear ? slotCount - WearSlots : slotCount;
            OutputSlots = outputSlots;
        }

        /// <summary>How many of the last slots only give out (a recycler's): see <see cref="AddOutput"/>.</summary>
        public int OutputSlots { get; }

        /// <summary>Where things put in stop: the slots before the output ones.</summary>
        private int FillEnd => CarrySlots - OutputSlots;

        public bool IsOutputSlot(int slot) => OutputSlots > 0 && slot >= FillEnd && slot < CarrySlots;

        public bool HasHotbar { get; }
        public bool HasWear { get; }

        /// <summary>The hotbar and backpack: every slot but the wear slots.</summary>
        public int CarrySlots { get; }

        public bool IsWearSlot(int slot) => HasWear && slot >= CarrySlots && slot < _slots.Length;

        /// <summary>Which part of the body a slot dresses (None: not a wear slot).</summary>
        public WearSlot WearOf(int slot) => IsWearSlot(slot) ? (WearSlot)(slot - CarrySlots + 1) : WearSlot.None;

        /// <summary>The slot that dresses this part of the body, or -1.</summary>
        public int SlotFor(WearSlot wear) => HasWear && wear != WearSlot.None ? CarrySlots + (int)wear - 1 : -1;

        /// <summary>Whether this stack may lie in this slot: anything in a carry slot, only the right clothing in a wear slot.</summary>
        public bool CanHold(int slot, ItemStack stack)
        {
            if (!stack.IsEmpty && IsOutputSlot(slot)) return false;
            if (stack.IsEmpty || !IsWearSlot(slot)) return true;
            return _catalog.TryGet(stack.ItemId, out var def) && def.Category == ItemCategory.Attire && def.Wear == WearOf(slot);
        }

        /// <summary>The share of each blow the worn clothing takes (0 to 0.8).</summary>
        public float Protection
        {
            get
            {
                if (!HasWear) return 0f;
                float total = 0f;
                for (int i = CarrySlots; i < _slots.Length; i++)
                    if (!_slots[i].IsEmpty && _catalog.TryGet(_slots[i].ItemId, out var def)) total += def.Protection;
                return Math.Min(0.8f, total);
            }
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

        /// <summary>How many are carried (not worn): what can be spent.</summary>
        public int CountCarried(string itemId)
        {
            int total = 0;
            for (int i = 0; i < CarrySlots; i++)
                if (!_slots[i].IsEmpty && _slots[i].ItemId == itemId) total += _slots[i].Amount;
            return total;
        }

        /// <summary>How many of this item would fit right now.</summary>
        public int SpaceFor(string itemId, int skinId = 0)
        {
            var def = _catalog.Get(itemId);
            int space = 0;
            var probe = new ItemStack(itemId, 1, skinId);
            for (int i = 0; i < FillEnd; i++)
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

            for (int i = 0; i < FillEnd && remaining > 0; i++)
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

        /// <summary>How many of this item the output slots have room for.</summary>
        public int SpaceForOutput(string itemId)
        {
            var def = _catalog.Get(itemId);
            int space = 0;
            for (int i = FillEnd; i < CarrySlots; i++)
            {
                if (_slots[i].IsEmpty) space += def.MaxStack;
                else if (_slots[i].ItemId == itemId && _slots[i].SkinId == 0) space += def.MaxStack - _slots[i].Amount;
            }
            return space;
        }

        /// <summary>Puts what a machine made into its output slots: onto matching stacks, then empty slots. Returns what did not fit.</summary>
        public int AddOutput(string itemId, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0 || OutputSlots == 0) return amount;
            var def = _catalog.Get(itemId);
            int remaining = amount;
            for (int pass = 0; pass < 2 && remaining > 0; pass++)
            for (int i = FillEnd; i < CarrySlots && remaining > 0; i++)
            {
                var there = _slots[i];
                if (pass == 0 ? there.IsEmpty || there.ItemId != itemId || there.SkinId != 0 : !there.IsEmpty) continue;
                int put = Math.Min(def.MaxStack - (pass == 0 ? there.Amount : 0), remaining);
                if (put <= 0) continue;
                _slots[i] = pass == 0 ? there.WithAmount(there.Amount + put) : new ItemStack(itemId, put);
                remaining -= put;
            }
            if (remaining != amount) Changed?.Invoke(-1);
            return remaining;
        }

        /// <summary>Removes exactly <paramref name="amount"/> or nothing. Takes from the backpack before the hotbar.</summary>
        public bool Remove(string itemId, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0) return true;
            if (CountCarried(itemId) < amount) return false;

            int remaining = amount;
            int split = HasHotbar ? HotbarSize : 0;
            for (int pass = 0; pass < 2 && remaining > 0; pass++)
            {
                int from = pass == 0 ? split : 0;
                int to = pass == 0 ? CarrySlots : split;
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
            // Into a wear slot only the right clothing; a swap must suit both slots.
            if (!CanHold(to, _slots[from]) || !CanHold(from, _slots[to])) return;

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

        /// <summary>
        /// Moves a slot onto a slot of another inventory (a box, a furnace): merges when it can,
        /// otherwise swaps. Returns true if anything moved.
        /// </summary>
        public static bool Transfer(Inventory from, int fromSlot, Inventory to, int toSlot)
        {
            from.CheckSlot(fromSlot);
            to.CheckSlot(toSlot);
            if (ReferenceEquals(from, to))
            {
                from.Move(fromSlot, toSlot);
                return fromSlot != toSlot;
            }
            var moving = from._slots[fromSlot];
            if (moving.IsEmpty) return false;
            var there = to._slots[toSlot];
            if (!to.CanHold(toSlot, moving) || !from.CanHold(fromSlot, there)) return false;
            if (there.CanMergeWith(moving))
            {
                int put = Math.Min(to._catalog.Get(there.ItemId).MaxStack - there.Amount, moving.Amount);
                if (put <= 0) return false;
                to._slots[toSlot] = there.WithAmount(there.Amount + put);
                from._slots[fromSlot] = moving.WithAmount(moving.Amount - put);
            }
            else
            {
                // A swap must fit both ways.
                if (!there.IsEmpty && there.Amount > from._catalog.Get(there.ItemId).MaxStack) return false;
                to._slots[toSlot] = moving;
                from._slots[fromSlot] = there.IsEmpty ? ItemStack.Empty : there;
            }
            from.Changed?.Invoke(fromSlot);
            to.Changed?.Invoke(toSlot);
            return true;
        }

        /// <summary>Sends a whole stack into another inventory wherever it fits; what does not fit stays.</summary>
        public static bool TransferAll(Inventory from, int fromSlot, Inventory to)
        {
            from.CheckSlot(fromSlot);
            var moving = from._slots[fromSlot];
            if (moving.IsEmpty || ReferenceEquals(from, to)) return false;
            int left = to.Add(moving.ItemId, moving.Amount, moving.SkinId);
            if (left == moving.Amount) return false;
            from._slots[fromSlot] = moving.WithAmount(left);
            from.Changed?.Invoke(fromSlot);
            return true;
        }

        /// <summary>Whether every slot is empty.</summary>
        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < _slots.Length; i++)
                    if (!_slots[i].IsEmpty) return false;
                return true;
            }
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
            // Hotbar to backpack and back; taken off, clothing goes to the backpack.
            int from = IsHotbarSlot(slot) || IsWearSlot(slot) ? HotbarSize : 0;
            int to = IsHotbarSlot(slot) || IsWearSlot(slot) ? CarrySlots : HotbarSize;
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

        /// <summary>
        /// Puts on the clothing in a carry slot (swapping with what was worn there), or takes off what
        /// is in a wear slot into the backpack. Returns true if anything moved.
        /// </summary>
        public bool Wear(int slot)
        {
            CheckSlot(slot);
            if (_slots[slot].IsEmpty) return false;
            if (IsWearSlot(slot)) return QuickMove(slot);
            if (!_catalog.TryGet(_slots[slot].ItemId, out var def) || def.Category != ItemCategory.Attire) return false;
            int to = SlotFor(def.Wear);
            if (to < 0) return false;
            Move(slot, to);
            return !_slots[to].IsEmpty && _slots[to].ItemId == def.Id;
        }

        /// <summary>Moves half of a stack (rounded down) into the first empty slot. Returns the new slot or -1.</summary>
        public int Split(int slot)
        {
            CheckSlot(slot);
            var stack = _slots[slot];
            if (stack.IsEmpty || stack.Amount < 2) return -1;
            int target = Array.FindIndex(_slots, 0, CarrySlots, s => s.IsEmpty);
            if (target < 0 || IsWearSlot(slot)) return -1;
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

        /// <summary>Puts several stacks in at once (a server's update), announced as one change.</summary>
        public void SetSlots(IReadOnlyList<(int Slot, ItemStack Stack)> stacks)
        {
            if (stacks.Count == 0) return;
            foreach (var (slot, stack) in stacks)
            {
                CheckSlot(slot);
                _slots[slot] = stack.IsEmpty ? ItemStack.Empty : stack;
            }
            Changed?.Invoke(-1);
        }

        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
            Changed?.Invoke(-1);
        }

        private IEnumerable<int> EmptySlotOrder(ItemCategory category)
        {
            bool hotbarFirst = !HasHotbar || category != ItemCategory.Resource;
            if (hotbarFirst)
            {
                for (int i = 0; i < FillEnd; i++) yield return i;
            }
            else
            {
                for (int i = HotbarSize; i < CarrySlots; i++) yield return i;
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
