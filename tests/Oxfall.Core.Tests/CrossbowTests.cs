using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;

namespace Oxfall.Core.Tests
{
    /// <summary>The crossbow: the bow's arrows, harder, no draw, a slow reload; judged by what is in hand.</summary>
    public class CrossbowTests
    {
        private const int Seed = 20261113;

        [Test]
        public void It_is_made_at_a_workbench_and_shoots_harder_and_slower()
        {
            var catalog = ItemCatalog.CreateDefault();
            var book = RecipeBook.CreateDefault(catalog);
            Assert.IsTrue(book.TryGet("crossbow", out var r));
            Assert.IsTrue(r.NeedsWorkbench);
            Assert.AreEqual(ToolKind.Crossbow, catalog.Get(ItemIds.Crossbow).Tool);
            Assert.IsTrue(Combat.Shoots(ToolKind.Crossbow));
            Assert.IsFalse(Combat.CanHurt(ToolKind.Crossbow), "not a club");
            Assert.Greater(Combat.ArrowDamageFor(ToolKind.Crossbow), Combat.ArrowDamageFor(ToolKind.Bow));
            Assert.Greater(Combat.ShotSecondsFor(ToolKind.Crossbow), Combat.ShotSecondsFor(ToolKind.Bow));
            Assert.AreEqual(0f, Combat.DrawSecondsFor(ToolKind.Crossbow));
        }

        [Test]
        public void On_the_server_a_crossbow_in_hand_hits_for_its_damage_and_reloads_slowly()
        {
            var world = new ServerWorld(Seed);
            var a = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var pa, out _, out _);
            var b = world.Join(Protocol.Version, "Bea", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 0f, out var pb, out _, out _);
            var at = pa.Pose;
            world.Players.Place(pb.Id, new Pose(at.X + 30f, at.Y, at.Z, 0f), 0f);
            a.Survivor.Inventory.Add(ItemIds.Crossbow, 1);
            a.Survivor.Inventory.Add(ItemIds.WoodenBow, 1);
            a.Survivor.Inventory.Add(ItemIds.Arrow, 5);
            int slot = -1;
            for (int i = 0; i < Inventory.HotbarSize; i++) if (a.Survivor.Inventory[i].ItemId == ItemIds.Crossbow) slot = i;
            Assume.That(slot >= 0, "the crossbow went on the hotbar");
            Assert.AreEqual(ItemIds.Crossbow, world.Hold(pa.Id, ItemIds.Crossbow));

            float x = at.X, y = at.Y + 1.6f, z = at.Z;
            Assert.IsTrue(world.Shoot(pa.Id, x, y, z, 1f));
            Assert.IsTrue(world.ArrowHit(pa.Id, ArrowTarget.Player, pb.Id, 1.5f, out float damage, out _, out _));
            Assert.AreEqual(Combat.CrossbowDamage, damage, 1e-3f);
            Assert.IsFalse(world.Shoot(pa.Id, x, y, z, 2f), "still loading");
            Assert.IsTrue(world.Shoot(pa.Id, x, y, z, 1f + Combat.CrossbowShotSeconds));
        }
    }
}
