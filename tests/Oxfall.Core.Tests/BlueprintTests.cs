using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Blueprints: the better things are learned first, at a workbench, for scrap; then they are known for good.</summary>
    public class BlueprintTests
    {
        private const int Seed = 20261113;
        private const string Token = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        [Test]
        public void The_basics_are_known_and_the_better_things_locked()
        {
            var recipes = RecipeBook.CreateDefault(Catalog);
            var s = new Survivor(Catalog);
            foreach (string id in new[] { "stone_hatchet", "building_plan", "wooden_bow", "workbench", "code_lock", "blunderbuss", "gunpowder" })
                Assert.IsTrue(s.Knows(recipes.Get(id)), id);
            foreach (string id in new[] { "revolver", "satchel_charge", "metal_hatchet", "metal_pickaxe", "sheet_metal_door", "pistol_bullets", "medical_syringe" })
            {
                Assert.IsFalse(s.Knows(recipes.Get(id)), id);
                Assert.Greater(recipes.Get(id).ScrapToLearn, 0, id);
            }
            // Only things made at a workbench are locked, so the bench is where they are learned.
            Assert.IsTrue(recipes.All.Where(r => r.ScrapToLearn > 0).All(r => r.NeedsWorkbench));
        }

        [Test]
        public void Learned_at_a_workbench_for_scrap_then_made()
        {
            var ground = new IslandGround(Seed);
            var settlement = new Settlement(ground, Catalog);
            var recipes = RecipeBook.CreateDefault(Catalog);
            var revolver = recipes.Get("revolver");
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.MetalFragments, 500);
            s.Inventory.Add(ItemIds.Cloth, 100);
            s.Inventory.Add(ItemIds.MetalPipe, 1);
            s.Inventory.Add(ItemIds.MetalSpring, 1);
            s.Inventory.Add(ItemIds.Scrap, revolver.ScrapToLearn - 1);
            float x = 10f, y = ground.Height(10f, 10f), z = 10f;
            int told = 0;
            s.LearnedChanged += () => told++;

            Assert.AreEqual(ActionResult.NeedsWorkbench, PlayerActions.Learn(s, revolver, settlement, x, y, z));
            settlement.Add(new Deployable(0, DeployableKind.Workbench, x + 2f, y, z, 0f, 1));
            Assert.AreEqual(ActionResult.NotLearned, PlayerActions.Craft(s, revolver, settlement, x, y, z));
            Assert.AreEqual(ActionResult.NotEnoughResources, PlayerActions.Learn(s, revolver, settlement, x, y, z));
            s.Inventory.Add(ItemIds.Scrap, 6);

            Assert.AreEqual(ActionResult.Ok, PlayerActions.Learn(s, revolver, settlement, x, y, z));
            Assert.AreEqual(5, s.Inventory.Count(ItemIds.Scrap), "it cost its scrap");
            Assert.IsTrue(s.Knows(revolver));
            Assert.AreEqual(1, told);
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Learn(s, revolver, settlement, x, y, z), "already known");
            Assert.AreEqual(ActionResult.Invalid, PlayerActions.Learn(s, recipes.Get("torch"), settlement, x, y, z), "nothing to learn");
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Craft(s, revolver, settlement, x, y, z));

            // Known through death: a fresh body keeps what the player learned.
            s.GiveStarterKit();
            Assert.IsTrue(s.Knows(revolver));
        }

        [Test]
        public void On_the_server_learning_is_kept_with_the_island()
        {
            var world = new ServerWorld(Seed);
            var record = world.Join(Protocol.Version, "Bea", Token, 0f, out var player, out _, out _);
            var pose = player.Pose;
            world.Settlement.Add(new Deployable(0, DeployableKind.Workbench, pose.X + 1.5f, pose.Y, pose.Z, 0f, record.Owner));
            record.Survivor.Inventory.Add(ItemIds.Scrap, 200);

            Assert.AreEqual(ActionResult.Invalid, world.Act(player.Id, ActKind.Learn, 0, 0, "no_such_thing"));
            Assert.AreEqual(ActionResult.Ok, world.Act(player.Id, ActKind.Learn, 0, 0, "satchel_charge"));
            Assert.AreEqual(100, record.Survivor.Inventory.Count(ItemIds.Scrap));
            CollectionAssert.AreEquivalent(new[] { "satchel_charge" }, record.Survivor.Learned);

            var loaded = new ServerWorld(Seed);
            Assert.IsTrue(loaded.Load(world.Save(), out var why), why);
            var again = loaded.Records.Single(r => r.Owner == record.Owner);
            CollectionAssert.AreEquivalent(new[] { "satchel_charge" }, again.Survivor.Learned);
        }

        [Test]
        public void The_blueprints_message_round_trips()
        {
            var w = new NetWriter();
            Messages.Blueprints(w, new[] { "revolver", "metal_pickaxe" });
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Blueprints, (MessageType)r.Byte());
            var ids = new List<string> { "stale" };
            Messages.ReadBlueprints(r, ids);
            CollectionAssert.AreEqual(new[] { "revolver", "metal_pickaxe" }, ids);
            Assert.AreEqual(0, r.Remaining);
        }

        [Test]
        public void Every_action_survives_the_trip_to_the_server()
        {
            // Every kind of action the game sends must read back whole (one it cannot read is a broken protocol).
            foreach (ActKind kind in Enum.GetValues(typeof(ActKind)))
            {
                var w = new NetWriter();
                Messages.Act(w, kind, 7, 3, "revolver");
                var r = new NetReader(w.Buffer, 0, w.Length);
                Assert.AreEqual(MessageType.Act, (MessageType)r.Byte());
                Assert.AreEqual(kind, Messages.ReadAct(r, out int a, out _, out string recipe), kind.ToString());
                Assert.AreEqual(0, r.Remaining, kind + " left bytes unread");
                if (kind == ActKind.Wear || kind == ActKind.DevSpawnAnimal || kind == ActKind.DevGive) Assert.AreEqual(7, a, kind.ToString());
                if (kind == ActKind.Craft || kind == ActKind.Learn) Assert.AreEqual("revolver", recipe, kind.ToString());
            }
        }
    }
}
