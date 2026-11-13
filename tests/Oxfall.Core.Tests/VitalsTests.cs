using NUnit.Framework;
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
    }
}
