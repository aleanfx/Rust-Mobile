using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Survival;

namespace Oxfall.Core.Rules
{
    /// <summary>
    /// One player's body and belongings: what they carry, what they are making and how they are.
    /// The server keeps the real one; a phone online keeps a copy that the server's messages update.
    /// </summary>
    public sealed class Survivor
    {
        public Inventory Inventory { get; }
        public CraftingQueue Crafting { get; }
        public Vitals Vitals { get; }

        public Survivor(ItemCatalog catalog)
        {
            Inventory = new Inventory(catalog);
            Crafting = new CraftingQueue(Inventory);
            Vitals = new Vitals();
            Vitals.Died += () => Looting = 0;
            GiveStarterKit();
        }

        /// <summary>The box or furnace they have open (0: none).</summary>
        public int Looting { get; set; }

        /// <summary>What a fresh player wakes up with in the genre: a rock to gather with and a torch.</summary>
        public void GiveStarterKit()
        {
            Inventory.Clear();
            Inventory.Add(ItemIds.Rock, 1);
            Inventory.Add(ItemIds.Torch, 1);
        }

        /// <summary>Whether they carry something that works as this tool (bare hands always do).</summary>
        public bool HasTool(ToolKind tool)
        {
            if (tool == ToolKind.None) return true;
            for (int i = 0; i < Inventory.SlotCount; i++)
            {
                var stack = Inventory[i];
                if (!stack.IsEmpty && Inventory.Catalog.TryGet(stack.ItemId, out var def) && def.Tool == tool) return true;
            }
            return false;
        }
    }
}
