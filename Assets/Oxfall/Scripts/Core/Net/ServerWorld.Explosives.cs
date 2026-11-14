using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>
    /// Satchel charges on the server: a throw is judged (a charge to spend, close enough, stuck to
    /// something that is there) and the charge kept until its fuse burns down; then the blast takes its
    /// bite out of what it stuck to and hurts whoever stands near, awake or asleep.
    /// </summary>
    public sealed partial class ServerWorld
    {
        private readonly List<LiveCharge> _charges = new List<LiveCharge>();
        private readonly Dictionary<ushort, float> _lastThrow = new Dictionary<ushort, float>();
        private readonly List<SurvivorRecord> _near = new List<SurvivorRecord>();
        private int _nextCharge = 1;

        /// <summary>Charges that went off since the caller last emptied this, for everyone to see and hear.</summary>
        public List<LiveCharge> Blasts { get; } = new List<LiveCharge>();

        /// <summary>Players the blasts hurt: who, whose charge, how much, and whether it killed them. The caller empties it.</summary>
        public List<(ushort Target, ushort By, float Damage, bool Killed)> BlastHurts { get; } =
            new List<(ushort, ushort, float, bool)>();

        /// <summary>Sleepers the blasts hurt: whose body, whose charge (id and name), how much, whether it died. The caller empties it.</summary>
        public List<(int Owner, ushort By, string ByName, float Damage, bool Killed)> BlastSleeperHits { get; } =
            new List<(int, ushort, string, float, bool)>();

        /// <summary>Charges stuck somewhere, their fuses still burning.</summary>
        public IReadOnlyList<LiveCharge> LiveCharges => _charges;

        /// <summary>
        /// A player throws a charge that sticks at (x, y, z) to <paramref name="target"/>, the surface
        /// facing (nx, ny, nz). Alive, with a charge to spend, not faster than one a second, within a
        /// throw of their eyes and stuck to something that is there. The charge is spent. False if refused.
        /// </summary>
        public bool Throw(ushort playerId, ChargeTarget target, float x, float y, float z, float nx, float ny, float nz,
            float now, out LiveCharge charge)
        {
            charge = default;
            if (!Players.TryGet(playerId, out var player) || !player.Placed) return false;
            if (!_online.TryGetValue(playerId, out var record) || record.Survivor.Vitals.IsDead) return false;
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) || !IsFinite(nx) || !IsFinite(ny) || !IsFinite(nz)) return false;
            var s = record.Survivor;
            if (!s.Inventory.Has(ItemIds.SatchelCharge, 1)) return false;
            if (_lastThrow.TryGetValue(playerId, out float last) && now - last < Explosives.ThrowSeconds * 0.75f) return false;
            var p = player.Pose;
            float dx = x - p.X, dy = y - (p.Y + 1.6f), dz = z - p.Z, reach = Explosives.ThrowReach + Combat.ReachSlack;
            if (dx * dx + dy * dy + dz * dz > reach * reach) return false;
            if (!Explosives.CanStick(Settlement.Grid, target, x, y, z)) return false;

            s.Inventory.Remove(ItemIds.SatchelCharge, 1);
            _lastThrow[playerId] = now;
            float length = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length < 1e-3f) (nx, ny, nz, length) = (0f, 1f, 0f, 1f);
            charge = new LiveCharge(_nextCharge++, playerId, x, y, z, nx / length, ny / length, nz / length, target,
                Time + Explosives.FuseSeconds);
            _charges.Add(charge);
            return true;
        }

        /// <summary>Burns the fuses down and sets off those that reach the end.</summary>
        private void TickCharges()
        {
            for (int i = 0; i < _charges.Count;)
            {
                if (Time < _charges[i].GoesOffAt)
                {
                    i++;
                    continue;
                }
                var charge = _charges[i];
                _charges.RemoveAt(i);
                Detonate(charge);
            }
        }

        private void Detonate(LiveCharge c)
        {
            Blasts.Add(c);
            // The building first: what falls is announced with the rest of this tick's changes. Then
            // what is set down near it, unless a wall still stands between.
            Explosives.Blast(Settlement.Grid, c.Target, Announcements);
            Explosives.Splash(Settlement, c.X, c.Y, c.Z, Announcements);

            string byName = _online.TryGetValue(c.Thrower, out var thrower) ? thrower.Name : null;
            foreach (var record in _online.Values)
            {
                if (record.Survivor.Vitals.IsDead || !Players.TryGet(record.PlayerId, out var p) || !p.Placed) continue;
                float amount = Explosives.DamageToPlayer(Distance3(c, p.Pose.X, p.Pose.Y + 0.9f, p.Pose.Z));
                if (amount <= 0f || Explosives.Shielded(Settlement.Grid, c.X, c.Y, c.Z, p.Pose.X, p.Pose.Y + 0.9f, p.Pose.Z)) continue;
                float done = record.Survivor.TakeHit(amount);
                BlastHurts.Add((record.PlayerId, c.Thrower, done, record.Survivor.Vitals.IsDead));
            }
            // Sleepers lie on the ground; hurting one may change the list, so it is copied first.
            _near.Clear();
            foreach (var sleeper in Sleepers)
                if (Explosives.DamageToPlayer(Distance3(c, sleeper.Pose.X, sleeper.Pose.Y + 0.3f, sleeper.Pose.Z)) > 0f
                    && !Explosives.Shielded(Settlement.Grid, c.X, c.Y, c.Z, sleeper.Pose.X, sleeper.Pose.Y + 0.3f, sleeper.Pose.Z)) _near.Add(sleeper);
            foreach (var sleeper in _near)
            {
                float amount = Explosives.DamageToPlayer(Distance3(c, sleeper.Pose.X, sleeper.Pose.Y + 0.3f, sleeper.Pose.Z));
                HurtSleeper(sleeper, amount, byName, out float damage, out bool killed);
                BlastSleeperHits.Add((sleeper.Owner, c.Thrower, byName, damage, killed));
            }
        }

        private static float Distance3(LiveCharge c, float x, float y, float z)
        {
            float dx = c.X - x, dy = c.Y - y, dz = c.Z - z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) < 100000f;
    }
}
