using NUnit.Framework;
using Oxfall.Core.Environment;
using Oxfall.Core.Net;

namespace Oxfall.Core.Tests
{
    /// <summary>The weather: fair at first, then now and then rain (sometimes a storm) and fog, the same everywhere.</summary>
    public class WeatherTests
    {
        [Test]
        public void Fair_at_first_then_rain_now_and_then()
        {
            Weather.At(7, 30.0, out float rain, out float fog);
            Assert.AreEqual((0f, 0f), (rain, fog), "a fresh island starts fair");

            int samples = 0, wet = 0, storms = 0, foggy = 0;
            float lastRain = 0f, biggestStep = 0f;
            for (double t = Weather.FairStart; t < Weather.FairStart + 3 * 24 * 3600; t += 10)
            {
                Weather.At(20261113, t, out rain, out fog);
                Assert.That(rain, Is.InRange(0f, 1f));
                Assert.That(fog, Is.InRange(0f, 1f));
                samples++;
                if (rain > 0.05f) wet++;
                if (rain >= Weather.Storm) storms++;
                if (fog > 0.3f) foggy++;
                biggestStep = System.Math.Max(biggestStep, System.Math.Abs(rain - lastRain));
                lastRain = rain;
            }
            float share = wet / (float)samples;
            Assert.That(share, Is.InRange(0.1f, 0.45f), "it rains some of the time, not most of it");
            Assert.Greater(storms, 0, "now and then a storm");
            Assert.Greater(foggy, 0, "and some fog");
            Assert.Less(biggestStep, 0.25f, "it changes over minutes, not at once");

            Weather.At(20261113, 12345.0, out float a, out float b);
            Weather.At(20261113, 12345.0, out float c, out float d);
            Assert.AreEqual((a, b), (c, d), "the same seed and time, the same weather");
        }

        [Test]
        public void The_weather_message_round_trips()
        {
            var w = new NetWriter();
            Messages.Weather(w, 0.8f, 0.4f);
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Weather, (MessageType)r.Byte());
            Messages.ReadWeather(r, out float rain, out float fog);
            Assert.AreEqual(0.8f, rain, 0.005f);
            Assert.AreEqual(0.4f, fog, 0.005f);
        }
    }
}
