using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Survival;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>The cold: night, rain and the sea chill; clothes, roofs and fires warm; freezing hurts.</summary>
    public class TemperatureTests
    {
        private const int Seed = 20261114;
        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        [Test]
        public void Night_rain_and_water_are_cold_and_days_are_mild()
        {
            Assert.AreEqual(Temperature.DayAir, Temperature.Air(1f, 0f), 1e-4f);
            Assert.AreEqual(Temperature.NightAir, Temperature.Air(0f, 0f), 1e-4f);
            Assert.Less(Temperature.Air(0f, 1f), Temperature.Freezing, "a rainy night is freezing");
            Assert.GreaterOrEqual(Temperature.Felt(1f, 0f, 0f, 0f, false, false), Temperature.Chilly, "a fair day is fine naked");
            Assert.Less(Temperature.Felt(1f, 0f, 0f, 0f, false, true), Temperature.Felt(1f, 0f, 0f, 0f, false, false), "the sea is colder");
            Assert.Less(Temperature.Felt(0f, 0f, 0f, 0f, false, true), Temperature.Freezing, "swimming at night freezes");
        }

        [Test]
        public void Clothes_a_roof_and_a_fire_keep_the_cold_out()
        {
            var s = new Survivor(Catalog);
            Assert.AreEqual(0f, Temperature.Insulation(s.Inventory));
            foreach (var id in new[] { ItemIds.HideHood, ItemIds.HideVest, ItemIds.HidePants, ItemIds.HideBoots })
            {
                s.Inventory.Add(id, 1);
                for (int i = 0; i < s.Inventory.CarrySlots; i++)
                    if (s.Inventory[i].ItemId == id) { PlayerActions.Wear(s, i); break; }
            }
            float hide = Temperature.Insulation(s.Inventory);
            Assert.AreEqual(14f, hide, 1e-4f, "a full set of hide");
            Assert.GreaterOrEqual(Temperature.Felt(0f, 1f, hide, 0f, false, false), Temperature.Freezing, "hide keeps a rainy night out");
            Assert.GreaterOrEqual(Temperature.Felt(0f, 1f, 0f, 0f, true, false), Temperature.Freezing, "and so does a roof");

            var w = new Settlement(new IslandGround(Seed), Catalog);
            w.Add(new Deployable(0, DeployableKind.Campfire, 0f, 1f, 0f, 0f));
            Assert.AreEqual(Temperature.FireHeat, Temperature.FireWarmth(w, 0f, 1f, 0f), 1e-4f);
            Assert.Greater(Temperature.FireWarmth(w, 2f, 1f, 0f), 0f);
            Assert.AreEqual(0f, Temperature.FireWarmth(w, Temperature.FireReach + 0.1f, 1f, 0f), "out of its reach");
            Assert.GreaterOrEqual(Temperature.At(w, null, 0f, 1f, 1f, 1f, 0f, false), Temperature.Freezing, "by the fire on a rainy night");
            var furnace = w.Add(new Deployable(0, DeployableKind.Furnace, 20f, 1f, 0f, 0f));
            Assert.AreEqual(0f, Temperature.FireWarmth(w, 20.5f, 1f, 0f), "a cold furnace");
            w.SetLit(furnace.Id, true);
            Assert.Greater(Temperature.FireWarmth(w, 20.5f, 1f, 0f), 0f, "a lit one");
        }

        [Test]
        public void Under_a_roof_is_sheltered()
        {
            var grid = new BuildingGrid();
            var cell = new Cell(10, 10);
            Assert.AreEqual(PlaceResult.Ok, grid.PlaceFoundation(cell, 5f));
            float x = 31.5f, z = 31.5f;
            Assert.IsFalse(Temperature.Sheltered(grid, x, 5f, z), "no roof yet");
            foreach (var e in cell.Edges) grid.PlaceEdge(e, PieceType.Wall);
            Assert.AreEqual(PlaceResult.Ok, grid.PlaceRoof(cell));
            Assert.IsTrue(Temperature.Sheltered(grid, x, 5f, z));
            Assert.IsFalse(Temperature.Sheltered(grid, x + 3f, 5f, z), "next door, outside");
            Assert.IsFalse(Temperature.Sheltered(grid, x, 8.3f, z), "standing on the roof");
        }

        [Test]
        public void Freezing_hurts_and_chilly_stops_healing()
        {
            Assert.AreEqual(0f, Temperature.Hurt(Temperature.Freezing));
            Assert.Greater(Temperature.Hurt(0f), 0f);
            Assert.AreEqual(Temperature.MaxHurt, Temperature.Hurt(-40f), 1e-4f);

            var v = new Vitals(50f, 400f, 200f);
            v.Tick(10f, 1f, 20f);
            Assert.Greater(v.Health, 50f, "warm and fed: heals");
            float before = v.Health;
            v.Tick(10f, 1f, Temperature.Chilly - 1f);
            Assert.AreEqual(before, v.Health, 1e-4f, "chilly: no healing, no harm");
            v.Tick(10f, 1f, -5f);
            Assert.AreEqual(before - Temperature.Hurt(-5f) * 10f, v.Health, 1e-3f, "freezing: it hurts");
            Assert.IsTrue(v.Freezing);
        }

        [Test]
        public void On_the_server_a_player_in_the_sea_at_night_freezes()
        {
            var world = new ServerWorld(Seed);
            var record = world.Join(Protocol.Version, "Ana", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var ana, out _, out _);
            world.Day.Set(1f);
            world.Players.Place(ana.Id, new Pose(ana.Pose.X, -2f, ana.Pose.Z, 0f), 0f);
            float before = record.Survivor.Vitals.Health;
            for (int i = 0; i < 20; i++) world.Tick(0.5f);
            Assert.Less(record.Survivor.Vitals.Health, before);
            Assert.Less(record.Survivor.Vitals.Warmth, Temperature.Freezing);
        }
    }
}
