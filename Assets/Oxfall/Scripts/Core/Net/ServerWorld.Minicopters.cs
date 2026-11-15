using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>The minicopters as the server keeps them: who flies which, crashes, wrecks and their rebuilding.</summary>
    public sealed partial class ServerWorld
    {
        public List<Minicopter> Minicopters { get; private set; }

        /// <summary>A copter moved, or someone got in or out, since they were last sent.</summary>
        public bool MinicoptersChanged { get; set; } = true;

        private readonly Dictionary<int, float> _lastFly = new Dictionary<int, float>();

        private void CreateMinicopters() => Minicopters = Flying.Spawn(Ground);

        /// <summary>The copter this player flies, or null.</summary>
        public Minicopter FlownBy(ushort playerId)
        {
            if (playerId == 0) return null;
            foreach (var m in Minicopters)
                if (m.Driver == playerId) return m;
            return null;
        }

        /// <summary>Gets into a copter, pouring the fuel the player carries into its tank.</summary>
        public ActionResult Mount(ushort playerId, int id)
        {
            if (!_online.TryGetValue(playerId, out var record) || !Players.TryGet(playerId, out var player)) return ActionResult.Invalid;
            if (record.Survivor.Vitals.IsDead) return ActionResult.Dead;
            if (Aboard(playerId)) return ActionResult.Invalid;
            foreach (var m in Minicopters)
            {
                if (m.Id != id) continue;
                if (!Flying.CanMount(m, player.Pose.X, player.Pose.Y, player.Pose.Z)) return ActionResult.TooFar;
                m.Driver = playerId;
                _lastFly[m.Id] = Time;
                var inv = record.Survivor.Inventory;
                int pour = Math.Min(inv.Count(ItemIds.LowGradeFuel), Flying.TankSpace(m));
                if (pour > 0)
                {
                    inv.Remove(ItemIds.LowGradeFuel, pour);
                    m.Fuel += pour;
                }
                Flying.Seat(m, out float x, out float y, out float z);
                Players.Place(playerId, new Pose(x, y, z, player.Pose.Yaw), player.LastMoveTime);
                MinicoptersChanged = true;
                return ActionResult.Ok;
            }
            return ActionResult.Invalid;
        }

        /// <summary>Gets the pilot out beside it (in the air, they fall); the client is told where.</summary>
        public ActionResult Dismount(ushort playerId)
        {
            var m = FlownBy(playerId);
            if (m == null) return ActionResult.Invalid;
            m.Driver = 0;
            MinicoptersChanged = true;
            if (Players.TryGet(playerId, out var player))
            {
                Flying.Landing(m, Ground, out float x, out float y, out float z);
                Players.Place(playerId, new Pose(x, y, z, player.Pose.Yaw), player.LastMoveTime);
            }
            return ActionResult.Ok;
        }

        /// <summary>
        /// The pilot's phone moved their copter: believed at a copter's speed, else it stays and false
        /// says the pilot must be told where it is. Fuel burns by the time flown; a hard landing hurts.
        /// </summary>
        public bool Fly(ushort playerId, float x, float y, float z, float yaw)
        {
            var m = FlownBy(playerId);
            if (m == null) return true;
            if (!_online.TryGetValue(playerId, out var record) || record.Survivor.Vitals.IsDead)
            {
                m.Driver = 0;
                MinicoptersChanged = true;
                return true;
            }
            float dt = Time - (_lastFly.TryGetValue(m.Id, out float last) ? last : Time);
            if (float.IsNaN(yaw) || float.IsInfinity(yaw) || !Flying.Believable(m, x, y, z, Math.Max(dt, 0.1f), Ground))
                return false;
            // Up faster than a copter climbs is only believed while it has fuel to climb with.
            if (m.Fuel <= 0f && y > m.Y + 0.5f) return false;
            _lastFly[m.Id] = Time;
            float floor = Flying.Floor(Ground, x, z);
            bool airborne = y > floor + 0.1f || m.Y > Flying.Floor(Ground, m.X, m.Z) + 0.1f;
            if (dt > 0f)
            {
                float vy = (y - m.Y) / Math.Max(dt, 0.05f);
                if (airborne && m.Fuel > 0f) m.Fuel = Math.Max(0f, m.Fuel - Math.Min(dt, 1f) / Flying.SecondsPerFuel);
                // Came down onto the ground faster than a safe landing.
                if (y <= floor + 0.15f && -vy > Flying.SafeLanding + 1.5f) Crash(m, -vy - Flying.SafeLanding);
                m.Vx = (x - m.X) / Math.Max(dt, 0.05f);
                m.Vy = vy;
                m.Vz = (z - m.Z) / Math.Max(dt, 0.05f);
            }
            m.X = x;
            m.Y = y;
            m.Z = z;
            m.Yaw = ((yaw % 360f) + 360f) % 360f;
            if (m.Driver == playerId && Players.TryGet(playerId, out var player))
            {
                Flying.Seat(m, out float sx, out float sy, out float sz);
                Players.Place(playerId, new Pose(sx, sy, sz, player.Pose.Yaw), player.LastMoveTime);
            }
            MinicoptersChanged = true;
            return true;
        }

        /// <summary>A hard landing: the copter takes the blow, the pilot a third of it; a wreck throws the pilot out.</summary>
        private void Crash(Minicopter m, float overSafe)
        {
            float damage = overSafe * Flying.CrashDamage;
            m.Health = Math.Max(0f, m.Health - damage);
            if (m.Driver != 0 && _online.TryGetValue(m.Driver, out var record))
                record.Survivor.Vitals.Damage(damage / 3f);
            if (m.Wrecked) Wreck(m);
            MinicoptersChanged = true;
        }

        private void Wreck(Minicopter m)
        {
            if (m.Driver != 0) Dismount(m.Driver);
            m.Driver = 0;
            m.Vx = m.Vz = 0f;
            m.RebuiltAt = Time + Flying.RebuildSeconds;
        }

        /// <summary>Copters nobody flies drop to the ground (and sink at sea); pilots gone or dead let go; wrecks come back rebuilt.</summary>
        private void TickMinicopters(float dt)
        {
            foreach (var m in Minicopters)
            {
                if (m.Wrecked)
                {
                    if (m.RebuiltAt > 0f && Time >= m.RebuiltAt)
                    {
                        m.Rebuild();
                        MinicoptersChanged = true;
                    }
                    continue;
                }
                if (m.Driver != 0 && (!_online.TryGetValue(m.Driver, out var record) || record.Survivor.Vitals.IsDead))
                {
                    m.Driver = 0;
                    MinicoptersChanged = true;
                }
                if (m.Driver != 0) continue;
                float floor = Flying.Floor(Ground, m.X, m.Z);
                bool resting = m.Y <= floor + 0.01f && m.Vy >= 0f && Math.Abs(m.Vx) + Math.Abs(m.Vz) < 0.05f;
                if (resting && !Flying.OverWater(Ground, m.X, m.Z)) continue;
                float before = m.Health;
                Flying.Step(m, 0f, 0f, false, m.Yaw, 0f, dt, Ground);
                if (m.Wrecked && before > 0f) Wreck(m);
                MinicoptersChanged = true;
            }
        }
    }
}
