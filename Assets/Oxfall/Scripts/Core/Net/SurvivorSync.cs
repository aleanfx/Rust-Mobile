using System;
using System.Collections.Generic;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>
    /// What one client has been told about its own survivor, so the server sends only what changed:
    /// the inventory slots that differ, the crafting queue when a job starts or ends, and the vitals
    /// every few seconds (they change slowly, and the phone shows them between updates).
    /// </summary>
    public sealed class SurvivorSync
    {
        /// <summary>How often the vitals are sent while nothing sudden happens to them.</summary>
        public const float VitalsInterval = 2f;

        private readonly ItemStack[] _slots;
        private readonly List<CraftingQueue.Job> _jobs = new List<CraftingQueue.Job>();
        private readonly List<(int, ItemStack)> _changed = new List<(int, ItemStack)>();
        private float _health = -1f, _food = -1f, _water = -1f;
        private float _vitalsAt = float.NegativeInfinity;
        private bool _jobsSent;

        public SurvivorSync(int slotCount = Inventory.DefaultSlotCount) => _slots = new ItemStack[slotCount];

        /// <summary>Forgets what was sent, so everything goes again (a new connection).</summary>
        public void Reset()
        {
            Array.Clear(_slots, 0, _slots.Length);
            _jobs.Clear();
            _jobsSent = false;
            _health = _food = _water = -1f;
            _vitalsAt = float.NegativeInfinity;
        }

        /// <summary>Writes the slots that changed since last time; false (nothing written) if none did.</summary>
        public bool Slots(Survivor s, NetWriter w)
        {
            _changed.Clear();
            int n = Math.Min(_slots.Length, s.Inventory.SlotCount);
            for (int i = 0; i < n; i++)
            {
                var now = s.Inventory[i];
                if (now.IsEmpty && _slots[i].IsEmpty) continue;
                if (now.Equals(_slots[i])) continue;
                _slots[i] = now.IsEmpty ? ItemStack.Empty : now;
                _changed.Add((i, _slots[i]));
            }
            if (_changed.Count == 0) return false;
            Messages.Slots(w, _changed);
            return true;
        }

        /// <summary>Writes the crafting queue if a job was added, cancelled or finished since last time.</summary>
        public bool Crafts(Survivor s, NetWriter w)
        {
            var jobs = s.Crafting.Jobs;
            bool same = _jobsSent && jobs.Count == _jobs.Count;
            for (int i = 0; same && i < jobs.Count; i++) same = ReferenceEquals(jobs[i], _jobs[i]);
            if (same) return false;
            _jobs.Clear();
            for (int i = 0; i < jobs.Count; i++) _jobs.Add(jobs[i]);
            _jobsSent = true;
            Messages.Crafts(w, jobs);
            return true;
        }

        /// <summary>Writes the vitals if it is time, or if they jumped (eating, drinking, dying, waking up).</summary>
        public bool Vitals(Survivor s, NetWriter w, float now)
        {
            var v = s.Vitals;
            bool jumped = Math.Abs(v.Health - _health) >= 1f || Math.Abs(v.Food - _food) >= 5f || Math.Abs(v.Water - _water) >= 5f
                          || (v.IsDead != (_health <= 0f && _health > -1f));
            if (!jumped && now - _vitalsAt < VitalsInterval) return false;
            if (!jumped && v.Health == _health && v.Food == _food && v.Water == _water) return false;
            _health = v.Health;
            _food = v.Food;
            _water = v.Water;
            _vitalsAt = now;
            Messages.Vitals(w, v.Health, v.Food, v.Water);
            return true;
        }
    }

    /// <summary>
    /// What one client has been told about the box or furnace it has open: everything when it
    /// opens, then only the slots that change (a furnace changes on its own while it burns), and a
    /// word when it closes.
    /// </summary>
    public sealed class LootSync
    {
        private static readonly List<(int, ItemStack)> None = new List<(int, ItemStack)>();
        private readonly List<(int, ItemStack)> _changed = new List<(int, ItemStack)>();
        private ItemStack[] _slots = Array.Empty<ItemStack>();
        private int _id;

        /// <summary>The box this client was last told about (0: none).</summary>
        public int Open => _id;

        public void Reset()
        {
            _id = 0;
            _slots = Array.Empty<ItemStack>();
        }

        /// <summary>Writes what changed in the open box (or that it opened or closed); false (nothing written) if nothing did.</summary>
        public bool Write(Settlement settlement, Survivor s, NetWriter w)
        {
            int id = s.Looting;
            var box = id != 0 ? settlement.Container(id) : null;
            if (box == null) id = 0;
            _changed.Clear();
            if (id != _id)
            {
                bool wasOpen = _id != 0;
                _id = id;
                if (id == 0)
                {
                    _slots = Array.Empty<ItemStack>();
                    if (!wasOpen) return false;
                    Messages.Loot(w, 0, 0, None);
                    return true;
                }
                _slots = new ItemStack[box.SlotCount];
                for (int i = 0; i < _slots.Length; i++)
                {
                    _slots[i] = box[i].IsEmpty ? ItemStack.Empty : box[i];
                    _changed.Add((i, _slots[i]));
                }
                Messages.Loot(w, id, box.SlotCount, _changed);
                return true;
            }
            if (id == 0) return false;
            for (int i = 0; i < _slots.Length; i++)
            {
                var now = box[i];
                if (now.IsEmpty && _slots[i].IsEmpty) continue;
                if (now.Equals(_slots[i])) continue;
                _slots[i] = now.IsEmpty ? ItemStack.Empty : now;
                _changed.Add((i, _slots[i]));
            }
            if (_changed.Count == 0) return false;
            Messages.Loot(w, id, box.SlotCount, _changed);
            return true;
        }
    }
}
