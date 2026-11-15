using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Wildlife;

namespace Oxfall.Core.Rules
{
    /// <summary>What a turret shot at: a player, or an animal (a wolf, a bandit, an angry boar).</summary>
    public enum TurretTarget : byte
    {
        Player = 0,
        Animal = 1,
    }

    /// <summary>
    /// The automatic turret: a rifle on a turning head, fed with rifle or pistol bullets, that guards a
    /// base with no electricity (a battery inside, as a phone game keeps it simple). It covers a wide
    /// half circle in front of it, far out, and fires short bursts at anyone not authorized on the
    /// tool cupboard it stands under, and at wolves, bandits and angry boars. Each shot spends a
    /// bullet; empty, it only watches. Learned at the workbench.
    /// </summary>
    public static class Turrets
    {
        /// <summary>How far it sees and fires, in metres.</summary>
        public const float Range = 30f;

        /// <summary>Within this of it, it turns to a target all the way round (something chewing at its legs).</summary>
        public const float Close = 5f;

        /// <summary>Half the width of what it covers, in degrees either side of where it faces.</summary>
        public const float HalfCone = 90f;

        /// <summary>Seconds between two shots, and the wait before the first at a new target.</summary>
        public const float Interval = 0.3f;
        public const float FirstShot = 0.6f;

        /// <summary>Bullets it holds (slots).</summary>
        public const int Slots = 6;

        /// <summary>A shot's damage with each kind of bullet (before what the target wears takes its share).</summary>
        public const float RifleDamage = 16f;
        public const float PistolDamage = 11f;

        /// <summary>The height of its barrel above the ground it stands on.</summary>
        public const float MuzzleHeight = 1.05f;

        public static bool Takes(string itemId) => itemId == ItemIds.RifleBullet || itemId == ItemIds.PistolBullet;

        /// <summary>The bullet it fires next from what it holds (rifle first), or null if it is empty.</summary>
        public static string NextRound(Inventory ammo)
        {
            if (ammo == null) return null;
            if (ammo.Has(ItemIds.RifleBullet, 1)) return ItemIds.RifleBullet;
            if (ammo.Has(ItemIds.PistolBullet, 1)) return ItemIds.PistolBullet;
            return null;
        }

        public static float Damage(string round) => round == ItemIds.RifleBullet ? RifleDamage : PistolDamage;

        /// <summary>Whether a point is in front of the turret, within its reach and its half circle.</summary>
        public static bool Covers(Deployable turret, float x, float y, float z)
        {
            float dx = x - turret.X, dz = z - turret.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 > Range * Range || Math.Abs(y - turret.Y) > 12f) return false;
            // Right beside it the head swings to anything, whichever way it faces.
            if (d2 < Close * Close) return true;
            double yaw = turret.Yaw * Math.PI / 180.0;
            double along = (dx * Math.Sin(yaw) + dz * Math.Cos(yaw)) / Math.Sqrt(d2);
            return along >= Math.Cos(HalfCone * Math.PI / 180.0) - 1e-6;
        }

        /// <summary>Whether it goes for this animal: wolves and bandits always, a boar once it is angry; never deer.</summary>
        public static bool Hostile(Animal a) =>
            !a.IsDead && (a.Kind == AnimalKind.Wolf || a.Kind == AnimalKind.Bandit || (a.Kind == AnimalKind.Boar && a.Angry));

        /// <summary>
        /// The nearest hostile animal the turret covers and sees (<paramref name="sees"/> from its
        /// barrel to a point), and its squared distance; null if none.
        /// </summary>
        public static Animal NearestAnimal(Deployable turret, IEnumerable<Animal> animals, Func<float, float, float, bool> sees, out float distance2)
        {
            Animal best = null;
            distance2 = float.MaxValue;
            foreach (var a in animals)
            {
                if (!Hostile(a)) continue;
                float y = a.Y + 0.5f;
                if (!Covers(turret, a.X, y, a.Z)) continue;
                float dx = a.X - turret.X, dz = a.Z - turret.Z, d2 = dx * dx + dz * dz;
                if (d2 >= distance2 || (sees != null && !sees(a.X, y, a.Z))) continue;
                distance2 = d2;
                best = a;
            }
            return best;
        }

        /// <summary>
        /// One turret's turn: if it is ready and has a bullet, spends one at the animal given and says
        /// what it did. <paramref name="ready"/> keeps when each turret may fire next.
        /// </summary>
        public static bool TryFire(Deployable turret, Inventory ammo, float now, Dictionary<int, float> ready, out float damage)
        {
            damage = 0f;
            if (ready.TryGetValue(turret.Id, out float at) && now < at) return false;
            string round = NextRound(ammo);
            if (round == null) return false;
            // A new target: a moment to swing round onto it.
            if (!ready.ContainsKey(turret.Id))
            {
                ready[turret.Id] = now + FirstShot;
                return false;
            }
            ammo.Remove(round, 1);
            ready[turret.Id] = now + Interval;
            damage = Damage(round);
            return true;
        }

        /// <summary>A turret with nothing to shoot at forgets it was aiming (the next target waits the swing again).</summary>
        public static void Idle(Deployable turret, Dictionary<int, float> ready, float now)
        {
            if (ready.TryGetValue(turret.Id, out float at) && now >= at + 1f) ready.Remove(turret.Id);
        }
    }
}
