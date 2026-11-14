using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;

namespace Oxfall.Core.Net
{
    /// <summary>The island's animals as the server runs them for everyone, and the players' blows at them.</summary>
    public sealed partial class ServerWorld
    {
        private readonly List<Fauna.Prey> _prey = new List<Fauna.Prey>();
        private readonly List<Fauna.Bite> _bites = new List<Fauna.Bite>();

        public Fauna Fauna { get; }

        /// <summary>Animals' attacks that landed since the last tick was sent: on whom, what bit them, how much, and whether it killed them.</summary>
        public List<(ushort Target, AnimalKind Kind, float Damage, bool Killed)> Bites { get; } = new List<(ushort, AnimalKind, float, bool)>();

        /// <summary>Animals within this far of a player are sent to them.</summary>
        public const float AnimalRange = 160f;

        private void TickFauna(float deltaSeconds)
        {
            _prey.Clear();
            foreach (var record in _online.Values)
            {
                if (record.Survivor.Vitals.IsDead || !Players.TryGet(record.PlayerId, out var player) || !player.Placed) continue;
                _prey.Add(new Fauna.Prey(record.PlayerId, player.Pose.X, player.Pose.Y, player.Pose.Z));
            }
            _bites.Clear();
            Fauna.Tick(deltaSeconds, _prey, _bites);
            foreach (var bite in _bites)
            {
                // A bandit's shot that missed: only the shot is seen (his count of shots).
                if (bite.Damage <= 0f) continue;
                if (!_online.TryGetValue((ushort)bite.Target, out var record) || record.Survivor.Vitals.IsDead) continue;
                if (!Fauna.TryGet(bite.Animal, out var animal)) continue;
                float damage = record.Survivor.TakeHit(bite.Damage);
                Bites.Add(((ushort)bite.Target, animal.Kind, damage, record.Survivor.Vitals.IsDead));
            }
        }

        /// <summary>
        /// A player's blow at an animal with the tool in hand, judged here like a blow at a player: alive,
        /// within reach (the animal's length counts), not faster than the tool swings, a tool they have.
        /// A living animal is hurt (<paramref name="damage"/>) and may die; a carcass is cut up and what it
        /// gives goes into their inventory (<paramref name="lost"/>: what did not fit).
        /// </summary>
        public AnimalBlow StrikeAnimal(ushort playerId, int animalId, ToolKind tool, float now, out float damage, out int lost)
        {
            damage = 0f;
            lost = 0;
            if (!Players.TryGet(playerId, out var player) || !player.Placed) return AnimalBlow.None;
            if (!_online.TryGetValue(playerId, out var record) || record.Survivor.Vitals.IsDead) return AnimalBlow.None;
            if (!Fauna.TryGet(animalId, out var animal)) return AnimalBlow.None;
            if (!Enum.IsDefined(typeof(ToolKind), tool) || !record.Survivor.HasTool(tool)) return AnimalBlow.None;
            if (now - player.LastHitTime < Math.Min(MinHitInterval, Combat.SwingSeconds(tool) * 0.75f)) return AnimalBlow.None;
            float dx = player.Pose.X - animal.X, dz = player.Pose.Z - animal.Z;
            float reach = Combat.MeleeReach(tool) + Wildlife.Animals.HalfSize(animal.Kind).Z + Combat.ReachSlack;
            if (dx * dx + dz * dz > reach * reach || Math.Abs(player.Pose.Y - animal.Y) > 2.5f) return AnimalBlow.None;

            if (animal.IsDead)
            {
                if (animal.Carcass <= 0) return AnimalBlow.None;
                player.LastHitTime = now;
                var yields = Fauna.Harvest(animalId, tool);
                if (yields.Length == 0) return AnimalBlow.NeedsBlade;
                foreach (var (item, amount) in yields)
                    if (amount > 0) lost += record.Survivor.Inventory.Add(item, amount);
                return animal.Carcass > 0 ? AnimalBlow.Cut : AnimalBlow.UsedUp;
            }
            if (!Combat.CanHurt(tool)) return AnimalBlow.None;
            player.LastHitTime = now;
            damage = Math.Min(Combat.MeleeDamage(tool), animal.Health);
            return Fauna.Hit(animalId, Combat.MeleeDamage(tool), playerId, player.Pose.X, player.Pose.Z) ? AnimalBlow.Killed : AnimalBlow.Hurt;
        }
    }
}
