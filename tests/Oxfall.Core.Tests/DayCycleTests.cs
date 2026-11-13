using NUnit.Framework;
using Oxfall.Core.Environment;

namespace Oxfall.Core.Tests
{
    public class DayCycleTests
    {
        [Test]
        public void The_day_and_the_night_run_at_their_own_pace()
        {
            // 14 hours of day in 1400 s (1 h per 100 s), 10 hours of night in 500 s (1 h per 50 s).
            var cycle = new DayCycle(8f, 1400f, 500f);
            cycle.Tick(100f);
            Assert.That(cycle.Hours, Is.EqualTo(9f).Within(1e-3));

            cycle.Set(21f);
            cycle.Tick(50f);
            Assert.That(cycle.Hours, Is.EqualTo(22f).Within(1e-3));
        }

        [Test]
        public void A_tick_across_sunset_changes_pace_there()
        {
            var cycle = new DayCycle(19f, 1400f, 500f);
            cycle.Tick(100f + 50f); // one day hour to sunset, then one night hour
            Assert.That(cycle.Hours, Is.EqualTo(21f).Within(1e-3));
            Assert.That(cycle.IsNight, Is.True);
        }

        [Test]
        public void A_whole_cycle_comes_back_to_the_same_hour()
        {
            var cycle = new DayCycle(10f, 1800f, 600f);
            cycle.Tick(2400f);
            Assert.That(cycle.Hours, Is.EqualTo(10f).Within(1e-2));
        }

        [Test]
        public void The_clock_wraps_past_midnight()
        {
            var cycle = new DayCycle(23.5f, 1400f, 500f);
            cycle.Tick(50f);
            Assert.That(cycle.Hours, Is.EqualTo(0.5f).Within(1e-3));
            cycle.Set(-1f);
            Assert.That(cycle.Hours, Is.EqualTo(23f).Within(1e-3));
            cycle.Set(30f);
            Assert.That(cycle.Hours, Is.EqualTo(6f).Within(1e-3));
        }

        [TestCase(5.9f, true)]
        [TestCase(6f, false)]
        [TestCase(13f, false)]
        [TestCase(19.99f, false)]
        [TestCase(20f, true)]
        [TestCase(0f, true)]
        public void Night_is_from_sunset_to_sunrise(float hours, bool night)
        {
            Assert.That(new DayCycle(hours).IsNight, Is.EqualTo(night));
        }

        [Test]
        public void The_sun_is_highest_at_one_pm_and_down_at_night()
        {
            Assert.That(new DayCycle(13f).SunElevation, Is.EqualTo(DayCycle.SunPeak).Within(1e-3));
            Assert.That(new DayCycle(6f).SunElevation, Is.EqualTo(0f).Within(1e-3));
            Assert.That(new DayCycle(9f).SunElevation, Is.GreaterThan(30f));
            Assert.That(new DayCycle(23f).SunElevation, Is.EqualTo(0f));
            Assert.That(new DayCycle(1f).MoonElevation, Is.EqualTo(DayCycle.MoonPeak).Within(1e-3));
            Assert.That(new DayCycle(12f).MoonElevation, Is.EqualTo(0f));
        }

        [Test]
        public void Daylight_fades_in_and_out_over_an_hour()
        {
            Assert.That(new DayCycle(3f).Daylight, Is.EqualTo(0f));
            Assert.That(new DayCycle(6.5f).Daylight, Is.EqualTo(0.5f).Within(1e-3));
            Assert.That(new DayCycle(12f).Daylight, Is.EqualTo(1f));
            Assert.That(new DayCycle(19.5f).Daylight, Is.EqualTo(0.5f).Within(1e-3));
        }

        [Test]
        public void The_clock_reads_hours_and_minutes()
        {
            Assert.That(new DayCycle(14.25f).Clock, Is.EqualTo("14:15"));
            Assert.That(new DayCycle(0f).Clock, Is.EqualTo("00:00"));
            Assert.That(new DayCycle(9.5f).Clock, Is.EqualTo("09:30"));
        }
    }
}
