using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    /// <summary>
    /// Everything players have put on the island: the building grid, who built each piece, and the
    /// things set down (campfires, boxes, furnaces, sleeping bags, tool cupboards) with what they
    /// hold. The server keeps the real one; online, a phone keeps a copy that the server's
    /// <see cref="BuildOp"/>s update in the same order, so the copy always matches. (A box's
    /// contents are only sent to whoever has it open.)
    /// </summary>
    public sealed class Settlement
    {
        /// <summary>Seconds one piece of wood keeps a furnace burning (it leaves a piece of charcoal).</summary>
        public const float FuelSeconds = 2f;

        /// <summary>Seconds a burning furnace takes to turn one metal ore into one metal fragment.</summary>
        public const float SmeltSeconds = 2f;

        private sealed class Furnace
        {
            public float Fuel;
            public float Smelt;
        }

        private readonly Dictionary<int, Deployable> _deployables = new Dictionary<int, Deployable>();
        private readonly Dictionary<int, Inventory> _containers = new Dictionary<int, Inventory>();
        private readonly Dictionary<int, Furnace> _lit = new Dictionary<int, Furnace>();
        private readonly Dictionary<int, HashSet<int>> _authorized = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, float> _bagReady = new Dictionary<int, float>();
        private readonly Dictionary<PieceRef, int> _owners = new Dictionary<PieceRef, int>();
        private readonly List<int> _scratch = new List<int>();
        private int _nextId = 1;

        public Settlement(IslandGround ground, ItemCatalog catalog)
        {
            Ground = ground ?? throw new ArgumentNullException(nameof(ground));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Grid = new BuildingGrid();
            // A piece that falls takes its owner record with it.
            Grid.PieceRemoved += (piece, tier) => _owners.Remove(piece);
        }

        public IslandGround Ground { get; }
        public ItemCatalog Catalog { get; }
        public BuildingGrid Grid { get; }

        public IEnumerable<Deployable> Deployables => _deployables.Values;
        public int DeployableCount => _deployables.Count;

        public event Action<Deployable> DeployableAdded;
        public event Action<int> DeployableRemoved;

        /// <summary>A deployable lit up or went out, or a tool cupboard's list changed.</summary>
        public event Action<int> DeployableChanged;

        public bool TryGet(int id, out Deployable deployable) => _deployables.TryGetValue(id, out deployable);

        /// <summary>
        /// Sets a deployable down; an id of 0 gets the next free one. Returns it with its id. A tool
        /// cupboard starts with whoever set it down authorized on it.
        /// </summary>
        public Deployable Add(Deployable deployable)
        {
            if (deployable.Id == 0) deployable = deployable.WithId(_nextId);
            if (_deployables.TryGetValue(deployable.Id, out var existing)) return existing;
            _nextId = Math.Max(_nextId, deployable.Id + 1);
            _deployables.Add(deployable.Id, deployable);
            int slots = Rules.Deployables.Slots(deployable.Kind);
            if (slots > 0) _containers.Add(deployable.Id, new Inventory(Catalog, slots, hasHotbar: false));
            if (deployable.Kind == DeployableKind.ToolCupboard) _authorized[deployable.Id] = new HashSet<int> { deployable.Owner };
            DeployableAdded?.Invoke(deployable);
            return deployable;
        }

        public bool Remove(int id)
        {
            if (!_deployables.Remove(id)) return false;
            _containers.Remove(id);
            _lit.Remove(id);
            _authorized.Remove(id);
            _bagReady.Remove(id);
            DeployableRemoved?.Invoke(id);
            return true;
        }

        /// <summary>What a box or furnace holds; null for things that hold nothing.</summary>
        public Inventory Container(int id) => _containers.TryGetValue(id, out var c) ? c : null;

        public bool IsLit(int id) => _lit.ContainsKey(id);

        public void SetLit(int id, bool on)
        {
            if (!_deployables.ContainsKey(id) || on == IsLit(id)) return;
            if (on) _lit.Add(id, new Furnace());
            else _lit.Remove(id);
            DeployableChanged?.Invoke(id);
        }

        // ---- Tool cupboards

        /// <summary>Who is authorized on a tool cupboard (empty for anything else).</summary>
        public IReadOnlyCollection<int> AuthorizedOn(int cupboard) =>
            _authorized.TryGetValue(cupboard, out var list) ? list : (IReadOnlyCollection<int>)Array.Empty<int>();

        public bool IsAuthorized(int cupboard, int owner) => _authorized.TryGetValue(cupboard, out var list) && list.Contains(owner);

        public void Authorize(int cupboard, int owner)
        {
            if (_authorized.TryGetValue(cupboard, out var list) && list.Add(owner)) DeployableChanged?.Invoke(cupboard);
        }

        public void ClearAuthorized(int cupboard)
        {
            if (_authorized.TryGetValue(cupboard, out var list) && list.Count > 0)
            {
                list.Clear();
                DeployableChanged?.Invoke(cupboard);
            }
        }

        /// <summary>
        /// Whether this player may build at a spot: anywhere no tool cupboard reaches, and within
        /// reach of cupboards only if authorized on every one of them.
        /// </summary>
        public bool HasPrivilege(int owner, float x, float z)
        {
            foreach (var pair in _authorized)
            {
                var tc = _deployables[pair.Key];
                float dx = x - tc.X, dz = z - tc.Z;
                if (dx * dx + dz * dz > Rules.Deployables.PrivilegeRadius * Rules.Deployables.PrivilegeRadius) continue;
                if (!pair.Value.Contains(owner)) return false;
            }
            return true;
        }

        /// <summary>The nearest tool cupboard reaching a spot, if any.</summary>
        public bool TryGetCupboardAt(float x, float z, out Deployable cupboard)
        {
            cupboard = default;
            float best = Rules.Deployables.PrivilegeRadius * Rules.Deployables.PrivilegeRadius;
            bool found = false;
            foreach (int id in _authorized.Keys)
            {
                var tc = _deployables[id];
                float dx = x - tc.X, dz = z - tc.Z, d = dx * dx + dz * dz;
                if (d > best) continue;
                best = d;
                cupboard = tc;
                found = true;
            }
            return found;
        }

        // ---- Sleeping bags

        /// <summary>When a sleeping bag can be woken up in again (in the caller's clock's seconds).</summary>
        public float BagReadyAt(int bag) => _bagReady.TryGetValue(bag, out float t) ? t : float.NegativeInfinity;

        public void UseBag(int bag, float now) => _bagReady[bag] = now + Rules.Deployables.BagCooldown;

        /// <summary>This player's sleeping bags.</summary>
        public IEnumerable<Deployable> BagsOf(int owner)
        {
            foreach (var d in _deployables.Values)
                if (d.Kind == DeployableKind.SleepingBag && d.Owner == owner) yield return d;
        }

        // ---- Building pieces

        /// <summary>Every piece someone in particular built, with who (for saving).</summary>
        public IEnumerable<KeyValuePair<PieceRef, int>> Owners => _owners;

        /// <summary>Who built a piece (0: nobody in particular, as in a game played alone).</summary>
        public int OwnerOf(PieceRef piece) => _owners.TryGetValue(piece, out int owner) ? owner : 0;

        public void SetOwner(PieceRef piece, int owner)
        {
            if (owner == 0) _owners.Remove(piece);
            else _owners[piece] = owner;
        }

        // ---- Time

        /// <summary>
        /// Furnaces burn: each piece of wood keeps one going <see cref="FuelSeconds"/> and leaves
        /// charcoal, and meanwhile metal ore turns into fragments. One with no wood left goes out;
        /// its id goes in <paramref name="wentOut"/> (when given) so the server can tell everyone.
        /// </summary>
        public void Tick(float dt, List<int> wentOut = null)
        {
            if (_lit.Count == 0 || dt <= 0f) return;
            _scratch.Clear();
            _scratch.AddRange(_lit.Keys);
            foreach (int id in _scratch)
            {
                var furnace = _lit[id];
                var box = _containers[id];
                furnace.Fuel -= dt;
                bool burning = true;
                while (furnace.Fuel < 0f)
                {
                    if (!box.Remove(ItemIds.Wood, 1))
                    {
                        burning = false;
                        break;
                    }
                    furnace.Fuel += FuelSeconds;
                    box.Add(ItemIds.Charcoal, 1);
                }
                if (!burning)
                {
                    SetLit(id, false);
                    wentOut?.Add(id);
                    continue;
                }
                furnace.Smelt += dt;
                while (furnace.Smelt >= SmeltSeconds)
                {
                    furnace.Smelt -= SmeltSeconds;
                    if (!box.Has(ItemIds.MetalOre, 1) || box.SpaceFor(ItemIds.MetalFragments) < 1)
                    {
                        furnace.Smelt = 0f;
                        break;
                    }
                    box.Remove(ItemIds.MetalOre, 1);
                    box.Add(ItemIds.MetalFragments, 1);
                }
            }
        }

        /// <summary>Takes everything down (each removal is announced, so views follow).</summary>
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
            var all = new List<int>(_deployables.Keys);
            foreach (int id in all) Remove(id);
            _owners.Clear();
            _nextId = 1;
        }
    }
}
