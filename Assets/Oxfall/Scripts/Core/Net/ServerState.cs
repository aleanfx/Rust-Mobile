using System;
using System.Collections.Generic;

namespace Oxfall.Core.Net
{
    /// <summary>
    /// What the server knows about the players, with no sockets in it so it can be tested on its
    /// own: who is in, where each one is, and whether a move is believable. The server program feeds
    /// it messages and sends out what it answers.
    /// </summary>
    public sealed class ServerState
    {
        /// <summary>Faster than a sprinting player can go on foot, with room for a laggy burst.</summary>
        public const float MaxHorizontalSpeed = 11f;

        /// <summary>Falling from a cliff is fast; anything beyond this is not falling.</summary>
        public const float MaxVerticalSpeed = 40f;

        /// <summary>Moves arrive in bursts on a bad connection: some distance is always allowed.</summary>
        public const float Slack = 2f;

        public sealed class Player
        {
            public ushort Id { get; internal set; }
            public string Name { get; internal set; }
            public Pose Pose { get; internal set; }
            public float LastMoveTime { get; internal set; }
            /// <summary>False until the player has a position: the server gives one when they join.</summary>
            public bool Placed { get; internal set; }
            public int RejectedMoves { get; internal set; }
            public float LastHitTime { get; internal set; } = -100f;
            /// <summary>Ground speed lately (metres a second), to tell running from walking.</summary>
            public float Speed { get; internal set; }
        }

        private readonly Dictionary<ushort, Player> _players = new Dictionary<ushort, Player>();
        private readonly List<(ushort, Pose)> _snapshot = new List<(ushort, Pose)>();
        private ushort _nextId = 1;

        public ServerState(int seed) => Seed = seed;

        /// <summary>The island everyone shares this wipe.</summary>
        public int Seed { get; }

        /// <summary>
        /// For tests only: a client may move its player anywhere by saying it teleported. A real
        /// server never believes that; it moves players itself (waking up) and tells them.
        /// </summary>
        public bool AllowTeleports { get; set; }

        public int Count => _players.Count;
        public IEnumerable<Player> Players => _players.Values;

        public bool TryGet(ushort id, out Player player) => _players.TryGetValue(id, out player);

        /// <summary>A new player in at <paramref name="pose"/>, or null with a reason when they cannot come in.</summary>
        public Player Join(ushort version, string name, Pose pose, float now, out string reason)
        {
            reason = null;
            if (version != Protocol.Version)
            {
                reason = version < Protocol.Version
                    ? "Tu versión del juego es antigua: actualízala para jugar."
                    : "El servidor aún no tiene esta versión del juego.";
                return null;
            }
            if (_players.Count >= Protocol.MaxPlayers)
            {
                reason = "El servidor está lleno.";
                return null;
            }
            var player = new Player { Id = NextId(), Name = Protocol.CleanName(name), LastMoveTime = now, Pose = pose, Placed = true };
            _players.Add(player.Id, player);
            return player;
        }

        public bool Leave(ushort id) => _players.Remove(id);

        /// <summary>
        /// Takes a player's reported position if it could have been reached since the last one;
        /// otherwise keeps the old one (the client will be pulled back). Returns whether it was taken.
        /// </summary>
        public bool ApplyMove(ushort id, Pose pose, bool teleport, float now)
        {
            if (!_players.TryGetValue(id, out var player) || !pose.IsFinite) return false;
            // Capped: standing still a long time does not earn a long jump.
            float dt = Math.Min(1f, Math.Max(0f, now - player.LastMoveTime));
            bool believable = (teleport && AllowTeleports) || !player.Placed
                || (player.Pose.HorizontalDistance(pose) <= MaxHorizontalSpeed * dt + Slack
                    && Math.Abs(pose.Y - player.Pose.Y) <= MaxVerticalSpeed * dt + Slack);
            if (!believable)
            {
                player.RejectedMoves++;
                // The client is told where it really is, and moves count from there again.
                player.LastMoveTime = now;
                return false;
            }
            if (dt > 0f && player.Placed)
            {
                float speed = Math.Min(MaxHorizontalSpeed, player.Pose.HorizontalDistance(pose) / dt);
                player.Speed += (speed - player.Speed) * Math.Min(1f, dt * 4f);
            }
            player.Pose = pose;
            player.LastMoveTime = now;
            player.Placed = true;
            return true;
        }

        /// <summary>Moves a player on the server's say (waking up on the beach); the client must be told.</summary>
        public void Place(ushort id, Pose pose, float now)
        {
            if (!_players.TryGetValue(id, out var player)) return;
            player.Pose = pose;
            player.LastMoveTime = now;
            player.Placed = true;
            player.Speed = 0f;
        }

        /// <summary>Everyone's position (once known), for the snapshot sent to all.</summary>
        public IReadOnlyList<(ushort Id, Pose Pose)> Snapshot()
        {
            _snapshot.Clear();
            foreach (var p in _players.Values)
                if (p.Placed) _snapshot.Add((p.Id, p.Pose));
            return _snapshot;
        }

        private ushort NextId()
        {
            // Ids are reused only after wrapping, and never 0 (0 means "nobody").
            for (int i = 0; i < ushort.MaxValue; i++)
            {
                ushort id = _nextId;
                _nextId = (ushort)(_nextId == ushort.MaxValue ? 1 : _nextId + 1);
                if (!_players.ContainsKey(id)) return id;
            }
            throw new InvalidOperationException("No free player id.");
        }
    }

    /// <summary>
    /// A remote player's recent positions with the server time of each, so the client can draw them
    /// a little in the past, smoothly between two known positions, instead of jumping at every snapshot.
    /// </summary>
    public sealed class PoseBuffer
    {
        /// <summary>How far in the past other players are drawn: two snapshots at 20 per second, and a spare.</summary>
        public const float Delay = 0.15f;

        private const int Capacity = 32;
        private readonly float[] _times = new float[Capacity];
        private readonly Pose[] _poses = new Pose[Capacity];
        private int _start, _count;

        public int Count => _count;

        public void Add(float time, Pose pose)
        {
            // Out of order or repeated: a newer one is already in.
            if (_count > 0 && time <= _times[Index(_count - 1)]) return;
            if (_count == Capacity)
            {
                _start = (_start + 1) % Capacity;
                _count--;
            }
            _times[Index(_count)] = time;
            _poses[Index(_count)] = pose;
            _count++;
        }

        /// <summary>The position at a time: between the two around it, or the nearest end.</summary>
        public Pose Sample(float time)
        {
            if (_count == 0) return default;
            if (time <= _times[Index(0)]) return _poses[Index(0)];
            for (int i = 1; i < _count; i++)
            {
                float t1 = _times[Index(i)];
                if (time > t1) continue;
                float t0 = _times[Index(i - 1)];
                // Older entries are no longer needed once both ends are past them.
                Drop(i - 1);
                return Pose.Lerp(_poses[Index(0)], _poses[Index(1)], (time - t0) / (t1 - t0));
            }
            return _poses[Index(_count - 1)];
        }

        private void Drop(int n)
        {
            _start = (_start + n) % Capacity;
            _count -= n;
        }

        private int Index(int i) => (_start + i) % Capacity;
    }

    /// <summary>
    /// The client's guess of the server clock: the server time of each snapshot minus when it
    /// arrived, keeping the smallest (the snapshot that was least delayed), eased so it never jumps.
    /// </summary>
    public sealed class ServerClock
    {
        private float _offset;
        private bool _known;

        public bool Known => _known;

        public void Observe(float serverTime, float localTime)
        {
            float offset = serverTime - localTime;
            if (!_known)
            {
                _offset = offset;
                _known = true;
                return;
            }
            // Earlier arrivals win at once; later ones drift the estimate slowly (the clocks can creep).
            _offset = offset > _offset ? offset : _offset + (offset - _offset) * 0.02f;
        }

        public float Now(float localTime) => localTime + _offset;
    }
}
