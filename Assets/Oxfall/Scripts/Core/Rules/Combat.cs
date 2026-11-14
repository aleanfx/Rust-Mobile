using Oxfall.Core.Items;

namespace Oxfall.Core.Rules
{
    /// <summary>
    /// Hitting other players: how much each thing in hand takes off a 100-health survivor, how far it
    /// reaches and how often it swings. Numbers follow the PC game's feel: a rock needs ten blows, a
    /// stone tool about seven, a metal one four or five, a wooden spear three.
    /// </summary>
    public static class Combat
    {
        /// <summary>Health taken by one blow of this, on a player.</summary>
        public static float MeleeDamage(ToolKind tool)
        {
            switch (tool)
            {
                case ToolKind.None: return 5f;
                case ToolKind.Rock: return 10f;
                case ToolKind.Torch: return 9f;
                case ToolKind.Hatchet: return 15f;
                case ToolKind.Pickaxe: return 16f;
                case ToolKind.MetalHatchet: return 22f;
                case ToolKind.MetalPickaxe: return 24f;
                case ToolKind.Spear: return 34f;
                // The hammer builds; it does not fight.
                default: return 0f;
            }
        }

        /// <summary>How far a blow reaches, from the eyes, in metres.</summary>
        public static float MeleeReach(ToolKind tool) => tool == ToolKind.Spear ? 3.3f : 2.7f;

        /// <summary>Seconds between blows.</summary>
        public static float SwingSeconds(ToolKind tool) => tool == ToolKind.Spear ? 1.0f : 0.7f;

        /// <summary>
        /// Leeway the server allows on reach, for lag: the two players are seen where they were a
        /// moment ago, each by the other.
        /// </summary>
        public const float ReachSlack = 1.6f;

        /// <summary>Whether a blow with this hurts players at all.</summary>
        public static bool CanHurt(ToolKind tool) => MeleeDamage(tool) > 0f;
    }
}
