using System;
using System.Collections.Generic;
using Oxfall.Core.Gathering;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;

namespace Oxfall.Core.Net
{
    /// <summary>The patrol helicopter on the server: it comes now and then, hunts whoever it sees, and drops crates where it is shot down.</summary>
    public sealed partial class ServerWorld
    {
        private readonly List<Fauna.Prey> _heliPrey = new List<Fauna.Prey>();
        private readonly List<HeliShot> _heliShots = new List<HeliShot>();
        private readonly List<PatrolHeli> _heliCrashed = new List<PatrolHeli>();

        public PatrolHelis Helis { get; private set; }

        /// <summary>What the helicopters fired since the last tick was sent, with what each did (damage after armour, whether it killed).</summary>
        public List<(HeliShot Shot, float Damage, bool Killed)> HeliShots { get; } = new List<(HeliShot, float, bool)>();

        /// <summary>Who brought a helicopter down (its id, their name), since last told.</summary>
        public List<(int Heli, string By)> HelisDowned { get; } = new List<(int, string)>();

        private void CreateHelis(int seed)
        {
            Helis = new PatrolHelis(Ground, seed)
            {
                Shielded = (x0, y0, z0, x1, y1, z1) => Explosives.Shielded(Settlement, x0, y0, z0, x1, y1, z1),
                // What a rocket breaks is told with the rest of this tick's changes.
                RocketBurst = (heli, x, y, z) => Explosives.RocketBlast(Settlement, x, y, z, Announcements),
            };
        }

        private void TickHelis(float deltaSeconds)
        {
            _heliPrey.Clear();
            foreach (var record in _online.Values)
            {
                if (record.Survivor.Vitals.IsDead || !Players.TryGet(record.PlayerId, out var player) || !player.Placed) continue;
                _heliPrey.Add(new Fauna.Prey(record.PlayerId, player.Pose.X, player.Pose.Y, player.Pose.Z));
            }
            _heliShots.Clear();
            _heliCrashed.Clear();
            Helis.Tick(Time, deltaSeconds, _heliPrey, _heliShots, null, _heliCrashed);
            foreach (var shot in _heliShots)
            {
                float dealt = 0f;
                bool killed = false;
                if (shot.Target != 0 && shot.Damage > 0f && _online.TryGetValue((ushort)shot.Target, out var record) && !record.Survivor.Vitals.IsDead)
                {
                    dealt = record.Survivor.TakeHit(shot.Damage);
                    killed = record.Survivor.Vitals.IsDead;
                }
                HeliShots.Add((shot, dealt, killed));
            }
            foreach (var wreck in _heliCrashed) DropHeliCrates(wreck);
        }

        /// <summary>A wreck down: its crates round it, too hot to open for a while (everyone is told of each and its heat).</summary>
        private void DropHeliCrates(PatrolHeli wreck)
        {
            for (int i = 0; i < PatrolHelis.Crates; i++)
            {
                Helis.CrateSpot(wreck, i, out float x, out float y, out float z, out float yaw);
                var crate = Settlement.PlaceHeliCrate(x, y, z, yaw, LootTable.HeliCrate(_random));
                Announcements.Add(BuildOp.Hack(crate.Id, PatrolHelis.CrateBurnSeconds));
            }
        }

        /// <summary>A player's shot struck a helicopter: it turns on them. True if that brought it down.</summary>
        private bool HurtHeli(PatrolHeli heli, float damage, bool rotor, ushort playerId)
        {
            if (damage <= 0f || heli.Down) return false;
            float x = 0f, y = 0f, z = 0f;
            if (Players.TryGet(playerId, out var player))
            {
                x = player.Pose.X;
                y = player.Pose.Y;
                z = player.Pose.Z;
            }
            if (!Helis.Hit(heli.Id, damage, rotor, playerId, x, y, z, Time)) return false;
            HelisDowned.Add((heli.Id, _online.TryGetValue(playerId, out var record) ? record.Name : null));
            return true;
        }

        private ActionResult DevHeli(int a, ServerState.Player player)
        {
            switch (a)
            {
                case 0:
                    Helis.Launch(Time);
                    return ActionResult.Ok;
                case 1:
                    Helis.Launch(Time, player.Pose.X, player.Pose.Z, 150f);
                    return ActionResult.Ok;
                case 2:
                case 3:
                    foreach (var h in Helis.All)
                        if (!h.Down)
                        {
                            Helis.SetHealth(h.Id, a == 2 ? PatrolHelis.MaxHealth * 0.25f : 0f, Time);
                            if (a == 3) HelisDowned.Add((h.Id, null));
                        }
                    return ActionResult.Ok;
                case 4:
                    Helis.Clear();
                    return ActionResult.Ok;
                default: return ActionResult.Invalid;
            }
        }
    }
}
