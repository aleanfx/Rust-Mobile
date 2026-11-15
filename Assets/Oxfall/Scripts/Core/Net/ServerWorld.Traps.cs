using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>Shotgun traps as the server runs them: each fires at a stranger in front of it while it has shells.</summary>
    public sealed partial class ServerWorld
    {
        private readonly Dictionary<int, float> _trapReady = new Dictionary<int, float>();
        private readonly List<int> _trapGone = new List<int>();
        private readonly List<Deployable> _traps = new List<Deployable>();

        /// <summary>Traps' shots since the last tick was sent: which trap, at whom, how much it took off, and whether it killed them.</summary>
        public List<(int Trap, ushort Target, float Damage, bool Killed)> TrapShots { get; } = new List<(int, ushort, float, bool)>();

        private void TickTraps()
        {
            // Listed first: a shot that kills drops a bag, which adds to the island's deployables.
            _traps.Clear();
            foreach (var d in Settlement.Deployables)
                if (d.Kind == DeployableKind.ShotgunTrap) _traps.Add(d);
            foreach (var trap in _traps)
            {
                if (_trapReady.TryGetValue(trap.Id, out float ready) && Time < ready) continue;
                var shells = Settlement.Container(trap.Id);
                if (shells == null || shells.Count(ItemIds.Shell) <= 0) continue;
                // The nearest stranger in front of it, in sight of its barrel.
                SurvivorRecord target = null;
                ServerState.Player targetPlayer = null;
                float best = float.MaxValue;
                foreach (var record in _online.Values)
                {
                    if (record.Survivor.Vitals.IsDead || !Players.TryGet(record.PlayerId, out var player) || !player.Placed) continue;
                    var p = player.Pose;
                    if (!GunTraps.Covers(trap, p.X, p.Y, p.Z)) continue;
                    if (GunTraps.Spares(Settlement, trap, record.Owner, Teams.SameTeam)) continue;
                    float dx = p.X - trap.X, dz = p.Z - trap.Z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= best || !TrapSees(trap, p)) continue;
                    best = d2;
                    target = record;
                    targetPlayer = player;
                }
                if (target == null) continue;
                shells.Remove(ItemIds.Shell, 1);
                _trapReady[trap.Id] = Time + GunTraps.Interval;
                float damage = target.Survivor.TakeHit(GunTraps.DamageAt((float)Math.Sqrt(best)));
                TrapShots.Add((trap.Id, targetPlayer.Id, damage, target.Survivor.Vitals.IsDead));
            }
            // Traps that are gone forget when they last fired.
            _trapGone.Clear();
            foreach (int id in _trapReady.Keys)
                if (!Settlement.TryGet(id, out _)) _trapGone.Add(id);
            foreach (int id in _trapGone) _trapReady.Remove(id);
        }

        /// <summary>Whether nothing (a hill, a ruin's wall, a wall of a building) stands between a trap's barrel and a player's chest.</summary>
        private bool TrapSees(Deployable trap, Pose target)
        {
            float ex = trap.X, ey = trap.Y + GunTraps.MuzzleHeight, ez = trap.Z;
            float tx = target.X, ty = target.Y + 1.2f, tz = target.Z;
            for (int i = 1; i < 6; i++)
            {
                float t = i / 6f;
                if (Ground.Height(ex + (tx - ex) * t, ez + (tz - ez) * t) > ey + (ty - ey) * t) return false;
            }
            if (World.Monuments.LineBlocked(Ground, ex, ey, ez, tx, ty, tz)) return false;
            return Fauna.WallBetween == null || !Fauna.WallBetween(ex, ey, ez, tx, ty, tz);
        }
    }
}
