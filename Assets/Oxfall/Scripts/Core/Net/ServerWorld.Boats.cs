using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>The rowboats as the server keeps them: who rows which, and where each rower has taken theirs.</summary>
    public sealed partial class ServerWorld
    {
        public List<Boat> Boats { get; private set; }

        /// <summary>A boat moved, or someone got in or out, since the boats were last sent.</summary>
        public bool BoatsChanged { get; set; } = true;

        private readonly Dictionary<int, float> _lastRow = new Dictionary<int, float>();

        /// <summary>When each motorboat's tank ran dry (it coasts down from its motor's speed a while after).</summary>
        private readonly Dictionary<int, float> _dryAt = new Dictionary<int, float>();

        /// <summary>Seconds a motorboat run dry may still go at its motor's speed (slowing down).</summary>
        private const float CoastSeconds = 6f;

        private void CreateBoats() => Boats = Boating.Spawn(Ground);

        /// <summary>The boat this player rows, or null.</summary>
        public Boat RowedBy(ushort playerId)
        {
            if (playerId == 0) return null;
            foreach (var b in Boats)
                if (b.Driver == playerId) return b;
            return null;
        }

        public ActionResult Board(ushort playerId, int boatId)
        {
            if (!_online.TryGetValue(playerId, out var record) || !Players.TryGet(playerId, out var player)) return ActionResult.Invalid;
            if (record.Survivor.Vitals.IsDead) return ActionResult.Dead;
            if (RowedBy(playerId) != null) return ActionResult.Invalid;
            foreach (var b in Boats)
            {
                if (b.Id != boatId) continue;
                if (!Boating.CanBoard(b, player.Pose.X, player.Pose.Y, player.Pose.Z)) return ActionResult.TooFar;
                b.Driver = playerId;
                _lastRow[b.Id] = Time;
                Boating.Seat(b, out float x, out float y, out float z);
                Players.Place(playerId, new Pose(x, y, z, player.Pose.Yaw), player.LastMoveTime);
                BoatsChanged = true;
                return ActionResult.Ok;
            }
            return ActionResult.Invalid;
        }

        /// <summary>Gets the player out of their boat onto the nearest land (or into the water); the client is told where.</summary>
        public ActionResult LeaveBoat(ushort playerId)
        {
            var b = RowedBy(playerId);
            if (b == null) return ActionResult.Invalid;
            b.Driver = 0;
            BoatsChanged = true;
            if (Players.TryGet(playerId, out var player))
            {
                Boating.Landing(b, Ground, out float x, out float y, out float z);
                Players.Place(playerId, new Pose(x, y, z, player.Pose.Yaw), player.LastMoveTime);
            }
            return ActionResult.Ok;
        }

        /// <summary>
        /// The rower's phone moved their boat: believed at a boat's speed and on water, else the
        /// boat stays and false says the rower must be told where it is.
        /// </summary>
        public bool Row(ushort playerId, float x, float z, float yaw, float speed)
        {
            var b = RowedBy(playerId);
            if (b == null) return true;
            if (_online.TryGetValue(playerId, out var record) && record.Survivor.Vitals.IsDead)
            {
                b.Driver = 0;
                b.Speed = 0f;
                BoatsChanged = true;
                return true;
            }
            float dt = Time - (_lastRow.TryGetValue(b.Id, out float last) ? last : Time);
            bool coasting = b.Kind == BoatKind.Motorboat && _dryAt.TryGetValue(b.Id, out float dry) && Time - dry < CoastSeconds;
            float top = b.Motored || coasting ? Boating.MotorSpeed : Boating.MaxSpeed;
            if (float.IsNaN(yaw) || float.IsInfinity(yaw) || float.IsNaN(speed) || !Boating.Believable(b, x, z, Math.Max(dt, 0.1f), Ground, top: top))
                return false;
            _lastRow[b.Id] = Time;
            if (b.Motored)
            {
                // The motor burns by the way it went: a full throttle's worth for each motor-speed second of water.
                float dx = x - b.X, dz = z - b.Z;
                Boating.Burn(b, (float)Math.Sqrt(dx * dx + dz * dz) / Boating.MotorSpeed);
                if (b.Fuel <= 0f) _dryAt[b.Id] = Time;
            }
            b.X = x;
            b.Z = z;
            b.Yaw = ((yaw % 360f) + 360f) % 360f;
            b.Speed = Math.Max(-Boating.MotorReverse, Math.Min(top, speed));
            Boating.Seat(b, out float sx, out float sy, out float sz);
            if (Players.TryGet(playerId, out var player))
                Players.Place(playerId, new Pose(sx, sy, sz, player.Pose.Yaw), player.LastMoveTime);
            BoatsChanged = true;
            return true;
        }

        /// <summary>Pours the player's fuel into a motorboat they sit in or stand by, as much as its tank takes.</summary>
        public ActionResult Refuel(ushort playerId, int boatId)
        {
            if (!_online.TryGetValue(playerId, out var record) || !Players.TryGet(playerId, out var player)) return ActionResult.Invalid;
            var s = record.Survivor;
            if (s.Vitals.IsDead) return ActionResult.Dead;
            foreach (var b in Boats)
            {
                if (b.Id != boatId) continue;
                if (b.Kind != BoatKind.Motorboat) return ActionResult.Invalid;
                if (!Boating.CanRefuel(b, playerId, player.Pose.X, player.Pose.Y, player.Pose.Z)) return ActionResult.TooFar;
                int pour = Math.Min(s.Inventory.Count(ItemIds.LowGradeFuel), Boating.TankSpace(b));
                if (pour <= 0) return s.Inventory.Count(ItemIds.LowGradeFuel) <= 0 ? ActionResult.NoFuel : ActionResult.Invalid;
                s.Inventory.Remove(ItemIds.LowGradeFuel, pour);
                b.Fuel += pour;
                _dryAt.Remove(b.Id);
                BoatsChanged = true;
                return ActionResult.Ok;
            }
            return ActionResult.Invalid;
        }

        /// <summary>Boats nobody rows drift to a stop; a rower who went away or died leaves theirs.</summary>
        private void TickBoats(float dt)
        {
            foreach (var b in Boats)
            {
                if (b.Driver != 0 && (!_online.TryGetValue(b.Driver, out var record) || record.Survivor.Vitals.IsDead))
                {
                    b.Driver = 0;
                    BoatsChanged = true;
                }
                if (b.Driver != 0 || b.Speed == 0f) continue;
                Boating.Step(b, 0f, 0f, dt, Ground);
                BoatsChanged = true;
            }
        }
    }
}
