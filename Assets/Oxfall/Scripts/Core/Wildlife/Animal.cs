using System;
using Oxfall.Core.Items;

namespace Oxfall.Core.Wildlife
{
    public enum AnimalKind : byte
    {
        /// <summary>Roots about in the woods; charges whoever hurts it.</summary>
        Boar = 1,

        /// <summary>Hunts: comes for anyone who gets close.</summary>
        Wolf = 2,

        /// <summary>Grazes in the open; runs from everyone.</summary>
        Deer = 3,

        /// <summary>
        /// Not an animal: an armed man guarding the ruins. Shoots whoever comes in sight, keeps to his
        /// post, drops what he carried when he falls, and another takes his place a while later.
        /// </summary>
        Bandit = 4,
    }

    public enum AnimalState : byte
    {
        Idle = 0,
        Wander = 1,
        Flee = 2,
        Chase = 3,
        Dead = 4,
    }

    /// <summary>How each kind of animal lives, fights and what its body gives.</summary>
    public static class Animals
    {
        public static bool IsKnown(AnimalKind kind) => kind >= AnimalKind.Boar && kind <= AnimalKind.Bandit;

        public static string Name(AnimalKind kind)
        {
            switch (kind)
            {
                case AnimalKind.Boar: return "Jabalí";
                case AnimalKind.Wolf: return "Lobo";
                case AnimalKind.Bandit: return "Bandido";
                default: return "Ciervo";
            }
        }

        public static float MaxHealth(AnimalKind kind)
        {
            switch (kind)
            {
                case AnimalKind.Boar: return 120f;
                case AnimalKind.Wolf: return 90f;
                case AnimalKind.Bandit: return 100f;
                default: return 80f;
            }
        }

        public const float WalkSpeed = 1.3f;

        /// <summary>Running speed: a wolf runs a little faster than a sprinting player, a deer faster still.</summary>
        public static float RunSpeed(AnimalKind kind)
        {
            switch (kind)
            {
                case AnimalKind.Boar: return 5.2f;
                case AnimalKind.Wolf: return 6.9f;
                case AnimalKind.Bandit: return 4.4f;
                default: return 7.6f;
            }
        }

        /// <summary>Health a bite or a charge takes off a player (0: it never attacks).</summary>
        public static float Damage(AnimalKind kind) => kind == AnimalKind.Boar ? 11f : kind == AnimalKind.Wolf ? 9f : 0f;

        public const float AttackReach = 1.7f;
        public const float AttackSeconds = 1.25f;

        /// <summary>How near a wolf notices a player and comes for them.</summary>
        public const float WolfSight = 20f;

        /// <summary>How near a player makes a deer (or an unhurt boar) bolt.</summary>
        public const float Shyness = 11f;

        /// <summary>A chase given up past this far from where it started.</summary>
        public const float ChaseLeash = 45f;

        /// <summary>How long a carcass lies before it is gone, harvested or not.</summary>
        public const float CarcassSeconds = 300f;

        /// <summary>How long after one is gone another of its kind turns up somewhere.</summary>
        public const float RespawnSeconds = 150f;

        /// <summary>How many good blows a carcass takes to harvest fully.</summary>
        public static int CarcassHits(AnimalKind kind) => kind == AnimalKind.Boar ? 8 : 6;

        /// <summary>
        /// What one blow on a carcass gives. A hatchet cuts best, a rock or spear less; meat and
        /// leather every blow, more from a boar.
        /// </summary>
        public static (string ItemId, int Amount)[] Harvest(AnimalKind kind, ToolKind tool)
        {
            float cut;
            switch (tool)
            {
                case ToolKind.MetalHatchet: cut = 2f; break;
                case ToolKind.Hatchet: cut = 1.5f; break;
                case ToolKind.Rock:
                case ToolKind.Spear:
                case ToolKind.MetalPickaxe:
                case ToolKind.Pickaxe: cut = 1f; break;
                default: return Array.Empty<(string, int)>();
            }
            int meat = kind == AnimalKind.Boar ? 2 : 1;
            int leather = kind == AnimalKind.Deer ? 4 : 3;
            return new[]
            {
                (ItemIds.RawMeat, (int)Math.Round(meat * cut)),
                (ItemIds.Leather, (int)Math.Round(leather * cut)),
            };
        }

        /// <summary>Half the size of its body (x across, y up, z along), for hits and drawing.</summary>
        public static (float X, float Y, float Z) HalfSize(AnimalKind kind)
        {
            switch (kind)
            {
                case AnimalKind.Boar: return (0.3f, 0.42f, 0.65f);
                case AnimalKind.Wolf: return (0.22f, 0.45f, 0.7f);
                case AnimalKind.Bandit: return (0.3f, 0.9f, 0.3f);
                default: return (0.25f, 0.75f, 0.75f);
            }
        }
    }

    /// <summary>How the ruins' bandits see, shoot and come back.</summary>
    public static class Bandits
    {
        /// <summary>How near a bandit notices a player in the open.</summary>
        public const float Sight = 24f;

        /// <summary>How near he shoots from (further, he closes in if he may).</summary>
        public const float Range = 21f;

        /// <summary>He never goes further than this from his post.</summary>
        public const float Leash = 24f;

        /// <summary>A target out of sight this long, or this far, is given up.</summary>
        public const float LoseSeconds = 6f, LoseRange = 34f;

        /// <summary>Seconds between shots, and before the first once he sees someone.</summary>
        public const float FireSeconds = 1.35f, FirstShotSeconds = 0.9f;

        /// <summary>Health a shot that hits takes off (before what the player wears).</summary>
        public const float Damage = 12f;

        /// <summary>How likely a shot is to hit at this distance: likely close, a long shot rarely.</summary>
        public static float HitChance(float distance) => Math.Max(0.2f, Math.Min(0.85f, 0.95f - distance / 30f));

        /// <summary>How long a fallen bandit lies there, and how long before another holds his post.</summary>
        public const float BodySeconds = 10f, RespawnSeconds = 300f;

        /// <summary>Another only turns up when nobody is this near the post.</summary>
        public const float RespawnClearance = 40f;
    }

    /// <summary>One animal on the island: where it is, which way it faces, how it is, what it is doing.</summary>
    public sealed class Animal
    {
        public int Id { get; internal set; }
        public AnimalKind Kind { get; internal set; }
        public float X { get; internal set; }
        public float Y { get; internal set; }
        public float Z { get; internal set; }

        /// <summary>Degrees about up, 0 facing +Z.</summary>
        public float Yaw { get; internal set; }

        public float Health { get; internal set; }
        public AnimalState State { get; internal set; }

        /// <summary>Ground speed now (metres a second), for the legs.</summary>
        public float Speed { get; internal set; }

        /// <summary>Blows left before the carcass is used up.</summary>
        public int Carcass { get; internal set; }

        /// <summary>A bandit's shots so far (each one is drawn and heard where he stands).</summary>
        public int Shots { get; internal set; }

        /// <summary>A bandit, who keeps to his post (it never moves with him).</summary>
        public bool Guard { get; internal set; }

        public bool IsDead => State == AnimalState.Dead;

        // What it is about: a spot to walk to, someone to chase or run from, and timers.
        internal float GoalX, GoalZ;
        internal int Quarry;
        internal float Timer;
        internal float AttackTimer;
        internal float HomeX, HomeZ;
        internal bool Angry;
        internal float Lost;
        internal float PostYaw;

        public override string ToString() => $"{Kind} {Id} ({X:0.0}, {Z:0.0}) {State} {Health:0}";
    }
}
