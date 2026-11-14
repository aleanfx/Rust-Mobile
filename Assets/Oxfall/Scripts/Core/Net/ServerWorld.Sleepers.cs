using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>
    /// Sleepers, as on the PC game: a survivor whose player has gone away stays where they stood,
    /// asleep, with everything they carried. Anyone can kill them, and what they carried falls into a
    /// bag; whoever comes back to a body killed in its sleep wakes up dead.
    /// </summary>
    public sealed partial class ServerWorld
    {
        private readonly Dictionary<int, SurvivorRecord> _byOwner = new Dictionary<int, SurvivorRecord>();
        private readonly List<SurvivorRecord> _sleepers = new List<SurvivorRecord>();

        /// <summary>Someone fell asleep, woke up or was killed asleep since the server last told everyone.</summary>
        public bool SleepersChanged { get; set; }

        /// <summary>The survivors asleep on the island now: alive, their players away.</summary>
        public IReadOnlyList<SurvivorRecord> Sleepers
        {
            get
            {
                _sleepers.Clear();
                foreach (var record in _records.Values)
                    if (IsAsleep(record)) _sleepers.Add(record);
                return _sleepers;
            }
        }

        private static bool IsAsleep(SurvivorRecord record) => record.PlayerId == 0 && !record.Survivor.Vitals.IsDead;

        public bool TryGetSleeper(int owner, out SurvivorRecord record) =>
            _byOwner.TryGetValue(owner, out record) && IsAsleep(record);

        /// <summary>Who killed a sleeper, for when its player comes back (by owner; not kept over a restart).</summary>
        private readonly Dictionary<int, string> _killedAsleep = new Dictionary<int, string>();

        /// <summary>
        /// The name of whoever killed this survivor in their sleep, once: asked when its player comes back.
        /// Null if they were not.
        /// </summary>
        public string TakeKilledAsleep(int owner)
        {
            if (!_killedAsleep.TryGetValue(owner, out var by)) return null;
            _killedAsleep.Remove(owner);
            return by;
        }

        /// <summary>
        /// A player's blow at a sleeper, judged like one at a player awake: alive and placed, a tool that
        /// hurts and that they have, not faster than it swings, and close enough to the body (which lies
        /// about a metre either way of where it fell asleep).
        /// </summary>
        public bool StrikeSleeper(ushort attackerId, int owner, ToolKind tool, float now, out float damage, out bool killed)
        {
            damage = 0f;
            killed = false;
            if (!Players.TryGet(attackerId, out var attacker) || !attacker.Placed) return false;
            if (!_online.TryGetValue(attackerId, out var a) || a.Survivor.Vitals.IsDead) return false;
            if (!TryGetSleeper(owner, out var sleeper)) return false;
            if (!Enum.IsDefined(typeof(ToolKind), tool) || !Combat.CanHurt(tool) || !a.Survivor.HasTool(tool)) return false;
            if (now - attacker.LastHitTime < Combat.SwingSeconds(tool) * 0.75f) return false;
            float reach = Combat.MeleeReach(tool) + Combat.ReachSlack + SleeperHalfLength;
            if (attacker.Pose.HorizontalDistance(sleeper.Pose) > reach || Math.Abs(attacker.Pose.Y - sleeper.Pose.Y) > 2.2f) return false;

            attacker.LastHitTime = now;
            return HurtSleeper(sleeper, Combat.MeleeDamage(tool), a.Name, out damage, out killed);
        }

        /// <summary>How far the body reaches from where it lies (head one way, feet the other).</summary>
        public const float SleeperHalfLength = 1f;

        private bool HurtSleeper(SurvivorRecord sleeper, float amount, string by, out float damage, out bool killed)
        {
            var vitals = sleeper.Survivor.Vitals;
            damage = Math.Min(amount, vitals.Health);
            vitals.Damage(amount);
            killed = vitals.IsDead;
            if (killed)
            {
                // The bag has already dropped where they lay (see DropBag); the body goes.
                _killedAsleep[sleeper.Owner] = by;
                SleepersChanged = true;
            }
            return true;
        }
    }
}
