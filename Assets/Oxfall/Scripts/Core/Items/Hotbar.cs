using System;

namespace Oxfall.Core.Items
{
    /// <summary>
    /// Which hotbar slot is in the player's hands. The hotbar is the first
    /// <see cref="Inventory.HotbarSize"/> slots of the inventory; whatever sits in the chosen slot is
    /// held, so moving another item into that slot changes what is held.
    /// </summary>
    public sealed class Hotbar
    {
        /// <summary>The value of <see cref="Selected"/> when the hands are empty.</summary>
        public const int Holstered = -1;

        private readonly Inventory _inventory;

        /// <summary>Raised after the chosen slot changes, with the new slot (or <see cref="Holstered"/>).</summary>
        public event Action<int> SelectionChanged;

        /// <summary>The chosen hotbar slot, or <see cref="Holstered"/>.</summary>
        public int Selected { get; private set; }

        public Hotbar(Inventory inventory, int selected = 0)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Selected = Check(selected);
        }

        /// <summary>What is in the hands: the stack in the chosen slot, or empty.</summary>
        public ItemStack Held => Selected == Holstered ? ItemStack.Empty : _inventory[Selected];

        /// <summary>The definition of what is held, or null when the hands are empty.</summary>
        public ItemDefinition HeldDefinition
        {
            get
            {
                var held = Held;
                return held.IsEmpty ? null : _inventory.Catalog.Get(held.ItemId);
            }
        }

        /// <summary>The tool in the hands; <see cref="ToolKind.None"/> for bare hands or anything that isn't a tool.</summary>
        public ToolKind HeldTool => HeldDefinition?.Tool ?? ToolKind.None;

        public bool IsHolding(string itemId)
        {
            var held = Held;
            return !held.IsEmpty && held.ItemId == itemId;
        }

        /// <summary>
        /// Chooses a slot. Choosing the slot already chosen empties the hands, as tapping it again
        /// does in the genre.
        /// </summary>
        public void Select(int slot)
        {
            Check(slot);
            Set(slot == Selected ? Holstered : slot);
        }

        /// <summary>Empties the hands.</summary>
        public void Holster() => Set(Holstered);

        /// <summary>Chooses the first hotbar slot holding this item. Returns false (and changes nothing) if none does.</summary>
        public bool SelectItem(string itemId)
        {
            for (int i = 0; i < Inventory.HotbarSize; i++)
            {
                var stack = _inventory[i];
                if (stack.IsEmpty || stack.ItemId != itemId) continue;
                Set(i);
                return true;
            }
            return false;
        }

        private void Set(int slot)
        {
            if (slot == Selected) return;
            Selected = slot;
            SelectionChanged?.Invoke(slot);
        }

        private static int Check(int slot)
        {
            if (slot != Holstered && (slot < 0 || slot >= Inventory.HotbarSize))
                throw new ArgumentOutOfRangeException(nameof(slot), $"Hotbar slots are 0 to {Inventory.HotbarSize - 1}.");
            return slot;
        }
    }
}
