using System;

namespace Oxfall.Core.World
{
    /// <summary>Where players wake up: on a beach, facing inland, as in the genre.</summary>
    public static class Spawns
    {
        /// <summary>
        /// A dry, flat spot on the beach, walking in from the sea at an angle chosen by
        /// <paramref name="random"/>. Yaw is in degrees about up, facing the middle of the island.
        /// </summary>
        public static (float X, float Y, float Z, float Yaw) Beach(IslandGround ground, Random random)
        {
            float startAngle = (float)random.NextDouble() * 360f;
            for (int attempt = 0; attempt < 36; attempt++)
            {
                double angle = (startAngle + attempt * 10f) * Math.PI / 180.0;
                float dx = (float)Math.Cos(angle), dz = (float)Math.Sin(angle);
                for (float r = IslandGround.HalfSize - 5f; r > 10f; r -= 2f)
                {
                    float x = dx * r, z = dz * r;
                    if (ground.Height(x, z) < 0.6f) continue;

                    // A few steps further in, so the player starts dry.
                    x -= dx * 6f;
                    z -= dz * 6f;
                    ground.Normal(x, z, out _, out float ny, out _);
                    if (ny < 0.9f) break;
                    float yaw = (float)(Math.Atan2(-dx, -dz) * 180.0 / Math.PI);
                    return (x, ground.Height(x, z) + 0.2f, z, yaw);
                }
            }
            return (0f, ground.Height(0f, 0f) + 1f, 0f, 0f);
        }
    }
}
