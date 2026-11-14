using System;
using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Tests
{
    public class FirearmTests
    {
        private const int Seed = 20261114;
        private const string TokenA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string TokenB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        [Test]
        public void GunsAndTheirRoundsAreCraftedFromGunpowderAndMetal()
        {
            var catalog = ItemCatalog.CreateDefault();
            var recipes = RecipeBook.CreateDefault(catalog);
            Assert.AreEqual(ToolKind.Shotgun, catalog.Get(ItemIds.Blunderbuss).Tool);
            Assert.AreEqual(ToolKind.Revolver, catalog.Get(ItemIds.Revolver).Tool);
            Assert.AreEqual(ItemCategory.Ammo, catalog.Get(ItemIds.Shell).Category);
            Assert.AreEqual(ItemCategory.Ammo, catalog.Get(ItemIds.PistolBullet).Category);
            foreach (var id in new[] { "blunderbuss", "revolver", "handmade_shells", "pistol_bullets" })
                Assert.IsTrue(recipes.TryGet(id, out _), id);
            Assert.IsTrue(System.Linq.Enumerable.Any(recipes.Get("handmade_shells").Ingredients, i => i.ItemId == ItemIds.Gunpowder));
            Assert.IsTrue(System.Linq.Enumerable.Any(recipes.Get("pistol_bullets").Ingredients, i => i.ItemId == ItemIds.Gunpowder));
            Assert.IsTrue(Firearms.IsGun(ToolKind.Shotgun) && Firearms.IsGun(ToolKind.Revolver));
            Assert.IsFalse(Firearms.IsGun(ToolKind.Bow));
            Assert.AreEqual(0f, Combat.MeleeDamage(ToolKind.Revolver), "not a club");
        }

        [Test]
        public void TheShotgunRulesUpCloseAndTheRevolverFarOff()
        {
            var shotgun = Firearms.Blunderbuss;
            var revolver = Firearms.Revolver;
            Assert.AreEqual(1f, Firearms.Falloff(shotgun, 3f));
            Assert.AreEqual(0f, Firearms.Falloff(shotgun, shotgun.Range));
            Assert.Greater(Firearms.Damage(shotgun, shotgun.Pellets, 3f), 80f, "a whole spray at three metres nearly kills");
            Assert.Less(Firearms.Damage(shotgun, 3, 25f), 15f, "a few pellets at 25 m scratch");
            Assert.AreEqual(30f, Firearms.Damage(revolver, 1, 15f));
            Assert.Greater(Firearms.Damage(revolver, 1, 60f), Firearms.Damage(shotgun, 3, 25f), "the revolver reaches");
            Assert.AreEqual(revolver.MaxDamage, Firearms.Damage(revolver, 5, 1f), "one bullet a shot, however many are claimed");

            // The spray: inside its spread, and the same for every copy that knows the seed.
            for (int i = 0; i < shotgun.Pellets; i++)
            {
                var (yaw, pitch) = Firearms.PelletOffset(shotgun, 4242, i);
                Assert.LessOrEqual(Math.Sqrt(yaw * yaw + pitch * pitch), shotgun.SpreadDegrees + 1e-3, "pellet " + i);
                Assert.AreEqual((yaw, pitch), Firearms.PelletOffset(shotgun, 4242, i));
            }
            Assert.AreNotEqual(Firearms.PelletOffset(shotgun, 1, 3), Firearms.PelletOffset(shotgun, 2, 3));
        }

        [Test]
        public void ARevolverFiresSixThenReloads()
        {
            var gun = Firearms.Revolver;
            var state = new GunState();
            float now = 10f;
            Assert.AreEqual(6, state.Loaded(gun, now));
            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(state.CanFire(gun, now), "shot " + i);
                state.Fire(gun, now);
                Assert.IsFalse(state.CanFire(gun, now + 0.1f), "not that fast");
                now += gun.FireSeconds;
            }
            Assert.AreEqual(0, state.Loaded(gun, now));
            Assert.IsTrue(state.Reloading(now));
            Assert.IsFalse(state.CanFire(gun, now + 1f), "reloading");
            Assert.IsTrue(state.CanFire(gun, now + gun.ReloadSeconds));
            Assert.AreEqual(6, state.Loaded(gun, now + gun.ReloadSeconds));

            // Reloading early, from two spent.
            now += gun.ReloadSeconds;
            state.Fire(gun, now);
            state.Fire(gun, now + 0.5f);
            Assert.AreEqual(4, state.Loaded(gun, now + 0.6f));
            Assert.IsTrue(state.Reload(gun, now + 0.6f));
            Assert.IsFalse(state.Reload(gun, now + 0.7f), "already at it");
            Assert.AreEqual(6, state.Loaded(gun, now + 0.6f + gun.ReloadSeconds));
        }

        [Test]
        public void OthersSeeOnlyWhatIsOnTheHotbarInHand()
        {
            var world = new ServerWorld(Seed) { DevMode = true };
            var ale = world.Join(Protocol.Version, "Ale", TokenA, 0f, out var pa, out _, out _);
            Assert.AreEqual("", pa.Held, "bare hands to begin with");
            Assert.AreEqual(ItemIds.Rock, world.Hold(pa.Id, ItemIds.Rock), "the rock he starts with");
            Assert.IsNull(world.Hold(pa.Id, ItemIds.Rock), "no change, nothing to tell");
            Assert.IsNull(world.Hold(pa.Id, ItemIds.SatchelCharge), "not carried");
            ale.Survivor.Inventory.SetSlot(Inventory.HotbarSize, new ItemStack(ItemIds.Blunderbuss, 1));
            Assert.IsNull(world.Hold(pa.Id, ItemIds.Blunderbuss), "in the backpack, not on the hotbar");
            ale.Survivor.Inventory.SetSlot(2, new ItemStack(ItemIds.Revolver, 1));
            Assert.AreEqual(ItemIds.Revolver, world.Hold(pa.Id, ItemIds.Revolver));
            Assert.AreEqual(ItemIds.Revolver, pa.Held);
            Assert.AreEqual("", world.Hold(pa.Id, null), "put away");
            Assert.IsNull(world.Hold(999, ItemIds.Rock), "nobody");
        }

        private static (ServerWorld world, SurvivorRecord ale, ServerState.Player pa, SurvivorRecord bea, ServerState.Player pb) Duel(float apart)
        {
            var world = new ServerWorld(Seed) { DevMode = true };
            var ale = world.Join(Protocol.Version, "Ale", TokenA, 0f, out var pa, out _, out _);
            var bea = world.Join(Protocol.Version, "Bea", TokenB, 0f, out var pb, out _, out _);
            // Bea due north of Ale, on flat ground at the same height.
            float x = pa.Pose.X, y = pa.Pose.Y, z = pa.Pose.Z;
            world.Players.Place(pa.Id, new Pose(x, y, z, 0f), 0f);
            world.Players.Place(pb.Id, new Pose(x, y, z + apart, 180f), 0f);
            ServerWorld.GiveTestKit(ale.Survivor.Inventory, 2);
            return (world, ale, pa, bea, pb);
        }

        [Test]
        public void ABulletAimedAtSomeoneHurtsThem_OneAimedAwayDoesNot()
        {
            var (world, ale, pa, bea, pb) = Duel(15f);
            var eye = (pa.Pose.X, pa.Pose.Y + 1.6f, pa.Pose.Z);
            int rounds = ale.Survivor.Inventory.Count(ItemIds.PistolBullet);

            // Straight at her chest (a little down: her middle is below his eyes).
            float pitch = (float)(Math.Atan2(-0.6, 15.0) * 180.0 / Math.PI);
            Assert.IsTrue(world.Fire(pa.Id, ToolKind.Revolver, eye.X, eye.Item2, eye.Z, 0f, pitch, 1f));
            Assert.AreEqual(rounds - 1, ale.Survivor.Inventory.Count(ItemIds.PistolBullet), "a round spent");
            Assert.IsTrue(world.GunHit(pa.Id, ArrowTarget.Player, pb.Id, 1, 1.05f, out float damage, out bool killed, out _));
            Assert.AreEqual(30f, damage, 1e-3f);
            Assert.IsFalse(killed);
            Assert.IsFalse(world.GunHit(pa.Id, ArrowTarget.Player, pb.Id, 1, 1.1f, out _, out _, out _), "one bullet hits once");

            // Forty degrees off: whatever the phone says, she was not in the line of it.
            Assert.IsTrue(world.Fire(pa.Id, ToolKind.Revolver, eye.X, eye.Item2, eye.Z, 40f, pitch, 1.5f));
            Assert.IsFalse(world.GunHit(pa.Id, ArrowTarget.Player, pb.Id, 1, 1.55f, out _, out _, out _), "aimed away");

            // Too late to claim, and not at oneself.
            Assert.IsTrue(world.Fire(pa.Id, ToolKind.Revolver, eye.X, eye.Item2, eye.Z, 0f, pitch, 2f));
            Assert.IsFalse(world.GunHit(pa.Id, ArrowTarget.Player, pa.Id, 1, 2.05f, out _, out _, out _), "not oneself");
            Assert.IsFalse(world.GunHit(pa.Id, ArrowTarget.Player, pb.Id, 1, 2f + Firearms.HitWindowSeconds + 0.1f, out _, out _, out _), "too late");
        }

        [Test]
        public void AShotgunSprayKillsInTwoUpClose()
        {
            var (world, ale, pa, bea, pb) = Duel(3f);
            var eye = (pa.Pose.X, pa.Pose.Y + 1.6f, pa.Pose.Z);
            Assert.IsTrue(world.Fire(pa.Id, ToolKind.Shotgun, eye.X, eye.Item2, eye.Z, 0f, -10f, 1f));
            Assert.IsTrue(world.GunHit(pa.Id, ArrowTarget.Player, pb.Id, 20, 1.05f, out float first, out bool killed, out _), "more claimed than it holds");
            Assert.AreEqual(Firearms.Blunderbuss.MaxDamage, first, 1e-3f, "but only its pellets count");
            Assert.IsFalse(killed);
            Assert.IsFalse(world.Fire(pa.Id, ToolKind.Shotgun, eye.X, eye.Item2, eye.Z, 0f, -10f, 1.8f), "reloading after its one shot");
            Assert.IsTrue(world.Fire(pa.Id, ToolKind.Shotgun, eye.X, eye.Item2, eye.Z, 0f, -10f, 1f + Firearms.Blunderbuss.ReloadSeconds));
            Assert.IsTrue(world.GunHit(pa.Id, ArrowTarget.Player, pb.Id, 8, 1.1f + Firearms.Blunderbuss.ReloadSeconds, out _, out killed, out _));
            Assert.IsTrue(killed, "two sprays at three metres");
        }

        [Test]
        public void NoGunNoRoundsOrFromElsewhereIsRefused()
        {
            var (world, ale, pa, bea, pb) = Duel(10f);
            var eye = (pa.Pose.X, pa.Pose.Y + 1.6f, pa.Pose.Z);
            Assert.IsFalse(world.Fire(pb.Id, ToolKind.Revolver, pb.Pose.X, pb.Pose.Y + 1.6f, pb.Pose.Z, 180f, 0f, 1f), "Bea has no gun");
            Assert.IsFalse(world.Fire(pa.Id, ToolKind.Bow, eye.X, eye.Item2, eye.Z, 0f, 0f, 1f), "not a gun");
            Assert.IsFalse(world.Fire(pa.Id, ToolKind.Revolver, eye.X + 30f, eye.Item2, eye.Z, 0f, 0f, 1f), "not from where he stands");
            Assert.IsFalse(world.Fire(pa.Id, ToolKind.Revolver, eye.X, float.NaN, eye.Z, 0f, 0f, 1f), "nonsense");
            ale.Survivor.Inventory.Remove(ItemIds.PistolBullet, ale.Survivor.Inventory.Count(ItemIds.PistolBullet));
            Assert.IsFalse(world.Fire(pa.Id, ToolKind.Revolver, eye.X, eye.Item2, eye.Z, 0f, 0f, 1f), "no bullets");
            Assert.IsTrue(world.Fire(pa.Id, ToolKind.Shotgun, eye.X, eye.Item2, eye.Z, 0f, 0f, 1f), "shells still");
        }

        [Test]
        public void ShotsCrossTheWireIntact()
        {
            var w = new NetWriter();
            Messages.Fire(w, ToolKind.Shotgun, 1.5f, 2.5f, -3.5f, 270f, -12.5f, 65000);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual(MessageType.Fire, (MessageType)r.Byte());
            Messages.ReadFire(r, out var tool, out float x, out float y, out float z, out float yaw, out float pitch, out ushort seed);
            Assert.AreEqual(ToolKind.Shotgun, tool);
            Assert.AreEqual((1.5f, 2.5f, -3.5f, 270f, -12.5f, (ushort)65000), (x, y, z, yaw, pitch, seed));

            Messages.GunHit(w, ArrowTarget.Sleeper, 77, 5);
            r = new NetReader(w.ToArray());
            Assert.AreEqual(MessageType.GunHit, (MessageType)r.Byte());
            Messages.ReadGunHit(r, out var kind, out int target, out int pellets);
            Assert.AreEqual((ArrowTarget.Sleeper, 77, 5), (kind, target, pellets));

            Messages.Gunshot(w, 9, ToolKind.Revolver, 1f, 2f, 3f, 45f, 5f, 12);
            r = new NetReader(w.ToArray());
            Assert.AreEqual(MessageType.Gunshot, (MessageType)r.Byte());
            Messages.ReadGunshot(r, out ushort shooter, out tool, out x, out y, out z, out yaw, out pitch, out seed);
            Assert.AreEqual((9, ToolKind.Revolver, 45f, (ushort)12), (shooter, tool, yaw, seed));

            Messages.Reload(w, ToolKind.Revolver);
            r = new NetReader(w.ToArray());
            Assert.AreEqual(MessageType.Reload, (MessageType)r.Byte());
            Assert.AreEqual(ToolKind.Revolver, Messages.ReadReload(r));
        }
    }
}
