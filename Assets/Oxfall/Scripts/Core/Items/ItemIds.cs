namespace Oxfall.Core.Items
{
    /// <summary>Stable item ids. These are saved to disk and sent over the network, so never rename one.</summary>
    public static class ItemIds
    {
        // Resources
        public const string Wood = "wood";
        public const string Stone = "stone";
        public const string MetalOre = "metal_ore";
        public const string Cloth = "cloth";

        // Tools
        public const string Rock = "rock";
        public const string StoneHatchet = "stone_hatchet";
        public const string StonePickaxe = "stone_pickaxe";
        public const string Torch = "torch";

        // Placeables
        public const string BuildingPlan = "building_plan";
        public const string Campfire = "campfire";
    }
}
