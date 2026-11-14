using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Tools and weapons wear with use and break; the wear is kept, sent and saved.</summary>
    public class DurabilityTests
    {
        private const int Seed = 20261114;
        private const string Token = "dddddddddddddddddddddddddddddddd";
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        [Test]
        public void Tools_and_weapons_wear_and_the_rest_never_does()
        {
            foreach (string id in new[] { ItemIds.Rock, ItemIds.StoneHatchet, ItemIds.MetalPickaxe, ItemIds.WoodenBow, ItemIds.Revolver, ItemIds.Blunderbuss })
                Assert.Greater(Durability.Uses(id), 0, id);
            foreach (string id in new[] { ItemIds.Wood, ItemIds.Hammer, ItemIds.BuildingPlan, ItemIds.Bandage, ItemIds.HideVest })
                Assert.AreEqual(0, Durability.Uses(id), id);
            // The better one lasts longer.
            Assert.Greater(Durability.Uses(ItemIds.MetalHatchet), Durability.Uses(ItemIds.StoneHatchet));
        }

        [Test]
        public void A_tool_used_up_breaks_and_is_gone()
        {
            var s = new Survivor(Catalog);
            int rock = Enumerable.Range(0, s.Inventory.SlotCount).First(i => s.Inventory[i].ItemId == ItemIds.Rock);
            string broke = null;
            s.ToolBroke += id => broke = id;
            int uses = Durability.Uses(ItemIds.Rock);

            for (int i = 0; i < uses - 1; i++) Assert.IsFalse(s.UseTool(ToolKind.Rock, ItemIds.Rock, out _));
            Assert.AreEqual(uses - 1, s.Inventory[rock].Used);
            Assert.AreEqual(1f / uses, Durability.Left(s.Inventory[rock]), 0.0001f);
            Assert.IsNull(broke);

            Assert.IsTrue(s.UseTool(ToolKind.Rock, ItemIds.Rock, out string gone));
            Assert.AreEqual(ItemIds.Rock, gone);
            Assert.AreEqual(ItemIds.Rock, broke);
            Assert.AreEqual(0, s.Inventory.Count(ItemIds.Rock));
            Assert.IsFalse(s.UseTool(ToolKind.Rock, ItemIds.Rock, out _), "nothing left to wear");
        }

        [Test]
        public void The_one_in_hand_wears_not_a_spare()
        {
            var s = new Survivor(Catalog);
            s.Inventory.Clear();
            s.Inventory.Add(ItemIds.StoneHatchet, 1);
            s.Inventory.Add(ItemIds.MetalHatchet, 1);
            // Both cut wood, but only the stone hatchet is a Hatchet; the metal one is its own kind.
            s.UseTool(ToolKind.MetalHatchet, ItemIds.MetalHatchet, out _);
            var metal = Enumerable.Range(0, s.Inventory.SlotCount).Select(i => s.Inventory[i]).First(st => st.ItemId == ItemIds.MetalHatchet);
            var stone = Enumerable.Range(0, s.Inventory.SlotCount).Select(i => s.Inventory[i]).First(st => st.ItemId == ItemIds.StoneHatchet);
            Assert.AreEqual(1, metal.Used);
            Assert.AreEqual(0, stone.Used);
        }

        [Test]
        public void Worn_stacks_do_not_merge_and_moving_keeps_the_wear()
        {
            var box = new Inventory(Catalog, 6);
            var s = new Survivor(Catalog);
            int rock = Enumerable.Range(0, s.Inventory.SlotCount).First(i => s.Inventory[i].ItemId == ItemIds.Rock);
            s.UseTool(ToolKind.Rock, null, out _);
            s.UseTool(ToolKind.Rock, null, out _);
            Assert.IsTrue(Inventory.TransferAll(s.Inventory, rock, box));
            var moved = Enumerable.Range(0, box.SlotCount).Select(i => box[i]).First(st => !st.IsEmpty);
            Assert.AreEqual(2, moved.Used, "a worn rock stays worn in a box");
            Assert.IsFalse(new ItemStack(ItemIds.Rock, 1, 0, 2).CanMergeWith(new ItemStack(ItemIds.Rock, 1)));
        }

        [Test]
        public void The_wear_is_sent_with_the_slots()
        {
            var w = new NetWriter();
            Messages.Slots(w, new List<(int, ItemStack)> { (3, new ItemStack(ItemIds.Revolver, 1, 0, 17)), (4, ItemStack.Empty) });
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Slots, (MessageType)r.Byte());
            var slots = new List<(int Slot, ItemStack Stack)>();
            Messages.ReadSlots(r, slots);
            Assert.AreEqual(17, slots[0].Stack.Used);
            Assert.IsTrue(slots[1].Stack.IsEmpty);
            Assert.AreEqual(0, r.Remaining);

            Messages.Broke(w, ItemIds.StonePickaxe);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Broke, (MessageType)r.Byte());
            Assert.AreEqual(ItemIds.StonePickaxe, Messages.ReadBroke(r));
        }

        [Test]
        public void On_the_server_hits_wear_the_tool_and_a_break_is_told()
        {
            var world = new ServerWorld(Seed) { DevMode = true };
            var tree = ResourceLayout.Generate(world.Ground).First(n => n.Type == ResourceNodeType.Tree);
            var record = world.Join(Protocol.Version, "Ana", Token, 0f, out var player, out _, out _);
            int rock = Enumerable.Range(0, record.Survivor.Inventory.SlotCount).First(i => record.Survivor.Inventory[i].ItemId == ItemIds.Rock);
            world.Players.Place(player.Id, new Pose(tree.X + 1.5f, tree.Y, tree.Z, 0f), 0f);
            world.Hold(player.Id, ItemIds.Rock);
            record.Survivor.Inventory.SetSlot(rock, record.Survivor.Inventory[rock].WithUsed(Durability.Uses(ItemIds.Rock) - 2));

            Assert.IsNotEmpty(world.Hit(player.Id, tree.Id, ToolKind.Rock, 10f, out _));
            Assert.AreEqual(Durability.Uses(ItemIds.Rock) - 1, record.Survivor.Inventory[rock].Used);
            Assert.IsEmpty(world.Broken);
            Assert.IsNotEmpty(world.Hit(player.Id, tree.Id, ToolKind.Rock, 20f, out _));
            Assert.AreEqual(0, record.Survivor.Inventory.Count(ItemIds.Rock), "worn out, gone");
            CollectionAssert.AreEqual(new[] { (player.Id, ItemIds.Rock) }, world.Broken);
        }

        [Test]
        public void The_wear_is_saved_with_the_island()
        {
            var world = new ServerWorld(Seed);
            var record = world.Join(Protocol.Version, "Ana", Token, 0f, out _, out _, out _);
            record.Survivor.UseTool(ToolKind.Rock, null, out _);
            var loaded = new ServerWorld(Seed);
            Assert.IsTrue(loaded.Load(world.Save(), out var why), why);
            var again = loaded.Records.Single(r => r.Owner == record.Owner).Survivor.Inventory;
            var rock = Enumerable.Range(0, again.SlotCount).Select(i => again[i]).First(st => st.ItemId == ItemIds.Rock);
            Assert.AreEqual(1, rock.Used);
        }
    }
}
