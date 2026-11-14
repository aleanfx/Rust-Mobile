using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;

namespace Oxfall.Core.Net
{
    /// <summary>
    /// The bow on the server: a shot is judged and its arrow spent when it is loosed; where it lands is
    /// the shooter's phone's to say (it flies there among the real colliders), checked against the shot
    /// it belongs to: the oldest of theirs still in the air, from close enough to have hit.
    /// </summary>
    public sealed partial class ServerWorld
    {
        private readonly Dictionary<ushort, Queue<(float X, float Y, float Z, float Time)>> _arrows =
            new Dictionary<ushort, Queue<(float, float, float, float)>>();

        /// <summary>Arrows a player may have in the air at once (more are forgotten, oldest first).</summary>
        private const int MaxArrowsInFlight = 6;

        /// <summary>
        /// A player looses an arrow from (x, y, z): alive, holding a bow they have, an arrow to spend, not
        /// faster than a bow draws, and from about where their eyes are. The arrow is spent. False if refused.
        /// </summary>
        public bool Shoot(ushort playerId, float x, float y, float z, float now)
        {
            if (!Players.TryGet(playerId, out var player) || !player.Placed) return false;
            if (!_online.TryGetValue(playerId, out var record) || record.Survivor.Vitals.IsDead) return false;
            var s = record.Survivor;
            if (!s.HasTool(ToolKind.Bow) || s.Inventory.Count(ItemIds.Arrow) < 1) return false;
            if (now - player.LastHitTime < Combat.ShotSeconds * 0.75f) return false;
            var p = player.Pose;
            float dx = x - p.X, dz = z - p.Z;
            if (dx * dx + dz * dz > 2.5f * 2.5f || Math.Abs(y - (p.Y + 1.6f)) > 2.5f) return false;

            player.LastHitTime = now;
            s.Inventory.Remove(ItemIds.Arrow, 1);
            Spend(record, player, ToolKind.Bow);
            if (!_arrows.TryGetValue(playerId, out var flying)) _arrows[playerId] = flying = new Queue<(float, float, float, float)>();
            flying.Enqueue((x, y, z, now));
            while (flying.Count > MaxArrowsInFlight) flying.Dequeue();
            return true;
        }

        /// <summary>
        /// Where a player's arrow landed. A player, a sleeper or a living animal in range of the shot is hurt
        /// (<paramref name="damage"/>, <paramref name="killed"/>; for an animal also <paramref name="blow"/>).
        /// False when it hurt nobody: the ground, no shot in the air, or a target out of reach of it.
        /// </summary>
        public bool ArrowHit(ushort playerId, ArrowTarget kind, int targetId, float now, out float damage, out bool killed, out AnimalBlow blow)
        {
            damage = 0f;
            killed = false;
            blow = AnimalBlow.None;
            if (!_arrows.TryGetValue(playerId, out var flying)) return false;
            while (flying.Count > 0 && now - flying.Peek().Time > Combat.ArrowFlightSeconds) flying.Dequeue();
            if (flying.Count == 0) return false;
            var shot = flying.Dequeue();
            float reach = Combat.BowRange + Combat.ReachSlack * 2f;

            if (kind == ArrowTarget.Player)
            {
                if (targetId <= 0 || targetId > ushort.MaxValue || targetId == playerId) return false;
                var targetKey = (ushort)targetId;
                if (!Players.TryGet(targetKey, out var target) || !target.Placed) return false;
                if (!_online.TryGetValue(targetKey, out var t) || t.Survivor.Vitals.IsDead) return false;
                if (Distance(shot.X, shot.Z, target.Pose.X, target.Pose.Z) > reach) return false;
                damage = t.Survivor.TakeHit(Combat.ArrowDamage);
                killed = t.Survivor.Vitals.IsDead;
                return true;
            }
            if (kind == ArrowTarget.Sleeper)
            {
                if (!TryGetSleeper(targetId, out var sleeper)) return false;
                if (Distance(shot.X, shot.Z, sleeper.Pose.X, sleeper.Pose.Z) > reach + SleeperHalfLength) return false;
                string by = _online.TryGetValue(playerId, out var shooter) ? shooter.Name : null;
                return HurtSleeper(sleeper, Combat.ArrowDamage, by, out damage, out killed);
            }
            if (kind == ArrowTarget.Animal)
            {
                if (!Fauna.TryGet(targetId, out var animal) || animal.IsDead) return false;
                if (Distance(shot.X, shot.Z, animal.X, animal.Z) > reach) return false;
                damage = Math.Min(Combat.ArrowDamage, animal.Health);
                killed = Fauna.Hit(targetId, Combat.ArrowDamage, playerId, shot.X, shot.Z);
                blow = killed ? AnimalBlow.Killed : AnimalBlow.Hurt;
                return true;
            }
            return false;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
