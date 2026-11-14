using System;
using System.Collections.Generic;
using Oxfall.Core.Gathering;
using Oxfall.Core.Items;
using Oxfall.Core.World;

namespace Oxfall.Core.Net
{
    /// <summary>
    /// The shared island on the server: the players (<see cref="ServerState"/>) and every tree, rock
    /// and plant, laid out from the seed exactly as the game lays them out. The server decides what
    /// a hit gives and when a node grows back; the game only shows it.
    /// </summary>
    public sealed class ServerWorld
    {
        /// <summary>How far a player can reach a node (the game's reach, plus room for lag).</summary>
        public const float HitReach = 6f;

        /// <summary>The quickest swing the game allows is 0.7 s; a little less is let through for jitter.</summary>
        public const float MinHitInterval = 0.5f;

        private sealed class Node
        {
            public ResourceNode State;
            public float X, Y, Z;
        }

        private readonly Dictionary<int, Node> _nodes = new Dictionary<int, Node>();
        private readonly List<(int, int)> _changed = new List<(int, int)>();
        private readonly List<(string, int)> _yields = new List<(string, int)>();

        public ServerWorld(int seed)
        {
            Players = new ServerState(seed);
            Ground = new IslandGround(seed);
            foreach (var spawn in ResourceLayout.Generate(Ground))
                _nodes[spawn.Id] = new Node { State = new ResourceNode(spawn.Id, spawn.Type), X = spawn.X, Y = spawn.Y, Z = spawn.Z };
        }

        public ServerState Players { get; }
        public IslandGround Ground { get; }
        public int Seed => Players.Seed;
        public int NodeCount => _nodes.Count;

        public bool TryGetNode(int id, out ResourceNode node, out float x, out float z)
        {
            if (_nodes.TryGetValue(id, out var n))
            {
                node = n.State;
                x = n.X;
                z = n.Z;
                return true;
            }
            node = null;
            x = z = 0f;
            return false;
        }

        /// <summary>
        /// A player's swing at a node. Returns what it gave (empty when it gave nothing or was not
        /// allowed: too far, too fast, no such node, or a tool that does nothing there).
        /// </summary>
        public IReadOnlyList<(string ItemId, int Amount)> Hit(ushort playerId, int nodeId, ToolKind tool, float now)
        {
            _yields.Clear();
            if (!Players.TryGet(playerId, out var player) || !player.Placed) return _yields;
            if (!_nodes.TryGetValue(nodeId, out var node)) return _yields;
            if (now - player.LastHitTime < MinHitInterval) return _yields;
            float dx = player.Pose.X - node.X, dz = player.Pose.Z - node.Z;
            if (dx * dx + dz * dz > HitReach * HitReach || Math.Abs(player.Pose.Y - node.Y) > HitReach) return _yields;
            if (!Enum.IsDefined(typeof(ToolKind), tool)) return _yields;

            player.LastHitTime = now;
            foreach (var y in node.State.Hit(tool)) _yields.Add((y.ItemId, y.Amount));
            return _yields;
        }

        /// <summary>Grows depleted nodes back; returns the ones that came back this tick (id, amount).</summary>
        public IReadOnlyList<(int Id, int Remaining)> TickRespawn(float deltaSeconds)
        {
            _changed.Clear();
            foreach (var n in _nodes.Values)
                if (n.State.TickRespawn(deltaSeconds)) _changed.Add((n.State.Id, n.State.Remaining));
            return _changed;
        }

        /// <summary>Every node that is not full, for a player who has just joined.</summary>
        public List<(int Id, int Remaining)> NotFull()
        {
            var list = new List<(int, int)>();
            foreach (var n in _nodes.Values)
                if (n.State.Remaining < n.State.Capacity) list.Add((n.State.Id, n.State.Remaining));
            return list;
        }
    }
}
