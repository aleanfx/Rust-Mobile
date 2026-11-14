using System;
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
            Inventory = new Inventory(catalog, Inventory.PlayerSlotCount, hasHotbar: true, hasWear: true);
            Crafting = new CraftingQueue(Inventory);
            Vitals = new Vitals();
            Vitals.Died += () => Looting = 0;
            GiveStarterKit();
        }

        private readonly System.Collections.Generic.HashSet<string> _learned = new System.Collections.Generic.HashSet<string>();

        /// <summary>The blueprints they have learned (recipe ids); the rest of what needs one is locked.</summary>
        public System.Collections.Generic.IReadOnlyCollection<string> Learned => _learned;

        /// <summary>Their blueprints changed (one learned, or the server said which they know).</summary>
        public event Action LearnedChanged;

        /// <summary>Whether they can make this: it needs no blueprint, or they learned it.</summary>
        public bool Knows(Recipe recipe) => recipe.ScrapToLearn <= 0 || _learned.Contains(recipe.Id);

        public void Learn(string recipeId)
        {
            if (_learned.Add(recipeId)) LearnedChanged?.Invoke();
        }

        /// <summary>Exactly these blueprints (as the server or a save says).</summary>
        public void SetLearned(System.Collections.Generic.IEnumerable<string> recipeIds)
        {
            _learned.Clear();
            foreach (string id in recipeIds) _learned.Add(id);
            LearnedChanged?.Invoke();
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

        /// <summary>
        /// A blow, an arrow or a bite of <paramref name="amount"/>: what they wear takes its share, the
        /// rest comes off their health. Returns the damage done.
        /// </summary>
        public float TakeHit(float amount)
        {
            if (Vitals.IsDead || amount <= 0f) return 0f;
            float dealt = amount * (1f - Inventory.Protection);
            float done = Math.Min(dealt, Vitals.Health);
            Vitals.Damage(dealt);
            return done;
        }

        /// <summary>A tool or weapon of theirs wore out and broke (its item id).</summary>
        public event Action<string> ToolBroke;

        /// <summary>
        /// A tool or weapon of this kind was used (a swing that landed, an arrow loosed, a shot): the one
        /// in hand wears, the item <paramref name="held"/> if it is that, else the first such carried
        /// (the hotbar comes first). True if it broke.
        /// </summary>
        public bool UseTool(ToolKind tool, string held, out string broke)
        {
            broke = null;
            if (tool == ToolKind.None) return false;
            int slot = -1;
            for (int i = 0; i < Inventory.SlotCount; i++)
            {
                var stack = Inventory[i];
                if (stack.IsEmpty || !Inventory.Catalog.TryGet(stack.ItemId, out var def) || def.Tool != tool) continue;
                if (slot < 0) slot = i;
                if (stack.ItemId == held)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0) return false;
            broke = Inventory[slot].ItemId;
            if (UseSlot(slot)) return true;
            broke = null;
            return false;
        }

        /// <summary>The tool or weapon in this slot was used: it wears. True if it broke.</summary>
        public bool UseSlot(int slot)
        {
            string item = Inventory[slot].ItemId;
            if (!Inventory.Spend(slot)) return false;
            ToolBroke?.Invoke(item);
            return true;
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
