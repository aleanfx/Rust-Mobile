using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
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

        /// <summary>Seconds a campfire takes to roast one piece of raw meat.</summary>
        public const float CookSeconds = 5f;

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
        private readonly Dictionary<int, float> _rotting = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _cooking = new Dictionary<int, float>();
        private readonly Dictionary<PieceRef, int> _owners = new Dictionary<PieceRef, int>();
        private readonly Dictionary<LockTarget, CodeLockState> _locks = new Dictionary<LockTarget, CodeLockState>();
        private readonly List<int> _scratch = new List<int>();
        private int _nextId = 1;
        private readonly Dictionary<PieceRef, float> _hurtAt = new Dictionary<PieceRef, float>();
        private readonly Dictionary<Edge, float> _doorHurtAt = new Dictionary<Edge, float>();
        private readonly Dictionary<int, float> _deployableHealth = new Dictionary<int, float>();
        private readonly List<Deployable> _falling = new List<Deployable>();
        private bool _clearing;
        private Recycling _recycling;

        public Settlement(IslandGround ground, ItemCatalog catalog)
        {
            Ground = ground ?? throw new ArgumentNullException(nameof(ground));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Grid = new BuildingGrid();
            // A piece that falls takes its owner record with it.
            Grid.PieceRemoved += (piece, tier) => _owners.Remove(piece);
            // A lock goes with the door it is on.
            Grid.DoorRemoved += edge => RemoveLock(LockTarget.Door(edge));
            // When each piece and door was last hurt (it cannot be mended for a while after).
            Grid.PieceHealthChanged += (piece, health) =>
            {
                if (health < Grid.MaxHealth(piece)) _hurtAt[piece] = Clock;
                else _hurtAt.Remove(piece);
            };
            Grid.DoorHealthChanged += (edge, health) =>
            {
                if (health < DoorKinds.MaxHealth(Grid.DoorKindOf(edge))) _doorHurtAt[edge] = Clock;
                else _doorHurtAt.Remove(edge);
            };
            Grid.PieceRemoved += (piece, tier) => _hurtAt.Remove(piece);
            Grid.PieceUpgraded += (piece, tier) => _hurtAt.Remove(piece);
            Grid.DoorRemoved += edge => _doorHurtAt.Remove(edge);
            // What stood on a foundation that came down comes down with it (where this copy decides;
            // a player's copy online hears it from the server).
            Grid.FoundationRemoved += (cell, top) =>
            {
                if (_clearing || !DropsLoot) return;
                _falling.Clear();
                foreach (var d in _deployables.Values)
                    if (d.Kind != DeployableKind.Recycler && BuildingGrid.CellAt(d.X, d.Z).Equals(cell.Ground) && d.Y > top - OnFloorSlack) _falling.Add(d);
                foreach (var d in _falling)
                {
                    DeployableFell?.Invoke(d.Id);
                    Destroy(d.Id);
                }
            };
            PlaceRecyclers();
        }

        /// <summary>How far below a floor's top something can stand and still be on it (one put on the ground is well below).</summary>
        private const float OnFloorSlack = 0.12f;

        /// <summary>
        /// Whether this copy is the one that decides (the server's, or a game played alone): only then
        /// does what stood on a fallen floor come down with it, and what a destroyed box held spill
        /// into a bag. A player's copy online leaves that to the server, which tells it what went.
        /// </summary>
        public bool DropsLoot { get; set; }

        /// <summary>A bag of what a destroyed box, furnace or fire held, just dropped where it stood (only where <see cref="DropsLoot"/>).</summary>
        public event Action<Deployable> LootDropped;

        /// <summary>A deployable is about to be destroyed because the floor under it came down (only where <see cref="DropsLoot"/>).</summary>
        public event Action<int> DeployableFell;

        /// <summary>A deployable was hurt (by a blast) and still stands: the health it has now.</summary>
        public event Action<int, float> DeployableHealthChanged;

        /// <summary>How much health a deployable has left.</summary>
        public float DeployableHealth(int id) =>
            _deployableHealth.TryGetValue(id, out float h) ? h
            : _deployables.TryGetValue(id, out var d) ? Rules.Deployables.MaxHealth(d.Kind) : 0f;

        /// <summary>Deployables below full health, and how much each has.</summary>
        public IEnumerable<KeyValuePair<int, float>> DamagedDeployables => _deployableHealth;

        /// <summary>
        /// Sets how much health a deployable has; at 0 it is destroyed (see <see cref="Destroy"/>).
        /// False if there is no such deployable.
        /// </summary>
        public bool SetDeployableHealth(int id, float health)
        {
            if (!_deployables.TryGetValue(id, out var d)) return false;
            if (health <= 0f)
            {
                Destroy(id);
                return true;
            }
            float max = Rules.Deployables.MaxHealth(d.Kind);
            if (health >= max) _deployableHealth.Remove(id);
            else _deployableHealth[id] = health;
            DeployableHealthChanged?.Invoke(id, Math.Min(health, max));
            return true;
        }

        /// <summary>
        /// Destroys a deployable (blown up, or its floor gone): what it held spills into a bag on the
        /// floor or ground below, where this copy decides that (<see cref="DropsLoot"/>), and it goes.
        /// </summary>
        public bool Destroy(int id)
        {
            if (!_deployables.TryGetValue(id, out var d)) return false;
            var held = Container(id);
            if (!DropsLoot || held == null || held.IsEmpty) return Remove(id);
            float ground = Ground.Height(d.X, d.Z);
            float floor = Grid.TryGetFoundationTop(BuildingGrid.CellAt(d.X, d.Z), out float top) && Math.Abs(top - d.Y) < 1.5f ? top : ground;
            var stacks = new List<(int, ItemStack)>();
            for (int i = 0; i < held.SlotCount; i++)
                if (!held[i].IsEmpty) stacks.Add((stacks.Count, held[i]));
            Remove(id);
            // A dead player's bag stays theirs as it falls.
            var bag = Add(new Deployable(0, DeployableKind.Bag, d.X, floor, d.Z, d.Yaw, d.Kind == DeployableKind.Bag ? d.Owner : 0));
            Container(bag.Id).SetSlots(stacks);
            LootDropped?.Invoke(bag);
            return true;
        }

        /// <summary>
        /// A bag of things on the ground that nobody owns (what a fallen bandit had): anyone may take
        /// from it, and it rots like a dead player's.
        /// </summary>
        public Deployable DropBag(float x, float y, float z, float yaw, IEnumerable<Gathering.Yield> items)
        {
            var bag = Add(new Deployable(0, DeployableKind.Bag, x, y, z, yaw, 0));
            var into = Container(bag.Id);
            foreach (var item in items)
                if (item.Amount > 0) into.Add(item.ItemId, item.Amount);
            LootDropped?.Invoke(bag);
            return bag;
        }

        /// <summary>A supply crate came down here, full of <paramref name="items"/> (everyone sees it through LootDropped).</summary>
        public Deployable LandSupplyDrop(float x, float y, float z, float yaw, IEnumerable<Gathering.Yield> items)
        {
            var crate = Add(new Deployable(0, DeployableKind.SupplyDrop, x, y, z, yaw, 0));
            var into = Container(crate.Id);
            foreach (var item in items)
                if (item.Amount > 0) into.Add(item.ItemId, item.Amount);
            LootDropped?.Invoke(crate);
            return crate;
        }

        /// <summary>Seconds this settlement has been ticked (the clock for mending after a blast).</summary>
        public float Clock { get; private set; }

        /// <summary>Whether a blast hit this piece too recently for it to be mended.</summary>
        public bool RecentlyHurt(PieceRef piece) => _hurtAt.TryGetValue(piece, out float t) && Clock - t < Repairs.Delay;

        public bool RecentlyHurtDoor(Edge edge) => _doorHurtAt.TryGetValue(edge, out float t) && Clock - t < Repairs.Delay;

        public IslandGround Ground { get; }

        /// <summary>The island's trunks and boulders, which nothing is built over (none known: no check).</summary>
        public Obstacles Obstacles { get; set; }
        public ItemCatalog Catalog { get; }
        public BuildingGrid Grid { get; }

        /// <summary>Cupboards paying for the pieces round them, and the pieces nobody keeps up decaying.</summary>
        public Upkeep Upkeep { get; } = new Upkeep();

        public IEnumerable<Deployable> Deployables => _deployables.Values;

        /// <summary>How many things players have set down (the ruins' machines are not counted).</summary>
        public int DeployableCount
        {
            get
            {
                int n = 0;
                foreach (int id in _deployables.Keys)
                    if (id > 0) n++;
                return n;
            }
        }

        /// <summary>What recyclers make of things.</summary>
        public Recycling Recycling => _recycling ??= new Recycling(Catalog, RecipeBook.CreateDefault(Catalog));

        /// <summary>The id of the recycler in the island's <paramref name="monument"/>th ruin: below zero, the same on every copy.</summary>
        public static int RecyclerId(int monument) => -1 - monument;

        /// <summary>Sets a recycler in each of the island's ruins (again after <see cref="Clear"/>), where it always stands.</summary>
        private void PlaceRecyclers()
        {
            var monuments = Monuments.Find(Ground);
            for (int i = 0; i < monuments.Count; i++)
            {
                var (x, y, z, yaw) = Monuments.RecyclerSpot(monuments[i]);
                Add(new Deployable(RecyclerId(i), DeployableKind.Recycler, x, y, z, yaw));
            }
        }

        /// <summary>Whether a recycler has something in its input slots it would take now (and room for what comes out).</summary>
        public bool CanRecycle(int id) => _containers.TryGetValue(id, out var box) && box.OutputSlots > 0 && NextToRecycle(box) >= 0;

        private int NextToRecycle(Inventory box)
        {
            for (int slot = 0; slot < box.SlotCount - box.OutputSlots; slot++)
            {
                var stack = box[slot];
                if (stack.IsEmpty || !Recycling.Takes(stack.ItemId)) continue;
                bool fits = true;
                foreach (var y in Recycling.Yield(stack.ItemId))
                    if (box.SpaceForOutput(y.ItemId) < y.Amount) { fits = false; break; }
                if (fits) return slot;
            }
            return -1;
        }

        /// <summary>One thing from a recycler's input slots becomes what it was made of, in its output slots. False if nothing could be.</summary>
        private bool RecycleOne(Inventory box)
        {
            int slot = NextToRecycle(box);
            if (slot < 0) return false;
            string item = box[slot].ItemId;
            box.TakeFromSlot(slot, 1);
            foreach (var y in Recycling.Yield(item)) box.AddOutput(y.ItemId, y.Amount);
            return true;
        }

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
            int output = deployable.Kind == DeployableKind.Recycler ? Rules.Deployables.RecyclerInputSlots : 0;
            if (slots > 0) _containers.Add(deployable.Id, new Inventory(Catalog, slots, hasHotbar: false, outputSlots: output));
            if (deployable.Kind == DeployableKind.ToolCupboard) _authorized[deployable.Id] = new HashSet<int> { deployable.Owner };
            if (deployable.Kind == DeployableKind.Bag) _rotting[deployable.Id] = Rules.Deployables.DeathBagSeconds;
            if (deployable.Kind == DeployableKind.SupplyDrop) _rotting[deployable.Id] = Rules.Deployables.SupplyDropSeconds;
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
            _rotting.Remove(id);
            _cooking.Remove(id);
            _deployableHealth.Remove(id);
            RemoveLock(LockTarget.On(id));
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

        // ---- Code locks

        /// <summary>A lock was put on, taken off, locked or unlocked, or someone entered its code.</summary>
        public event Action<LockTarget> LockChanged;

        /// <summary>A lock was taken off (or went with its door or box), with whoever had put it on.</summary>
        public event Action<LockTarget, int> LockRemoved;

        public IEnumerable<KeyValuePair<LockTarget, CodeLockState>> Locks => _locks;

        public bool HasLock(LockTarget target) => _locks.ContainsKey(target);

        public bool TryGetLock(LockTarget target, out CodeLockState state) => _locks.TryGetValue(target, out state);

        /// <summary>Whether a lock can go on this: a door that hangs, or a box or tool cupboard.</summary>
        public bool IsLockable(LockTarget target) => target.IsDoor
            ? Grid.HasDoor(target.Edge)
            : _deployables.TryGetValue(target.Id, out var d) && CodeLocks.Lockable(d.Kind);

        /// <summary>
        /// Whether this player can get through: no lock, an open one, or one they know the code of.
        /// </summary>
        public bool CanPass(LockTarget target, int owner) =>
            !_locks.TryGetValue(target, out var l) || !l.Locked || l.Authorized.Contains(owner);

        /// <summary>Puts a lock on, open and with no code yet. False if there is one already or nothing to lock.</summary>
        public bool AddLock(LockTarget target, int owner)
        {
            if (_locks.ContainsKey(target) || !IsLockable(target)) return false;
            _locks.Add(target, new CodeLockState { Owner = owner });
            LockChanged?.Invoke(target);
            return true;
        }

        public bool RemoveLock(LockTarget target)
        {
            if (!_locks.TryGetValue(target, out var l)) return false;
            _locks.Remove(target);
            LockRemoved?.Invoke(target, l.Owner);
            LockChanged?.Invoke(target);
            return true;
        }

        /// <summary>
        /// A new code (or <see cref="CodeLocks.Hidden"/> on a copy): the lock locks, and only whoever
        /// set it knows it now, as in the PC game.
        /// </summary>
        public void SetCode(LockTarget target, int code, int setter)
        {
            if (!_locks.TryGetValue(target, out var l)) return;
            l.Code = code;
            l.Locked = true;
            l.Authorized.Clear();
            l.Authorized.Add(setter);
            LockChanged?.Invoke(target);
        }

        /// <summary>Whether this is the lock's code (never, on a copy that does not know it).</summary>
        public bool CodeMatches(LockTarget target, int code) =>
            _locks.TryGetValue(target, out var l) && CodeLocks.IsValidCode(l.Code) && l.Code == code;

        /// <summary>Someone entered the right code: they get through from now on.</summary>
        public void AuthorizeLock(LockTarget target, int owner)
        {
            if (_locks.TryGetValue(target, out var l) && l.Authorized.Add(owner)) LockChanged?.Invoke(target);
        }

        public void SetLocked(LockTarget target, bool locked)
        {
            if (!_locks.TryGetValue(target, out var l) || l.Locked == locked) return;
            l.Locked = locked;
            LockChanged?.Invoke(target);
        }

        /// <summary>Puts a saved code back (the settlement as described carries only hidden ones).</summary>
        public void RestoreCode(LockTarget target, int code)
        {
            if (_locks.TryGetValue(target, out var l) && CodeLocks.IsValidCode(code)) l.Code = code;
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
        /// <summary>Campfires always burn: raw meat in one roasts, a piece every <see cref="CookSeconds"/>.</summary>
        private void Cook(float dt)
        {
            foreach (var d in _deployables.Values)
            {
                if (d.Kind != DeployableKind.Campfire || !_containers.TryGetValue(d.Id, out var fire)) continue;
                if (!fire.Has(ItemIds.RawMeat, 1))
                {
                    _cooking.Remove(d.Id);
                    continue;
                }
                _cooking.TryGetValue(d.Id, out float t);
                t += dt;
                while (t >= CookSeconds && fire.Has(ItemIds.RawMeat, 1))
                {
                    t -= CookSeconds;
                    if (fire.SpaceFor(ItemIds.CookedMeat) < 1) break;
                    fire.Remove(ItemIds.RawMeat, 1);
                    fire.Add(ItemIds.CookedMeat, 1);
                }
                _cooking[d.Id] = Math.Min(t, CookSeconds);
            }
        }

        /// <summary>Seconds until a dead player's bag rots away (0 for anything else).</summary>
        public float RotsIn(int id) => _rotting.TryGetValue(id, out float t) ? t : 0f;

        /// <summary>
        /// Time passing: furnaces burn (those that run out of wood go out, listed in
        /// <paramref name="wentOut"/>), dead players' bags rot away (removed, and listed in
        /// <paramref name="rotted"/>), and cupboards keep their pieces up or the pieces decay (their
        /// new health listed in <paramref name="decayed"/>).
        /// </summary>
        public void Tick(float dt, List<int> wentOut = null, List<int> rotted = null, List<BuildOp> decayed = null)
        {
            if (dt <= 0f) return;
            Clock += dt;
            Upkeep.Tick(this, dt, decayed);
            if (_rotting.Count > 0)
            {
                _scratch.Clear();
                _scratch.AddRange(_rotting.Keys);
                foreach (int id in _scratch)
                {
                    float left = _rotting[id] - dt;
                    if (left > 0f)
                    {
                        _rotting[id] = left;
                        continue;
                    }
                    Remove(id);
                    rotted?.Add(id);
                }
            }
            Cook(dt);
            if (_lit.Count == 0) return;
            _scratch.Clear();
            _scratch.AddRange(_lit.Keys);
            foreach (int id in _scratch)
            {
                var furnace = _lit[id];
                var box = _containers[id];
                // A recycler needs no wood: it runs while it has something to take, then stops.
                if (_deployables[id].Kind == DeployableKind.Recycler)
                {
                    furnace.Smelt += dt;
                    bool going = true;
                    while (going && furnace.Smelt >= Recycling.Seconds)
                    {
                        furnace.Smelt -= Recycling.Seconds;
                        going = RecycleOne(box);
                    }
                    if (going && NextToRecycle(box) < 0) going = false;
                    if (!going)
                    {
                        SetLit(id, false);
                        wentOut?.Add(id);
                    }
                    continue;
                }
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
                    // Metal ore and sulfur ore side by side, one of each at a time.
                    bool metal = box.Has(ItemIds.MetalOre, 1) && box.SpaceFor(ItemIds.MetalFragments) >= 1;
                    bool sulfur = box.Has(ItemIds.SulfurOre, 1) && box.SpaceFor(ItemIds.Sulfur) >= 1;
                    if (!metal && !sulfur)
                    {
                        furnace.Smelt = 0f;
                        break;
                    }
                    if (metal)
                    {
                        box.Remove(ItemIds.MetalOre, 1);
                        box.Add(ItemIds.MetalFragments, 1);
                    }
                    if (sulfur)
                    {
                        box.Remove(ItemIds.SulfurOre, 1);
                        box.Add(ItemIds.Sulfur, 1);
                    }
                }
            }
        }

        /// <summary>Takes everything down (each removal is announced, so views follow).</summary>
        public void Clear()
        {
            _clearing = true;
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
            var locks = new List<LockTarget>(_locks.Keys);
            foreach (var target in locks) RemoveLock(target);
            _owners.Clear();
            _deployableHealth.Clear();
            Upkeep.Clear();
            _nextId = 1;
            _clearing = false;
            PlaceRecyclers();
        }
    }
}
