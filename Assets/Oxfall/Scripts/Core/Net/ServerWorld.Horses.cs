using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;

namespace Oxfall.Core.Net
{
    /// <summary>The horses as the server keeps them: who rides which, and the rest grazing.</summary>
    public sealed partial class ServerWorld
    {
        public List<Horse> Horses { get; private set; }

        /// <summary>A horse moved, or someone got on or off, since they were last sent.</summary>
        public bool HorsesChanged { get; set; } = true;

        private readonly Dictionary<int, float> _lastGallop = new Dictionary<int, float>();

        private void CreateHorses(World.Obstacles obstacles) =>
            Horses = Riding.Spawn(Ground, Seed, (x, z) => obstacles.Blocks(x, z, 3f));

        /// <summary>The horse this player rides, or null.</summary>
        public Horse RiddenBy(ushort playerId)
        {
            if (playerId == 0) return null;
            foreach (var h in Horses)
                if (h.Driver == playerId) return h;
            return null;
        }

        /// <summary>Whether this player is on any vehicle (boat, copter or horse).</summary>
        public bool Aboard(ushort playerId) => RowedBy(playerId) != null || FlownBy(playerId) != null || RiddenBy(playerId) != null;

        /// <summary>Gets on a horse; a hungry one eats the berries the rider carries first.</summary>
        public ActionResult Ride(ushort playerId, int id)
        {
            if (!_online.TryGetValue(playerId, out var record) || !Players.TryGet(playerId, out var player)) return ActionResult.Invalid;
            if (record.Survivor.Vitals.IsDead) return ActionResult.Dead;
            if (Aboard(playerId)) return ActionResult.Invalid;
            foreach (var h in Horses)
            {
                if (h.Id != id) continue;
                if (!Riding.CanMount(h, player.Pose.X, player.Pose.Y, player.Pose.Z)) return ActionResult.TooFar;
                h.Driver = playerId;
                h.Speed = 0f;
                _lastGallop[h.Id] = Time;
                var inv = record.Survivor.Inventory;
                int ate = Riding.Feed(h, inv.Count(ItemIds.Berries));
                if (ate > 0) inv.Remove(ItemIds.Berries, ate);
                Riding.Seat(h, out float x, out float y, out float z);
                Players.Place(playerId, new Pose(x, y, z, player.Pose.Yaw), player.LastMoveTime);
                HorsesChanged = true;
                return ActionResult.Ok;
            }
            return ActionResult.Invalid;
        }

        /// <summary>Gets the rider off beside it; the client is told where.</summary>
        public ActionResult GetOff(ushort playerId)
        {
            var h = RiddenBy(playerId);
            if (h == null) return ActionResult.Invalid;
            h.Driver = 0;
            h.Speed = 0f;
            HorsesChanged = true;
            if (Players.TryGet(playerId, out var player))
            {
                Riding.Landing(h, Ground, out float x, out float y, out float z);
                Players.Place(playerId, new Pose(x, y, z, player.Pose.Yaw), player.LastMoveTime);
            }
            return ActionResult.Ok;
        }

        /// <summary>
        /// The rider's phone moved their horse: believed at a gallop's pace over walkable ground,
        /// else it stays and false says the rider must be told where it is. Its wind and food are kept here.
        /// </summary>
        public bool Gallop(ushort playerId, float x, float z, float yaw, float speed)
        {
            var h = RiddenBy(playerId);
            if (h == null) return true;
            if (!_online.TryGetValue(playerId, out var record) || record.Survivor.Vitals.IsDead)
            {
                h.Driver = 0;
                HorsesChanged = true;
                return true;
            }
            float dt = Time - (_lastGallop.TryGetValue(h.Id, out float last) ? last : Time);
            if (float.IsNaN(yaw) || float.IsInfinity(yaw) || float.IsNaN(speed) || !Riding.Believable(h, x, z, Math.Max(dt, 0.1f), Ground))
                return false;
            // Faster than a trot is only believed while it has wind to gallop.
            if (speed > Riding.TrotSpeed + 0.5f && h.Stamina <= 0f) return false;
            _lastGallop[h.Id] = Time;
            h.Speed = Math.Max(-Riding.BackSpeed, Math.Min(Riding.GallopSpeed, speed));
            if (dt > 0f)
            {
                float step = Math.Min(dt, 1f);
                if (h.Galloping) h.Stamina = Math.Max(0f, h.Stamina - Riding.StaminaDrain * (h.Food < Riding.Hungry ? 2f : 1f) * step);
                else h.Stamina = Math.Min(Riding.MaxStamina, h.Stamina + Riding.StaminaRegen * step);
                h.Food = Math.Max(0f, h.Food - Riding.FoodDrain * step);
            }
            h.X = x;
            h.Z = z;
            h.Y = Riding.Floor(Ground, x, z);
            h.Yaw = ((yaw % 360f) + 360f) % 360f;
            if (Players.TryGet(playerId, out var player))
            {
                Riding.Seat(h, out float sx, out float sy, out float sz);
                Players.Place(playerId, new Pose(sx, sy, sz, player.Pose.Yaw), player.LastMoveTime);
            }
            HorsesChanged = true;
            return true;
        }

        /// <summary>Riders gone or dead let go; horses nobody rides graze.</summary>
        private void TickHorses(float dt)
        {
            foreach (var h in Horses)
            {
                if (h.Driver != 0 && (!_online.TryGetValue(h.Driver, out var record) || record.Survivor.Vitals.IsDead))
                {
                    h.Driver = 0;
                    h.Speed = 0f;
                    HorsesChanged = true;
                }
                if (h.Driver != 0) continue;
                float x = h.X, z = h.Z, speed = h.Speed;
                Riding.Graze(h, dt, (float)_random.NextDouble(), Ground);
                if (h.X != x || h.Z != z || h.Speed != speed) HorsesChanged = true;
            }
        }
    }
}
