using System;
using System.Collections.Generic;
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
            if (float.IsNaN(yaw) || float.IsInfinity(yaw) || float.IsNaN(speed) || !Boating.Believable(b, x, z, Math.Max(dt, 0.1f), Ground))
                return false;
            _lastRow[b.Id] = Time;
            b.X = x;
            b.Z = z;
            b.Yaw = ((yaw % 360f) + 360f) % 360f;
            b.Speed = Math.Max(-Boating.MaxReverse, Math.Min(Boating.MaxSpeed, speed));
            Boating.Seat(b, out float sx, out float sy, out float sz);
            if (Players.TryGet(playerId, out var player))
                Players.Place(playerId, new Pose(sx, sy, sz, player.Pose.Yaw), player.LastMoveTime);
            BoatsChanged = true;
            return true;
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
