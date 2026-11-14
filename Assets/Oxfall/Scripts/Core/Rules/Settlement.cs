using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    /// <summary>A campfire someone lit: where it stands and which way it faces.</summary>
    public readonly struct Campfire
    {
        public readonly int Id;
        public readonly float X, Y, Z, Yaw;

        public Campfire(int id, float x, float y, float z, float yaw)
        {
            Id = id;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }

        public Campfire WithId(int id) => new Campfire(id, X, Y, Z, Yaw);
    }

    /// <summary>
    /// Everything players have put on the island: the building grid, the campfires, and who built
    /// each piece. The server keeps the real one; online, a phone keeps a copy that the server's
    /// <see cref="BuildOp"/>s update in the same order, so the copy always matches.
    /// </summary>
    public sealed class Settlement
    {
        private readonly Dictionary<int, Campfire> _campfires = new Dictionary<int, Campfire>();
        private readonly Dictionary<PieceRef, int> _owners = new Dictionary<PieceRef, int>();
        private int _nextCampfire = 1;

        public Settlement(IslandGround ground)
        {
            Ground = ground ?? throw new ArgumentNullException(nameof(ground));
            Grid = new BuildingGrid();
            // A piece that falls takes its owner record with it.
            Grid.PieceRemoved += (piece, tier) => _owners.Remove(piece);
        }

        public IslandGround Ground { get; }
        public BuildingGrid Grid { get; }

        public IEnumerable<Campfire> Campfires => _campfires.Values;
        public int CampfireCount => _campfires.Count;

        public event Action<Campfire> CampfireAdded;
        public event Action<int> CampfireRemoved;

        /// <summary>Adds a campfire; an id of 0 gets the next free one. Returns it with its id.</summary>
        public Campfire AddCampfire(Campfire fire)
        {
            if (fire.Id == 0) fire = fire.WithId(_nextCampfire);
            if (_campfires.ContainsKey(fire.Id)) return _campfires[fire.Id];
            _nextCampfire = Math.Max(_nextCampfire, fire.Id + 1);
            _campfires.Add(fire.Id, fire);
            CampfireAdded?.Invoke(fire);
            return fire;
        }

        public bool RemoveCampfire(int id)
        {
            if (!_campfires.Remove(id)) return false;
            CampfireRemoved?.Invoke(id);
            return true;
        }

        /// <summary>Who built a piece (0: nobody in particular, as in a game played alone).</summary>
        public int OwnerOf(PieceRef piece) => _owners.TryGetValue(piece, out int owner) ? owner : 0;

        public void SetOwner(PieceRef piece, int owner)
        {
            if (owner == 0) _owners.Remove(piece);
            else _owners[piece] = owner;
        }

        /// <summary>Takes everything down (each piece's removal is announced, so views follow).</summary>
        public void Clear()
        {
            var foundations = new List<Cell>();
            foreach (var f in Grid.Foundations) foundations.Add(f.Key);
            foreach (var cell in foundations) Grid.RemoveFoundation(cell);
            // Anything left standing without a foundation under it.
            var rest = new List<PieceRef>();
            foreach (var e in Grid.EdgePieces) rest.Add(PieceRef.OnEdge(e.Key, e.Value));
            foreach (var r in Grid.Roofs) rest.Add(PieceRef.Roof(r));
            foreach (var piece in rest) Grid.Remove(piece);
            var fires = new List<int>(_campfires.Keys);
            foreach (int id in fires) RemoveCampfire(id);
            _owners.Clear();
            _nextCampfire = 1;
        }
    }
}
