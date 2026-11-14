using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>
    /// Saving the server's island to bytes and back: what has been cut and mined, what stands, who
    /// built it, and every survivor (online or away) with their belongings and where they stood. A
    /// save from another seed is another wipe's island, so it is not loaded.
    /// </summary>
    public sealed partial class ServerWorld
    {
        /// <summary>Raised whenever the save's layout changes; older saves are then not loaded.</summary>
        public const ushort SaveVersion = 4;

        private static readonly byte[] Magic = { (byte)'O', (byte)'X', (byte)'S', (byte)'V' };

        /// <summary>Writes everything worth keeping. Players online are saved where they stand now.</summary>
        public byte[] Save()
        {
            var w = new NetWriter(16 * 1024);
            foreach (byte b in Magic) w.Byte(b);
            w.UShort(SaveVersion);
            w.Int(Seed);

            var damaged = new List<Node>();
            foreach (var n in _nodes.Values)
                if (n.State.Remaining < n.State.Capacity) damaged.Add(n);
            w.Int(damaged.Count);
            foreach (var n in damaged)
            {
                w.Int(n.State.Id);
                w.UShort((ushort)Math.Max(0, n.State.Remaining));
                w.Float(n.State.RespawnTimer);
            }

            var ops = BuildOps.Describe(Settlement);
            w.Int(ops.Count);
            foreach (var op in ops) op.Write(w);
            var owners = new List<KeyValuePair<PieceRef, int>>(Settlement.Owners);
            w.Int(owners.Count);
            foreach (var pair in owners)
            {
                BuildOp.Remove(pair.Key).Write(w);
                w.Int(pair.Value);
            }

            // What boxes and furnaces hold.
            var boxes = new List<int>();
            foreach (var d in Settlement.Deployables)
                if (Settlement.Container(d.Id) != null) boxes.Add(d.Id);
            w.Int(boxes.Count);
            foreach (int id in boxes)
            {
                w.Int(id);
                WriteSlots(w, Settlement.Container(id));
            }

            // The codes of code locks (the settlement as described hides them).
            var codes = new List<KeyValuePair<LockTarget, CodeLockState>>();
            foreach (var pair in Settlement.Locks)
                if (CodeLocks.IsValidCode(pair.Value.Code)) codes.Add(pair);
            w.Int(codes.Count);
            foreach (var pair in codes)
            {
                BuildOp.RemoveLock(pair.Key).Write(w);
                w.Int(pair.Value.Code);
            }

            w.Int(_nextOwner);
            w.Int(_records.Count);
            foreach (var record in _records.Values)
            {
                var pose = record.PlayerId != 0 && Players.TryGet(record.PlayerId, out var player) ? player.Pose : record.Pose;
                var s = record.Survivor;
                w.String(record.Token, Protocol.MaxTokenBytes);
                w.String(record.Name ?? "", Protocol.MaxNameBytes);
                w.Int(record.Owner);
                pose.Write(w);
                w.Float(s.Vitals.Health);
                w.Float(s.Vitals.Food);
                w.Float(s.Vitals.Water);
                WriteSlots(w, s.Inventory);
                w.Byte((byte)s.Crafting.Jobs.Count);
                foreach (var job in s.Crafting.Jobs)
                {
                    w.String(job.Recipe.Id, 64);
                    w.Float(job.Elapsed);
                }
            }
            return w.ToArray();
        }

        private static void WriteSlots(NetWriter w, Inventory inventory)
        {
            w.Byte((byte)inventory.SlotCount);
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                var stack = inventory[i];
                w.String(stack.IsEmpty ? "" : stack.ItemId, 64);
                if (stack.IsEmpty) continue;
                w.UShort((ushort)stack.Amount);
                w.Int(stack.SkinId);
            }
        }

        /// <summary>Reads slots saved by <see cref="WriteSlots"/>; items this version no longer has are dropped.</summary>
        private void ReadSlots(NetReader r, List<(int, ItemStack)> into, int slotLimit)
        {
            into.Clear();
            int slotCount = r.Byte();
            for (int slot = 0; slot < slotCount; slot++)
            {
                string item = r.String();
                if (item.Length == 0) continue;
                int amount = r.UShort();
                int skin = r.Int();
                if (Catalog.TryGet(item, out var def) && slot < slotLimit)
                    into.Add((slot, new ItemStack(item, Math.Min(amount, def.MaxStack), skin)));
            }
        }

        /// <summary>
        /// Loads a save into this fresh world (before anyone joins). False, with the world left as
        /// it was, when the save is another seed's or another layout's.
        /// </summary>
        public bool Load(byte[] data, out string why)
        {
            why = null;
            var r = new NetReader(data);
            try
            {
                foreach (byte b in Magic)
                    if (r.Byte() != b) { why = "not an Oxfall save"; return false; }
                if (r.UShort() != SaveVersion) { why = "an older save layout"; return false; }
                int seed = r.Int();
                if (seed != Seed) { why = $"seed {seed}, another wipe"; return false; }
                if (_records.Count > 0 || Settlement.Grid.PieceCount > 0 || Settlement.DeployableCount > 0) { why = "the world is not fresh"; return false; }

                int nodes = r.Int();
                for (int i = 0; i < nodes; i++)
                {
                    int id = r.Int();
                    int remaining = r.UShort();
                    float timer = r.Float();
                    if (_nodes.TryGetValue(id, out var node)) node.State.Restore(Math.Min(remaining, node.State.Capacity), timer);
                }

                int opCount = r.Int();
                for (int i = 0; i < opCount; i++) BuildOps.Apply(Settlement, BuildOp.Read(r));
                int ownerCount = r.Int();
                for (int i = 0; i < ownerCount; i++)
                {
                    var piece = BuildOp.Read(r).Piece;
                    int owner = r.Int();
                    var stored = BuildOps.Stored(Settlement.Grid, piece);
                    if (Settlement.Grid.Exists(stored)) Settlement.SetOwner(stored, owner);
                }

                var slots = new List<(int, ItemStack)>();
                int boxCount = r.Int();
                for (int i = 0; i < boxCount; i++)
                {
                    int id = r.Int();
                    var box = Settlement.Container(id);
                    ReadSlots(r, slots, box?.SlotCount ?? 0);
                    box?.SetSlots(slots);
                }

                int codeCount = r.Int();
                for (int i = 0; i < codeCount; i++)
                {
                    var target = BuildOp.Read(r).Lock;
                    Settlement.RestoreCode(target, r.Int());
                }

                _nextOwner = Math.Max(1, r.Int());
                int records = r.Int();
                var jobs = new List<(Recipe, float)>();
                for (int i = 0; i < records; i++)
                {
                    string token = r.String();
                    string name = r.String();
                    int owner = r.Int();
                    var pose = Pose.Read(r);
                    float health = r.Float(), food = r.Float(), water = r.Float();
                    ReadSlots(r, slots, Inventory.PlayerSlotCount);
                    jobs.Clear();
                    int jobCount = r.Byte();
                    for (int j = 0; j < jobCount; j++)
                    {
                        string recipeId = r.String();
                        float elapsed = r.Float();
                        if (Recipes.TryGet(recipeId, out var recipe)) jobs.Add((recipe, elapsed));
                    }
                    if (!Protocol.IsValidToken(token) || _records.ContainsKey(token)) continue;

                    var record = AddRecord(token, owner, pose);
                    record.Name = name;
                    record.Survivor.Inventory.Clear();
                    record.Survivor.Inventory.SetSlots(slots);
                    record.Survivor.Crafting.Load(jobs);
                    record.Survivor.Vitals.Set(health, food, water);
                    _nextOwner = Math.Max(_nextOwner, owner + 1);
                }
                return true;
            }
            catch (ProtocolException e)
            {
                why = "damaged: " + e.Message;
                return false;
            }
        }
    }
}
