using NUnit.Framework;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.Survival;

namespace Oxfall.Core.Tests
{
    public class VitalsTests
    {
        [Test]
        public void Food_and_water_run_down_with_time()
        {
            var v = new Vitals(100f, 300f, 200f);
            v.Tick(600f);
            Assert.That(v.Food, Is.EqualTo(300f - Vitals.FoodPerSecond * 600f).Within(1e-3));
            Assert.That(v.Water, Is.EqualTo(200f - Vitals.WaterPerSecond * 600f).Within(1e-3));
        }

        [Test]
        public void Running_burns_them_faster()
        {
            var walk = new Vitals();
            var run = new Vitals();
            walk.Tick(100f);
            run.Tick(100f, 2f);
            Assert.That(300f - run.Food, Is.EqualTo((300f - walk.Food) * 2f).Within(1e-3));
        }

        [Test]
        public void Empty_food_or_water_drains_health_and_both_drain_twice_as_fast()
        {
            var hungry = new Vitals(100f, 0f, 200f);
            hungry.Tick(10f);
            Assert.That(hungry.Health, Is.EqualTo(100f - Vitals.StarvePerSecond * 10f).Within(1e-3));

            var both = new Vitals(100f, 0f, 0f);
            both.Tick(10f);
            Assert.That(both.Health, Is.EqualTo(100f - 2f * Vitals.StarvePerSecond * 10f).Within(1e-3));
        }

        [Test]
        public void Health_comes_back_only_when_well_fed()
        {
            var fed = new Vitals(50f, 400f, 200f);
            fed.Tick(10f);
            Assert.That(fed.Health, Is.EqualTo(50f + Vitals.HealPerSecond * 10f).Within(1e-3));

            var peckish = new Vitals(50f, 100f, 200f); // food under a quarter
            peckish.Tick(10f);
            Assert.That(peckish.Health, Is.EqualTo(50f));
        }

        [Test]
        public void Eating_fills_up_to_the_maximum()
        {
            var v = new Vitals(90f, 490f, 100f);
            v.Consume(new Nourishment(50f, 20f, 15f));
            Assert.That(v.Food, Is.EqualTo(Vitals.MaxFood));
            Assert.That(v.Water, Is.EqualTo(120f));
            Assert.That(v.Health, Is.EqualTo(Vitals.MaxHealth));
        }

        [Test]
        public void Dying_is_announced_once_and_stops_everything()
        {
            var v = new Vitals(10f);
            int deaths = 0;
            v.Died += () => deaths++;
            Assert.That(v.Damage(4f), Is.False);
            Assert.That(v.Damage(30f), Is.True);
            Assert.That(v.Damage(5f), Is.False);
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(v.IsDead, Is.True);

            float food = v.Food;
            v.Tick(100f);
            v.Consume(new Nourishment(100f, 100f, 50f));
            Assert.That(v.Food, Is.EqualTo(food));
            Assert.That(v.Health, Is.EqualTo(0f));
        }

        [Test]
        public void Starving_to_death_is_a_death_too()
        {
            var v = new Vitals(1f, 0f, 0f);
            bool died = false;
            v.Died += () => died = true;
            v.Tick(5f);
            Assert.That(died, Is.True);
        }

        [Test]
        public void Respawning_brings_back_a_fresh_player()
        {
            var v = new Vitals(0f, 0f, 0f);
            Assert.That(v.IsDead, Is.True);
            v.Respawn();
            Assert.That(v.IsDead, Is.False);
            Assert.That(v.Health, Is.EqualTo(60f));
        }

        [Test]
        public void A_bandage_heals_some_at_once_and_the_rest_over_a_few_seconds()
        {
            var s = new Survivor(ItemCatalog.CreateDefault());
            s.Vitals.Set(40f, 400f, 200f);
            s.Inventory.Add(ItemIds.Bandage, 2);
            int slot = -1;
            for (int i = 0; i < s.Inventory.SlotCount && slot < 0; i++)
                if (s.Inventory[i].ItemId == ItemIds.Bandage) slot = i;
            Assert.That(PlayerActions.Eat(s, slot), Is.EqualTo(ActionResult.Ok));
            Assert.That(s.Inventory.Count(ItemIds.Bandage), Is.EqualTo(1));
            Assert.That(s.Vitals.Health, Is.EqualTo(50f), "10 at once");
            Assert.That(s.Vitals.Regen, Is.EqualTo(10f));
            s.Vitals.Tick(1f);
            Assert.That(s.Vitals.Health, Is.EqualTo(50f + Vitals.RegenPerSecond + Vitals.HealPerSecond).Within(0.01f));
            s.Vitals.Tick(10f);
            Assert.That(s.Vitals.Regen, Is.EqualTo(0f));
            Assert.That(s.Vitals.Health, Is.EqualTo(60f + 11f * Vitals.HealPerSecond).Within(0.01f), "the rest came back, no more");
        }

        [Test]
        public void Lasting_healing_stops_at_death()
        {
            var v = new Vitals(30f, 400f, 200f);
            v.Consume(new Nourishment(0f, 0f, 15f, 30f));
            v.Damage(100f);
            Assert.That(v.IsDead, Is.True);
            Assert.That(v.Regen, Is.EqualTo(0f));
            v.Tick(5f);
            Assert.That(v.IsDead, Is.True, "no coming back from a syringe");
        }
    }
}
