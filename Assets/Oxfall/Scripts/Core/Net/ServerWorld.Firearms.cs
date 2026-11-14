using System;
using System.Collections.Generic;
using Oxfall.Core.Items;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;

namespace Oxfall.Core.Net
{
    /// <summary>
    /// Guns on the server: a shot is judged and its round spent when it is fired; what its pellets hit
    /// is the shooter's phone's to say (they fly among the real colliders there), checked against the
    /// shot they belong to: recent, from close enough, and aimed near enough the target.
    /// </summary>
    public sealed partial class ServerWorld
    {
        private sealed class Shot
        {
            public GunSpec Gun;
            public float X, Y, Z, Yaw, Pitch, Time;
            public int PelletsLeft;
        }

        private readonly Dictionary<(ushort, ToolKind), GunState> _guns = new Dictionary<(ushort, ToolKind), GunState>();
        private readonly Dictionary<ushort, Shot> _lastShot = new Dictionary<ushort, Shot>();

        /// <summary>The state of a player's gun of this kind (made on first use).</summary>
        public GunState GunOf(ushort playerId, ToolKind tool)
        {
            if (!_guns.TryGetValue((playerId, tool), out var state)) _guns[(playerId, tool)] = state = new GunState();
            return state;
        }

        /// <summary>
        /// A player fires a gun of this kind from (x, y, z) along yaw/pitch: alive, carrying that gun and a
        /// round for it, loaded and not faster than it fires, and from about where their eyes are. The round
        /// is spent. False if refused.
        /// </summary>
        public bool Fire(ushort playerId, ToolKind tool, float x, float y, float z, float yaw, float pitch, float now)
        {
            if (!Firearms.TryGet(tool, out var gun)) return false;
            if (!Players.TryGet(playerId, out var player) || !player.Placed) return false;
            if (!_online.TryGetValue(playerId, out var record) || record.Survivor.Vitals.IsDead) return false;
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z) || !IsFinite(yaw) || !IsFinite(pitch)) return false;
            var s = record.Survivor;
            if (!s.HasTool(tool) || s.Inventory.Count(gun.Ammo) < 1) return false;
            var state = GunOf(playerId, tool);
            if (!state.CanFire(gun, now, 0.75f)) return false;
            var p = player.Pose;
            float dx = x - p.X, dz = z - p.Z;
            if (dx * dx + dz * dz > 2.5f * 2.5f || Math.Abs(y - (p.Y + 1.6f)) > 2.5f) return false;

            s.Inventory.Remove(gun.Ammo, 1);
            state.Fire(gun, now);
            _lastShot[playerId] = new Shot { Gun = gun, X = x, Y = y, Z = z, Yaw = yaw, Pitch = pitch, Time = now, PelletsLeft = gun.Pellets };
            return true;
        }

        /// <summary>A player starts reloading their gun of this kind early. False if it is full or already reloading.</summary>
        public bool ReloadGun(ushort playerId, ToolKind tool, float now) =>
            Firearms.TryGet(tool, out var gun) && _online.ContainsKey(playerId) && GunOf(playerId, tool).Reload(gun, now);

        /// <summary>
        /// Some of a player's last shot's pellets hit a target: a player, a sleeper or a living animal, in
        /// range and in the line of the shot. Hurts it (<paramref name="damage"/>, <paramref name="killed"/>;
        /// for an animal also <paramref name="blow"/>). False when it hurt nobody.
        /// </summary>
        public bool GunHit(ushort playerId, ArrowTarget kind, int targetId, int pellets, float now,
            out float damage, out bool killed, out AnimalBlow blow)
        {
            damage = 0f;
            killed = false;
            blow = AnimalBlow.None;
            if (pellets < 1 || !_lastShot.TryGetValue(playerId, out var shot)) return false;
            if (now - shot.Time > Firearms.HitWindowSeconds || shot.PelletsLeft <= 0) return false;
            pellets = Math.Min(pellets, shot.PelletsLeft);
            var gun = shot.Gun;

            if (kind == ArrowTarget.Player)
            {
                if (targetId <= 0 || targetId > ushort.MaxValue || targetId == playerId) return false;
                var key = (ushort)targetId;
                if (!Players.TryGet(key, out var target) || !target.Placed) return false;
                if (!_online.TryGetValue(key, out var t) || t.Survivor.Vitals.IsDead) return false;
                var tp = target.Pose;
                if (!Firearms.CouldHit(gun, shot.X, shot.Y, shot.Z, shot.Yaw, shot.Pitch, tp.X, tp.Y, tp.Z, 1.8f)) return false;
                shot.PelletsLeft -= pellets;
                damage = t.Survivor.TakeHit(Firearms.Damage(gun, pellets, Distance3(shot, tp.X, tp.Y + 1f, tp.Z)));
                killed = t.Survivor.Vitals.IsDead;
                return true;
            }
            if (kind == ArrowTarget.Sleeper)
            {
                if (!TryGetSleeper(targetId, out var sleeper)) return false;
                var sp = sleeper.Pose;
                if (!Firearms.CouldHit(gun, shot.X, shot.Y, shot.Z, shot.Yaw, shot.Pitch, sp.X, sp.Y, sp.Z, 0.5f)
                    && Distance(shot.X, shot.Z, sp.X, sp.Z) > 2.5f) return false;
                shot.PelletsLeft -= pellets;
                string by = _online.TryGetValue(playerId, out var shooter) ? shooter.Name : null;
                return HurtSleeper(sleeper, Firearms.Damage(gun, pellets, Distance3(shot, sp.X, sp.Y + 0.3f, sp.Z)), by, out damage, out killed);
            }
            if (kind == ArrowTarget.Animal)
            {
                if (!Fauna.TryGet(targetId, out var animal) || animal.IsDead) return false;
                float ay = Ground.Height(animal.X, animal.Z);
                if (!Firearms.CouldHit(gun, shot.X, shot.Y, shot.Z, shot.Yaw, shot.Pitch, animal.X, ay, animal.Z, 1.3f)) return false;
                shot.PelletsLeft -= pellets;
                float amount = Firearms.Damage(gun, pellets, Distance3(shot, animal.X, ay + 0.7f, animal.Z));
                damage = Math.Min(amount, animal.Health);
                killed = Fauna.Hit(targetId, amount, playerId, shot.X, shot.Z);
                blow = killed ? AnimalBlow.Killed : AnimalBlow.Hurt;
                return true;
            }
            return false;
        }

        private static float Distance3(Shot s, float x, float y, float z)
        {
            float dx = s.X - x, dy = s.Y - y, dz = s.Z - z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
