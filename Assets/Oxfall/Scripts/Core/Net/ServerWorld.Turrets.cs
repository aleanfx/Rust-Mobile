using System;
using System.Collections.Generic;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>Automatic turrets as the server runs them: each fires at strangers and hostile animals in front of it while it has bullets.</summary>
    public sealed partial class ServerWorld
    {
        private readonly Dictionary<int, float> _turretReady = new Dictionary<int, float>();
        private readonly List<Deployable> _turrets = new List<Deployable>();

        /// <summary>A turret's shot: which turret, at what (a player's id, an animal's id), where it was, how much it took off, whether it killed.</summary>
        public readonly struct TurretShot
        {
            public readonly int Turret;
            public readonly TurretTarget Kind;
            public readonly int Target;
            public readonly float X, Y, Z, Damage;
            public readonly bool Killed;

            public TurretShot(int turret, TurretTarget kind, int target, float x, float y, float z, float damage, bool killed)
            {
                Turret = turret;
                Kind = kind;
                Target = target;
                X = x;
                Y = y;
                Z = z;
                Damage = damage;
                Killed = killed;
            }
        }

        /// <summary>Turrets' shots since the last tick was sent.</summary>
        public List<TurretShot> TurretShots { get; } = new List<TurretShot>();

        private void TickTurrets()
        {
            _turrets.Clear();
            foreach (var d in Settlement.Deployables)
                if (d.Kind == DeployableKind.AutoTurret) _turrets.Add(d);
            foreach (var turret in _turrets)
            {
                var ammo = Settlement.Container(turret.Id);
                if (Turrets.NextRound(ammo) == null) continue;
                // The nearest stranger, else the nearest hostile animal.
                SurvivorRecord target = null;
                ServerState.Player targetPlayer = null;
                float best = float.MaxValue;
                foreach (var record in _online.Values)
                {
                    if (record.Survivor.Vitals.IsDead || !Players.TryGet(record.PlayerId, out var player) || !player.Placed) continue;
                    var p = player.Pose;
                    if (!Turrets.Covers(turret, p.X, p.Y, p.Z)) continue;
                    if (GunTraps.Spares(Settlement, turret, record.Owner, Teams.SameTeam)) continue;
                    float dx = p.X - turret.X, dz = p.Z - turret.Z, d2 = dx * dx + dz * dz;
                    if (d2 >= best || !TurretSees(turret, p.X, p.Y + 1.2f, p.Z)) continue;
                    best = d2;
                    target = record;
                    targetPlayer = player;
                }
                var animal = target == null
                    ? Turrets.NearestAnimal(turret, Fauna.All, (x, y, z) => TurretSees(turret, x, y, z), out _)
                    : null;
                if (target == null && animal == null)
                {
                    Turrets.Idle(turret, _turretReady, Time);
                    continue;
                }
                if (!Turrets.TryFire(turret, ammo, Time, _turretReady, out float damage)) continue;
                if (target != null)
                {
                    float took = target.Survivor.TakeHit(damage);
                    var p = targetPlayer.Pose;
                    TurretShots.Add(new TurretShot(turret.Id, TurretTarget.Player, targetPlayer.Id, p.X, p.Y + 1.2f, p.Z, took, target.Survivor.Vitals.IsDead));
                }
                else
                {
                    float ax = animal.X, ay = animal.Y + 0.5f, az = animal.Z;
                    bool killed = Fauna.Hit(animal.Id, damage, 0, turret.X, turret.Z);
                    TurretShots.Add(new TurretShot(turret.Id, TurretTarget.Animal, animal.Id, ax, ay, az, damage, killed));
                }
            }
            // Turrets that are gone forget their aim.
            _trapGone.Clear();
            foreach (int id in _turretReady.Keys)
                if (!Settlement.TryGet(id, out _)) _trapGone.Add(id);
            foreach (int id in _trapGone) _turretReady.Remove(id);
        }

        /// <summary>Whether nothing (a hill, a ruin, a building's wall) stands between a turret's barrel and a point.</summary>
        private bool TurretSees(Deployable turret, float tx, float ty, float tz)
        {
            float ex = turret.X, ey = turret.Y + Turrets.MuzzleHeight, ez = turret.Z;
            for (int i = 1; i < 10; i++)
            {
                float t = i / 10f;
                if (Ground.Height(ex + (tx - ex) * t, ez + (tz - ez) * t) > ey + (ty - ey) * t) return false;
            }
            if (World.Monuments.LineBlocked(Ground, ex, ey, ez, tx, ty, tz)) return false;
            return Fauna.WallBetween == null || !Fauna.WallBetween(ex, ey, ez, tx, ty, tz);
        }
    }
}
