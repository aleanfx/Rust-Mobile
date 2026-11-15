using System;
using System.Collections.Generic;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;

namespace Oxfall.Core.Rules
{
    /// <summary>How far along a planted crop is.</summary>
    public enum CropStage : byte
    {
        /// <summary>Just come up: a few leaves.</summary>
        Sprout = 0,
        /// <summary>Half grown.</summary>
        Young = 1,
        /// <summary>Full size, not yet ready.</summary>
        Grown = 2,
        /// <summary>Ready to harvest (for <see cref="Crops.RipeSeconds"/>, then it withers).</summary>
        Ripe = 3,
        /// <summary>Left too long: brown and dry, it gives only a seed back.</summary>
        Withered = 4,
    }

    /// <summary>
    /// Farming: seeds (picked from wild hemp and berry bushes) are set down in open ground like
    /// anything else and grow on their own: a sprout, half grown, full size, then ripe. Rain makes
    /// them grow faster. A ripe plant gives much more than a wild one, and seeds to plant again;
    /// left ripe too long it withers. Anyone can harvest a ripe crop (guard your field); only who
    /// planted it can pull it up early, which gives the seed back.
    /// </summary>
    public static class Crops
    {
        /// <summary>Seconds from seed to ripe, in dry weather.</summary>
        public const float HempSeconds = 420f;
        public const float BerrySeconds = 540f;

        /// <summary>How long a ripe crop waits to be harvested before it withers.</summary>
        public const float RipeSeconds = 1800f;

        /// <summary>How much faster crops grow while it rains (rain above <see cref="RainAbove"/>).</summary>
        public const float RainBoost = 1.5f;
        public const float RainAbove = 0.25f;

        /// <summary>How near another crop a seed can go (centre to centre, metres): fields, not heaps.</summary>
        public const float Spacing = 0.7f;

        public static bool IsCrop(DeployableKind kind) => kind == DeployableKind.HempPlant || kind == DeployableKind.BerryPlant;

        /// <summary>Seconds this crop takes from seed to ripe in dry weather.</summary>
        public static float GrowSeconds(DeployableKind kind) =>
            kind == DeployableKind.BerryPlant ? BerrySeconds : HempSeconds;

        /// <summary>How fast crops grow in this much rain (0..1).</summary>
        public static float Rate(float rain) => rain > RainAbove ? RainBoost : 1f;

        /// <summary>Where a crop of this age (seconds of growth) is.</summary>
        public static CropStage Stage(DeployableKind kind, float age)
        {
            float grow = GrowSeconds(kind);
            if (age >= grow + RipeSeconds) return CropStage.Withered;
            if (age >= grow) return CropStage.Ripe;
            if (age >= grow * 2f / 3f) return CropStage.Grown;
            if (age >= grow / 3f) return CropStage.Young;
            return CropStage.Sprout;
        }

        /// <summary>How grown it is, 0 (just planted) to 1 (ripe and after).</summary>
        public static float Growth01(DeployableKind kind, float age) => Math.Max(0f, Math.Min(1f, age / GrowSeconds(kind)));

        /// <summary>Whether harvesting it now gives something (ripe or withered; before that it is only pulled up).</summary>
        public static bool Harvestable(CropStage stage) => stage == CropStage.Ripe || stage == CropStage.Withered;

        /// <summary>The seed a crop grows from.</summary>
        public static string SeedOf(DeployableKind kind) =>
            kind == DeployableKind.BerryPlant ? ItemIds.BerrySeed : ItemIds.HempSeed;

        /// <summary>What harvesting gives: a ripe plant its crop and two seeds, a withered one a seed alone.</summary>
        public static IReadOnlyList<Yield> Harvest(DeployableKind kind, CropStage stage)
        {
            string seed = SeedOf(kind);
            if (stage == CropStage.Withered) return new[] { new Yield(seed, 1) };
            if (stage != CropStage.Ripe) return Array.Empty<Yield>();
            return kind == DeployableKind.BerryPlant
                ? new[] { new Yield(ItemIds.Berries, 8), new Yield(seed, 2) }
                : new[] { new Yield(ItemIds.Cloth, 18), new Yield(seed, 2) };
        }

        /// <summary>The seed a wild plant gives along with what it is picked for (none for anything else).</summary>
        public static string WildSeed(ResourceNodeType type)
        {
            switch (type)
            {
                case ResourceNodeType.Hemp: return ItemIds.HempSeed;
                case ResourceNodeType.BerryBush: return ItemIds.BerrySeed;
                default: return null;
            }
        }

        public static string StageName(CropStage stage)
        {
            switch (stage)
            {
                case CropStage.Sprout: return "Brote";
                case CropStage.Young: return "Creciendo";
                case CropStage.Grown: return "Casi lista";
                case CropStage.Ripe: return "Lista";
                default: return "Seca";
            }
        }
    }
}
