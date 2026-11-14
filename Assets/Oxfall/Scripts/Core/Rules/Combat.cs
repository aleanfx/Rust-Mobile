using Oxfall.Core.Items;

namespace Oxfall.Core.Rules
{
    /// <summary>What an arrow landed on.</summary>
    public enum ArrowTarget : byte
    {
        /// <summary>The ground, a tree, a wall: nothing that bleeds.</summary>
        None = 0,
        Player = 1,
        Animal = 2,
        /// <summary>A survivor asleep (their player away): the target is their owner number.</summary>
        Sleeper = 3,
    }

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

        // ---- The bow

        /// <summary>Health an arrow takes off a player or an animal: three for a survivor, a deer or a wolf, four for a boar.</summary>
        public const float ArrowDamage = 35f;

        /// <summary>How fast an arrow leaves the bow (m/s) and how hard it falls (m/s²): it drops about a metre over 40 m.</summary>
        public const float ArrowSpeed = 55f;
        public const float ArrowGravity = 6f;

        /// <summary>The farthest an arrow is believed to hit anything (the server's check).</summary>
        public const float BowRange = 80f;

        /// <summary>Seconds to draw before the arrow flies, and the least between two shots.</summary>
        public const float DrawSeconds = 0.75f;
        public const float ShotSeconds = 1.0f;

        /// <summary>How long after a shot its arrow may still be said to land (the longest flight, with room for lag).</summary>
        public const float ArrowFlightSeconds = 4f;

        public static bool Shoots(ToolKind tool) => tool == ToolKind.Bow;
    }
}
