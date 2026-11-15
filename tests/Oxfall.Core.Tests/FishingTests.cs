using NUnit.Framework;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Fishing: cast at deep water, reel in while the float is under, roast the fish.</summary>
    public class FishingTests
    {
        private const int Seed = 20261114;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        /// <summary>A shore: a spot on land a few metres from deep sea, and a spot of deep sea ahead of it.</summary>
        private static bool FindShore(IslandGround g, out float x, out float z, out float wx, out float wz)
        {
            for (float r = 60f; r < IslandGround.HalfSize; r += 4f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * 0.3927f;
                    x = (float)System.Math.Sin(a) * r;
                    z = (float)System.Math.Cos(a) * r;
                    wx = (float)System.Math.Sin(a) * (r + 9f);
                    wz = (float)System.Math.Cos(a) * (r + 9f);
                    if (g.Height(x, z) > IslandGround.WaterLevel + 0.5f && Fishing.WaterAt(g, wx, wz, out _)) return true;
                }
            x = z = wx = wz = 0f;
            return false;
        }

        [Test]
        public void A_rod_is_crafted_and_raw_fish_roasts_at_a_campfire()
        {
            Assert.IsTrue(RecipeBook.CreateDefault(Catalog).TryGet("fishing_rod", out var r));
            Assert.AreEqual(ItemIds.FishingRod, r.OutputItemId);
            Assert.IsTrue(Catalog.TryGet(ItemIds.FishingRod, out var rod));
            Assert.AreEqual(ToolKind.FishingRod, rod.Tool);
            Assert.IsFalse(Combat.CanHurt(ToolKind.FishingRod));

            var w = new Settlement(new IslandGround(Seed), Catalog);
            var fire = w.Add(new Deployable(0, DeployableKind.Campfire, 0f, 1f, 0f, 0f, 1));
            var inside = w.Container(fire.Id);
            Assert.IsNotNull(inside);
            inside.Add(ItemIds.RawFish, 2);
            w.Tick(Settlement.CookSeconds * 2.5f);
            Assert.AreEqual(2, inside.Count(ItemIds.CookedFish));
            Assert.AreEqual(0, inside.Count(ItemIds.RawFish));
        }

        [Test]
        public void Only_deep_water_within_reach_takes_a_cast()
        {
            var g = new IslandGround(Seed);
            Assert.IsTrue(FindShore(g, out float x, out float z, out float wx, out float wz));
            float y = g.Height(x, z);
            Assert.AreEqual(ActionResult.Ok, Fishing.CanCast(g, x, y, z, wx, wz));
            Assert.AreEqual(ActionResult.NoWater, Fishing.CanCast(g, x, y, z, x + (x - wx) * 0.5f, z + (z - wz) * 0.5f), "inland, behind");
            Assert.AreEqual(ActionResult.TooFar, Fishing.CanCast(g, x, y, z, x + (wx - x) * 3f, z + (wz - z) * 3f));
        }

        [Test]
        public void Reeling_in_time_lands_a_fish_too_soon_nothing_too_late_it_escapes()
        {
            var g = new IslandGround(Seed);
            Assert.IsTrue(FindShore(g, out float x, out float z, out float wx, out float wz));
            float y = g.Height(x, z);

            var angler = new Angler();
            Assert.AreEqual(ActionResult.Ok, angler.Cast(g, x, y, z, wx, wz, 0f, 7));
            Assert.That(angler.BiteAt, Is.InRange(Fishing.BiteMin, Fishing.BiteMax));
            Assert.IsFalse(angler.Biting(angler.BiteAt - 0.5f));
            Assert.IsTrue(angler.Biting(angler.BiteAt + 0.5f));
            Assert.IsTrue(Fishing.IsFish(angler.Reel(angler.BiteAt + 0.5f)));
            Assert.IsFalse(angler.Casting);

            angler.Cast(g, x, y, z, wx, wz, 0f, 7);
            Assert.AreEqual(FishCatch.Nothing, angler.Reel(1f));
            angler.Cast(g, x, y, z, wx, wz, 0f, 7);
            Assert.AreEqual(FishCatch.Escaped, angler.Reel(angler.BiteAt + Fishing.BiteWindow + 1f));
        }

        [Test]
        public void Catches_are_mostly_small_sometimes_trout_rarely_salmon()
        {
            int small = 0, trout = 0, salmon = 0;
            for (int i = 0; i < 2000; i++)
            {
                var c = Fishing.Kind(i * 7919);
                if (c == FishCatch.Small) small++;
                else if (c == FishCatch.Trout) trout++;
                else if (c == FishCatch.Salmon) salmon++;
            }
            Assert.Greater(small, trout);
            Assert.Greater(trout, salmon);
            Assert.Greater(salmon, 50);
            Assert.AreEqual(4, Fishing.Pieces(FishCatch.Salmon));
        }

        [Test]
        public void On_the_server_a_bite_is_told_and_reeling_in_puts_fish_in_the_inventory()
        {
            var world = new ServerWorld(Seed);
            var rec = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var p, out _, out _);
            Assert.IsTrue(FindShore(world.Ground, out float x, out float z, out float wx, out float wz));
            world.Players.Place(p.Id, new Pose(x, world.Ground.Height(x, z), z, 0f), 0f);
            rec.Survivor.Inventory.Add(ItemIds.FishingRod, 1);
            int a = (int)System.Math.Round(wx * 10f), b = (int)System.Math.Round(wz * 10f);
            Assert.AreEqual(ActionResult.Invalid, world.Act(p.Id, ActKind.Cast, a, b, null), "not in hand");
            Assert.AreEqual(ItemIds.FishingRod, world.Hold(p.Id, ItemIds.FishingRod));
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Cast, a, b, null));

            for (int i = 0; i < 200 && !world.Fished.Exists(f => f.What == FishCatch.Bite); i++) world.Tick(0.1f);
            Assert.IsTrue(world.Fished.Exists(f => f.PlayerId == p.Id && f.What == FishCatch.Bite));
            world.Fished.Clear();
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Reel, 0, 0, null));
            Assert.AreEqual(1, world.Fished.Count);
            Assert.IsTrue(Fishing.IsFish(world.Fished[0].What));
            Assert.AreEqual(Fishing.Pieces(world.Fished[0].What), rec.Survivor.Inventory.Count(ItemIds.RawFish));
        }

        [Test]
        public void Walking_away_loses_the_line()
        {
            var world = new ServerWorld(Seed);
            var rec = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var p, out _, out _);
            Assert.IsTrue(FindShore(world.Ground, out float x, out float z, out float wx, out float wz));
            world.Players.Place(p.Id, new Pose(x, world.Ground.Height(x, z), z, 0f), 0f);
            rec.Survivor.Inventory.Add(ItemIds.FishingRod, 1);
            world.Hold(p.Id, ItemIds.FishingRod);
            Assert.AreEqual(ActionResult.Ok, world.Act(p.Id, ActKind.Cast, (int)(wx * 10f), (int)(wz * 10f), null));
            world.Players.Place(p.Id, new Pose(x - (wx - x) * 4f, world.Ground.Height(x, z), z - (wz - z) * 4f, 0f), 0f);
            world.Tick(0.1f);
            Assert.IsTrue(world.Fished.Exists(f => f.What == FishCatch.Lost));
            Assert.IsFalse(rec.Angler.Casting);
        }

        [Test]
        public void The_cast_message_round_trips()
        {
            var w = new NetWriter();
            Messages.Act(w, ActKind.Cast, 1234, -5678);
            var r = new NetReader(w.ToArray());
            Assert.AreEqual((byte)MessageType.Act, r.Byte());
            Assert.AreEqual(ActKind.Cast, Messages.ReadAct(r, out int a, out int b, out _));
            Assert.AreEqual(1234, a);
            Assert.AreEqual(-5678, b);
            Messages.Fish(w, FishCatch.Trout);
            r = new NetReader(w.ToArray());
            Assert.AreEqual((byte)MessageType.Fish, r.Byte());
            Assert.AreEqual(FishCatch.Trout, Messages.ReadFish(r));
        }
    }
}
