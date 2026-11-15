using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Survival;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Radiation: hot in the military ruins, clean at the gas station; clothes keep it off; held long, it hurts, and fades outside.</summary>
    public class RadiationTests
    {
        private const int Seed = 20261114;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        [Test]
        public void The_military_ruins_are_hot_and_the_gas_station_is_not()
        {
            var ground = new IslandGround(Seed);
            var all = Monuments.Find(ground);
            var outpost = all.First(m => m.Kind == MonumentKind.Outpost);
            Assert.AreEqual(1f, Radiation.Level(ground, outpost.X, outpost.Z), 1e-4f);
            Assert.Greater(Radiation.Level(ground, outpost.X + Radiation.Core + 2f, outpost.Z), 0f, "fading at its edge");
            Assert.AreEqual(0f, Radiation.Level(ground, outpost.X + Radiation.Reach + 1f, outpost.Z), "outside");
            foreach (var m in all.Where(m => m.Kind == MonumentKind.GasStation))
                Assert.AreEqual(0f, Radiation.Level(ground, m.X, m.Z), "the gas station is clean");
        }

        [Test]
        public void Clothes_keep_some_off()
        {
            var s = new Survivor(Catalog);
            Assert.AreEqual(0f, Radiation.Resistance(s.Inventory));
            foreach (var id in new[] { ItemIds.HideHood, ItemIds.HideVest, ItemIds.HidePants, ItemIds.HideBoots })
            {
                s.Inventory.Add(id, 1);
                for (int i = 0; i < s.Inventory.CarrySlots; i++)
                    if (s.Inventory[i].ItemId == id) { PlayerActions.Wear(s, i); break; }
            }
            Assert.AreEqual(0.5f, Radiation.Resistance(s.Inventory), 1e-4f);
            var ground = new IslandGround(Seed);
            var outpost = Monuments.Find(ground).First(m => m.Kind == MonumentKind.Outpost);
            Assert.AreEqual(Radiation.Dose * 0.5f, Radiation.DoseAt(ground, s.Inventory, outpost.X, outpost.Z), 1e-4f);
        }

        [Test]
        public void A_quick_visit_is_safe_a_long_one_hurts_and_it_fades()
        {
            var v = new Vitals(100f, 400f, 200f);
            v.Tick(15f, 1f, 20f, Radiation.Dose);
            Assert.AreEqual(100f, v.Health, 1e-3f, "fifteen seconds naked: nothing yet");
            v.Tick(60f, 1f, 20f, Radiation.Dose);
            Assert.Less(v.Health, 100f, "over a minute more: it hurts");
            float held = v.Rads;
            v.Tick(10f, 1f, 20f, 0f);
            Assert.AreEqual(held - Radiation.Fade * 10f, v.Rads, 1e-3f, "it fades outside");
            v.Respawn();
            Assert.AreEqual(0f, v.Rads);
        }

        [Test]
        public void On_the_server_a_player_in_a_military_ruin_soaks_it_up()
        {
            var world = new ServerWorld(Seed);
            var record = world.Join(Protocol.Version, "Ana", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var ana, out _, out _);
            var outpost = Monuments.Find(world.Ground).First(m => m.Kind == MonumentKind.Outpost);
            world.Players.Place(ana.Id, new Pose(outpost.X, outpost.Y, outpost.Z, 0f), 0f);
            for (int i = 0; i < 10; i++) world.Tick(0.5f);
            Assert.Greater(record.Survivor.Vitals.Rads, 10f);
        }
    }
}
