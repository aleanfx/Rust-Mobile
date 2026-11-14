using System;
using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;
using Oxfall.Core.Survival;
using Oxfall.Core.World;

namespace Oxfall.Core.Rules
{
    /// <summary>Why an action did or did not happen. Sent as numbers: only ever add at the end.</summary>
    public enum ActionResult : byte
    {
        Ok,
        Invalid,
        Dead,
        NotEnoughResources,
        InventoryFull,
        QueueFull,
        TooFar,
        Occupied,
        NeedsFoundation,
        NeedsWall,
        NeedsDoorway,
        NoPiece,
        MaxTier,
        InWater,
        TooSteep,
        GroundInTheWay,
        NotFood,
        NoWater,
        NoDoor,
        NotYours,
        NoSpace,
        BuildingBlocked,
        NotEmpty,
        NoFuel,
        NotReady,
        TooClose,
        NotOpen,
    }

    /// <summary>
    /// Everything a player can do to their belongings and to the island, with its checks and costs.
    /// The game alone runs these on its own copy; online, the server runs the very same code and the
    /// phone only shows the result, so a modified phone cannot build for free or carry more.
    /// Each one changes everything or nothing.
    /// </summary>
    public static class PlayerActions
    {
        /// <summary>How far from a piece's middle a player may build or hammer (the game's reach, plus a cell and lag).</summary>
        public const float BuildReach = 10f;

        /// <summary>How far a player may open a door or set a campfire down from.</summary>
        public const float UseReach = 7f;

        /// <summary>How far from a pond's edge a player can drink.</summary>
        public const float DrinkReach = 5f;

        /// <summary>Water a mouthful from a pond gives.</summary>
        public const float DrinkWater = 25f;

        private const float S = BuildingGrid.CellSize;

        // ---- Belongings

        public static ActionResult MoveSlot(Survivor s, int from, int to)
        {
            if (s.Vitals.IsDead) return ActionResult.Dead;
            if (!IsSlot(s, from) || !IsSlot(s, to)) return ActionResult.Invalid;
            s.Inventory.Move(from, to);
            return ActionResult.Ok;
        }

        public static ActionResult QuickMove(Survivor s, int slot)
        {
            if (s.Vitals.IsDead) return ActionResult.Dead;
            if (!IsSlot(s, slot)) return ActionResult.Invalid;
            return s.Inventory.QuickMove(slot) ? ActionResult.Ok : ActionResult.NoSpace;
        }

        public static ActionResult Split(Survivor s, int slot)
        {
            if (s.Vitals.IsDead) return ActionResult.Dead;
            if (!IsSlot(s, slot)) return ActionResult.Invalid;
            if (s.Inventory[slot].Amount < 2) return ActionResult.Invalid;
            return s.Inventory.Split(slot) >= 0 ? ActionResult.Ok : ActionResult.NoSpace;
        }

        public static ActionResult Craft(Survivor s, Recipe recipe)
        {
            if (recipe == null) return ActionResult.Invalid;
            if (s.Vitals.IsDead) return ActionResult.Dead;
            switch (s.Crafting.Enqueue(recipe))
            {
                case CraftingQueue.EnqueueResult.Queued: return ActionResult.Ok;
                case CraftingQueue.EnqueueResult.QueueFull: return ActionResult.QueueFull;
                default: return ActionResult.NotEnoughResources;
            }
        }

        public static ActionResult CancelCraft(Survivor s, int index)
        {
            if (s.Vitals.IsDead) return ActionResult.Dead;
            return s.Crafting.Cancel(index) ? ActionResult.Ok : ActionResult.Invalid;
        }

        /// <summary>Eats one of the food in a slot.</summary>
        public static ActionResult Eat(Survivor s, int slot)
        {
            if (s.Vitals.IsDead) return ActionResult.Dead;
            if (!IsSlot(s, slot)) return ActionResult.Invalid;
            var stack = s.Inventory[slot];
            if (stack.IsEmpty || !s.Inventory.Catalog.TryGet(stack.ItemId, out var food) || food.Category != ItemCategory.Food)
                return ActionResult.NotFood;
            s.Inventory.TakeFromSlot(slot, 1);
            s.Vitals.Consume(food.Nourishment);
            return ActionResult.Ok;
        }

        /// <summary>A mouthful of fresh water, for a player standing by a pond.</summary>
        public static ActionResult Drink(Survivor s, IslandGround ground, float x, float y, float z)
        {
            if (s.Vitals.IsDead) return ActionResult.Dead;
            foreach (var pond in ground.Ponds)
            {
                float dx = x - pond.X, dz = z - pond.Z;
                float reach = pond.Radius + DrinkReach;
                if (dx * dx + dz * dz > reach * reach || Math.Abs(y - pond.Level) > 4f) continue;
                s.Vitals.Consume(new Nourishment(0f, DrinkWater));
                return ActionResult.Ok;
            }
            return ActionResult.NoWater;
        }

        /// <summary>
        /// Wakes a dead player up as a fresh one: what they carried and were making is lost (later it
        /// will lie in a bag where they fell). Where they wake up is the caller's choice.
        /// </summary>
        public static ActionResult Respawn(Survivor s)
        {
            if (!s.Vitals.IsDead) return ActionResult.Invalid;
            s.Crafting.Clear();
            s.GiveStarterKit();
            s.Vitals.Respawn();
            s.Looting = 0;
            return ActionResult.Ok;
        }

        /// <summary>
        /// Wakes a dead player up in one of their sleeping bags, if it is ready (each waits
        /// <see cref="Deployables.BagCooldown"/> between uses). <paramref name="bag"/> is where to put them.
        /// </summary>
        public static ActionResult RespawnInBag(Settlement settlement, Survivor s, int owner, int bagId, float now, out Deployable bag)
        {
            var r = CanUseBag(settlement, owner, bagId, now, out bag);
            if (r != ActionResult.Ok) return r;
            if (!s.Vitals.IsDead) return ActionResult.Invalid;
            r = Respawn(s);
            if (r == ActionResult.Ok) settlement.UseBag(bagId, now);
            return r;
        }

        /// <summary>Whether this player can wake up in this sleeping bag now (theirs, and ready).</summary>
        public static ActionResult CanUseBag(Settlement settlement, int owner, int bagId, float now, out Deployable bag)
        {
            if (!settlement.TryGet(bagId, out bag) || bag.Kind != DeployableKind.SleepingBag) return ActionResult.NoPiece;
            if (bag.Owner != owner) return ActionResult.NotYours;
            if (now < settlement.BagReadyAt(bagId)) return ActionResult.NotReady;
            return ActionResult.Ok;
        }

        // ---- Boxes and furnaces

        /// <summary>Opens a box or furnace within reach: from now on its slots can be moved to and from.</summary>
        public static ActionResult Open(Settlement settlement, Survivor s, int id, float x, float y, float z)
        {
            if (s.Vitals.IsDead) return ActionResult.Dead;
            if (!settlement.TryGet(id, out var d) || settlement.Container(id) == null) return ActionResult.NoPiece;
            if (!Near(x, y, z, d.X, d.Y, d.Z, UseReach)) return ActionResult.TooFar;
            s.Looting = id;
            return ActionResult.Ok;
        }

        public static ActionResult Close(Survivor s)
        {
            s.Looting = 0;
            return ActionResult.Ok;
        }

        /// <summary>
        /// Takes from the open box's <paramref name="from"/> slot into inventory slot <paramref name="to"/>
        /// (merging, or swapping), or wherever it fits when <paramref name="to"/> is -1.
        /// </summary>
        public static ActionResult Take(Settlement settlement, Survivor s, int from, int to)
        {
            var r = OpenBox(settlement, s, out var box);
            if (r != ActionResult.Ok) return r;
            if (!IsSlot(box, from) || (to != -1 && !IsSlot(s.Inventory, to))) return ActionResult.Invalid;
            if (box[from].IsEmpty) return ActionResult.Invalid;
            bool moved = to == -1 ? Inventory.TransferAll(box, from, s.Inventory) : Inventory.Transfer(box, from, s.Inventory, to);
            return moved ? ActionResult.Ok : ActionResult.NoSpace;
        }

        /// <summary>Puts inventory slot <paramref name="from"/> into the open box's slot <paramref name="to"/> (or anywhere: -1).</summary>
        public static ActionResult Put(Settlement settlement, Survivor s, int from, int to)
        {
            var r = OpenBox(settlement, s, out var box);
            if (r != ActionResult.Ok) return r;
            if (!IsSlot(s.Inventory, from) || (to != -1 && !IsSlot(box, to))) return ActionResult.Invalid;
            if (s.Inventory[from].IsEmpty) return ActionResult.Invalid;
            bool moved = to == -1 ? Inventory.TransferAll(s.Inventory, from, box) : Inventory.Transfer(s.Inventory, from, box, to);
            return moved ? ActionResult.Ok : ActionResult.NoSpace;
        }

        /// <summary>Moves a slot of the open box onto another of its slots.</summary>
        public static ActionResult MoveInBox(Settlement settlement, Survivor s, int from, int to)
        {
            var r = OpenBox(settlement, s, out var box);
            if (r != ActionResult.Ok) return r;
            if (!IsSlot(box, from) || !IsSlot(box, to) || from == to) return ActionResult.Invalid;
            box.Move(from, to);
            return ActionResult.Ok;
        }

        private static ActionResult OpenBox(Settlement settlement, Survivor s, out Inventory box)
        {
            box = null;
            if (s.Vitals.IsDead) return ActionResult.Dead;
            if (s.Looting == 0) return ActionResult.NotOpen;
            box = settlement.Container(s.Looting);
            if (box == null)
            {
                s.Looting = 0;
                return ActionResult.NoPiece;
            }
            return ActionResult.Ok;
        }

        // ---- Building

        /// <summary>
        /// Builds, hammers, hangs a door, opens one or lights a campfire, for a player standing at
        /// (x, y, z) with this owner id. <paramref name="done"/> is the change as made (the floor's
        /// height, the new tier, the campfire's id), to apply to every copy of the settlement.
        /// </summary>
        public static ActionResult Build(Settlement settlement, Survivor s, int owner, BuildOp op,
            float x, float y, float z, out BuildOp done)
        {
            done = op;
            if (s.Vitals.IsDead) return ActionResult.Dead;
            var grid = settlement.Grid;
            switch (op.Kind)
            {
                case BuildOpKind.Foundation:
                {
                    var r = FoundationTop(settlement, op.Cell, out float top);
                    if (r != ActionResult.Ok) return r;
                    if (!Near(x, y, z, (op.X + 0.5f) * S, top, (op.Z + 0.5f) * S, BuildReach)) return ActionResult.TooFar;
                    if (!settlement.HasPrivilege(owner, (op.X + 0.5f) * S, (op.Z + 0.5f) * S)) return ActionResult.BuildingBlocked;
                    done = op.WithTop(top);
                    return Place(settlement, s, owner, PieceRef.Foundation(op.Cell), grid.CanPlaceFoundation(op.Cell),
                        () => grid.PlaceFoundation(op.Cell, top));
                }
                case BuildOpKind.Edge:
                {
                    if (!PieceRef.IsEdgeType(op.Type)) return ActionResult.Invalid;
                    var edge = op.EdgeAt;
                    var can = grid.CanPlaceEdge(edge);
                    if (can == PlaceResult.Ok && !Near(x, y, z, EdgePoint(grid, edge), BuildReach)) return ActionResult.TooFar;
                    if (can == PlaceResult.Ok && !Privileged(settlement, owner, EdgePoint(grid, edge))) return ActionResult.BuildingBlocked;
                    return Place(settlement, s, owner, PieceRef.OnEdge(edge, op.Type), can, () => grid.PlaceEdge(edge, op.Type));
                }
                case BuildOpKind.Roof:
                {
                    var can = grid.CanPlaceRoof(op.Cell);
                    if (can == PlaceResult.Ok && !Near(x, y, z, PiecePoint(grid, PieceRef.Roof(op.Cell)), BuildReach))
                        return ActionResult.TooFar;
                    if (can == PlaceResult.Ok && !Privileged(settlement, owner, PiecePoint(grid, PieceRef.Roof(op.Cell))))
                        return ActionResult.BuildingBlocked;
                    return Place(settlement, s, owner, PieceRef.Roof(op.Cell), can, () => grid.PlaceRoof(op.Cell));
                }
                case BuildOpKind.Upgrade:
                {
                    var piece = BuildOps.Stored(grid, op.Piece);
                    if (!grid.TryGetTier(piece, out var tier)) return ActionResult.NoPiece;
                    if (!Mine(settlement, piece, owner)) return ActionResult.NotYours;
                    if (!BuildingTiers.TryNext(tier, out var next)) return ActionResult.MaxTier;
                    if (!Privileged(settlement, owner, PiecePoint(grid, piece))) return ActionResult.BuildingBlocked;
                    if (!Near(x, y, z, PiecePoint(grid, piece), BuildReach)) return ActionResult.TooFar;
                    var cost = BuildCosts.Upgrade(piece.Type, next);
                    if (!BuildCosts.CanAfford(s.Inventory, cost)) return ActionResult.NotEnoughResources;
                    if (grid.Upgrade(piece, next) != PlaceResult.Ok) return ActionResult.Invalid;
                    BuildCosts.TryPay(s.Inventory, cost);
                    done = BuildOp.Upgrade(piece, next);
                    return ActionResult.Ok;
                }
                case BuildOpKind.Remove:
                {
                    var piece = BuildOps.Stored(grid, op.Piece);
                    if (!grid.Exists(piece)) return ActionResult.NoPiece;
                    if (!Mine(settlement, piece, owner)) return ActionResult.NotYours;
                    if (!Near(x, y, z, PiecePoint(grid, piece), BuildReach)) return ActionResult.TooFar;
                    // Half of everything that falls comes back, and any door hung in it.
                    void Refund(PieceRef fallen, BuildingTier tier)
                    {
                        foreach (var item in BuildCosts.Refund(fallen.Type, tier)) s.Inventory.Add(item.ItemId, item.Amount);
                    }
                    void ReturnDoor(Edge e) => s.Inventory.Add(ItemIds.WoodenDoor, 1);
                    grid.PieceRemoved += Refund;
                    grid.DoorRemoved += ReturnDoor;
                    try { grid.Remove(piece); }
                    finally
                    {
                        grid.PieceRemoved -= Refund;
                        grid.DoorRemoved -= ReturnDoor;
                    }
                    done = BuildOp.Remove(piece);
                    return ActionResult.Ok;
                }
                case BuildOpKind.Door:
                {
                    var edge = op.EdgeAt;
                    var can = grid.CanPlaceDoor(edge);
                    if (can != PlaceResult.Ok) return From(can);
                    if (!Near(x, y, z, EdgePoint(grid, edge), UseReach)) return ActionResult.TooFar;
                    if (!Privileged(settlement, owner, EdgePoint(grid, edge))) return ActionResult.BuildingBlocked;
                    if (!s.Inventory.Has(ItemIds.WoodenDoor, 1)) return ActionResult.NotEnoughResources;
                    grid.PlaceDoor(edge);
                    s.Inventory.Remove(ItemIds.WoodenDoor, 1);
                    done = BuildOp.Door(edge);
                    return ActionResult.Ok;
                }
                case BuildOpKind.ToggleDoor:
                {
                    var edge = op.EdgeAt;
                    if (!grid.HasDoor(edge)) return ActionResult.NoDoor;
                    if (!Near(x, y, z, EdgePoint(grid, edge), UseReach)) return ActionResult.TooFar;
                    grid.ToggleDoor(edge);
                    done = BuildOp.ToggleDoor(edge, grid.IsDoorOpen(edge));
                    return ActionResult.Ok;
                }
                case BuildOpKind.Deploy:
                {
                    var d = op.Deployed;
                    if (!Deployables.IsKnown(d.Kind)) return ActionResult.Invalid;
                    var r = CanDeploy(settlement, owner, d.Kind, d.X, d.Y, d.Z);
                    if (r != ActionResult.Ok) return r;
                    if (!Near(x, y, z, d.X, d.Y, d.Z, UseReach)) return ActionResult.TooFar;
                    if (!s.Inventory.Remove(Deployables.ItemOf(d.Kind), 1)) return ActionResult.NotEnoughResources;
                    var placed = settlement.Add(new Deployable(0, d.Kind, d.X, d.Y, d.Z, d.Yaw, owner));
                    done = BuildOp.Deploy(placed);
                    return ActionResult.Ok;
                }
                case BuildOpKind.Pickup:
                {
                    if (!settlement.TryGet(op.Id, out var d)) return ActionResult.NoPiece;
                    if (d.Owner != 0 && d.Owner != owner) return ActionResult.NotYours;
                    if (!Near(x, y, z, d.X, d.Y, d.Z, UseReach)) return ActionResult.TooFar;
                    var box = settlement.Container(d.Id);
                    if ((box != null && !box.IsEmpty) || settlement.IsLit(d.Id)) return ActionResult.NotEmpty;
                    string item = Deployables.ItemOf(d.Kind);
                    if (s.Inventory.SpaceFor(item) < 1) return ActionResult.NoSpace;
                    settlement.Remove(d.Id);
                    s.Inventory.Add(item, 1);
                    done = BuildOp.Pickup(d.Id);
                    return ActionResult.Ok;
                }
                case BuildOpKind.Light:
                {
                    if (!settlement.TryGet(op.Id, out var d)) return ActionResult.NoPiece;
                    if (d.Kind != DeployableKind.Furnace) return ActionResult.Invalid;
                    if (!Near(x, y, z, d.X, d.Y, d.Z, UseReach)) return ActionResult.TooFar;
                    if (op.Open && !settlement.Container(d.Id).Has(ItemIds.Wood, 1)) return ActionResult.NoFuel;
                    settlement.SetLit(d.Id, op.Open);
                    done = BuildOp.Light(d.Id, op.Open);
                    return ActionResult.Ok;
                }
                case BuildOpKind.Authorize:
                {
                    if (!settlement.TryGet(op.Id, out var d)) return ActionResult.NoPiece;
                    if (d.Kind != DeployableKind.ToolCupboard) return ActionResult.Invalid;
                    if (!Near(x, y, z, d.X, d.Y, d.Z, UseReach)) return ActionResult.TooFar;
                    settlement.Authorize(d.Id, owner);
                    done = BuildOp.Authorize(d.Id, owner);
                    return ActionResult.Ok;
                }
                case BuildOpKind.ClearAuthorized:
                {
                    if (!settlement.TryGet(op.Id, out var d)) return ActionResult.NoPiece;
                    if (d.Kind != DeployableKind.ToolCupboard) return ActionResult.Invalid;
                    if (!Near(x, y, z, d.X, d.Y, d.Z, UseReach)) return ActionResult.TooFar;
                    if (!settlement.IsAuthorized(d.Id, owner)) return ActionResult.NotYours;
                    settlement.ClearAuthorized(d.Id);
                    done = BuildOp.ClearAuthorized(d.Id);
                    return ActionResult.Ok;
                }
                default:
                    return ActionResult.Invalid;
            }
        }

        /// <summary>
        /// Whether a deployable of this kind can stand at (x, y, z) for this player: on the ground or
        /// a floor, out of the water, clear of other deployables, and where they may build.
        /// </summary>
        public static ActionResult CanDeploy(Settlement settlement, int owner, DeployableKind kind, float x, float y, float z)
        {
            float ground = settlement.Ground.Height(x, z);
            if (ground < IslandGround.WaterLevel + 0.3f) return ActionResult.InWater;
            float floor = settlement.Grid.TryGetFoundationTop(BuildingGrid.CellAt(x, z), out float top) ? top : ground;
            if (Math.Abs(y - floor) > 0.7f && Math.Abs(y - ground) > 0.7f) return ActionResult.Invalid;
            if (!settlement.HasPrivilege(owner, x, z)) return ActionResult.BuildingBlocked;
            float radius = Deployables.Radius(kind);
            foreach (var other in settlement.Deployables)
            {
                float dx = x - other.X, dz = z - other.Z, room = radius + Deployables.Radius(other.Kind);
                if (dx * dx + dz * dz < room * room && Math.Abs(y - other.Y) < 1.5f) return ActionResult.TooClose;
            }
            return ActionResult.Ok;
        }

        /// <summary>
        /// The height a floor in this cell would have, and whether the ground allows one: level with a
        /// neighbouring floor if there is one (so rooms come out flat), otherwise just above the highest
        /// ground under it; not in water, on a cliff, or buried.
        /// </summary>
        public static ActionResult FoundationTop(Settlement settlement, Cell cell, out float top)
        {
            var grid = settlement.Grid;
            var ground = settlement.Ground;
            float x0 = cell.X * S, z0 = cell.Z * S;
            float a = ground.Height(x0, z0), b = ground.Height(x0 + S, z0);
            float c = ground.Height(x0, z0 + S), d = ground.Height(x0 + S, z0 + S);
            float e = ground.Height(x0 + S * 0.5f, z0 + S * 0.5f);
            float highest = Math.Max(Math.Max(a, b), Math.Max(c, Math.Max(d, e)));
            float lowest = Math.Min(Math.Min(a, b), Math.Min(c, Math.Min(d, e)));

            top = highest + 0.25f;
            if (grid.TryGetFoundationTop(new Cell(cell.X + 1, cell.Z), out float t)
                || grid.TryGetFoundationTop(new Cell(cell.X - 1, cell.Z), out t)
                || grid.TryGetFoundationTop(new Cell(cell.X, cell.Z + 1), out t)
                || grid.TryGetFoundationTop(new Cell(cell.X, cell.Z - 1), out t))
                top = t;

            if (grid.HasFoundation(cell)) return ActionResult.Occupied;
            if (lowest < 0.4f) return ActionResult.InWater;
            if (top - lowest > 2.4f) return ActionResult.TooSteep;
            if (top < highest - 0.4f) return ActionResult.GroundInTheWay;
            return ActionResult.Ok;
        }

        /// <summary>Where a piece's middle is, for reach checks.</summary>
        public static (float X, float Y, float Z) PiecePoint(BuildingGrid grid, PieceRef piece)
        {
            switch (piece.Type)
            {
                case PieceType.Foundation:
                {
                    grid.TryGetFoundationTop(piece.Cell, out float top);
                    return ((piece.Cell.X + 0.5f) * S, top, (piece.Cell.Z + 0.5f) * S);
                }
                case PieceType.Roof:
                {
                    grid.TryGetFoundationTop(piece.Cell, out float floor);
                    return ((piece.Cell.X + 0.5f) * S, floor + BuildingGrid.WallHeight, (piece.Cell.Z + 0.5f) * S);
                }
                default:
                    return EdgePoint(grid, piece.Edge);
            }
        }

        /// <summary>The middle of a wall or doorway (half way up).</summary>
        public static (float X, float Y, float Z) EdgePoint(BuildingGrid grid, Edge edge)
        {
            float y = grid.EdgeBaseHeight(edge) + BuildingGrid.WallHeight * 0.5f;
            return edge.Axis == EdgeAxis.AlongX ? ((edge.X + 0.5f) * S, y, edge.Z * S) : (edge.X * S, y, (edge.Z + 0.5f) * S);
        }

        public static ActionResult From(PlaceResult result)
        {
            switch (result)
            {
                case PlaceResult.Ok: return ActionResult.Ok;
                case PlaceResult.Occupied: return ActionResult.Occupied;
                case PlaceResult.NeedsFoundation: return ActionResult.NeedsFoundation;
                case PlaceResult.NeedsWall: return ActionResult.NeedsWall;
                case PlaceResult.NotEnoughResources: return ActionResult.NotEnoughResources;
                case PlaceResult.NeedsDoorway: return ActionResult.NeedsDoorway;
                case PlaceResult.NoPiece: return ActionResult.NoPiece;
                case PlaceResult.NotAnUpgrade: return ActionResult.MaxTier;
                default: return ActionResult.Invalid;
            }
        }

        private static ActionResult Place(Settlement settlement, Survivor s, int owner, PieceRef piece, PlaceResult can, Func<PlaceResult> place)
        {
            if (can != PlaceResult.Ok) return From(can);
            if (!BuildCosts.CanAfford(s.Inventory, piece.Type)) return ActionResult.NotEnoughResources;
            var r = place();
            if (r != PlaceResult.Ok) return From(r);
            BuildCosts.TryPay(s.Inventory, piece.Type);
            settlement.SetOwner(piece, owner);
            return ActionResult.Ok;
        }

        /// <summary>Whether this player may hammer a piece: their own, or one nobody in particular built.</summary>
        private static bool Mine(Settlement settlement, PieceRef piece, int owner)
        {
            int builder = settlement.OwnerOf(piece);
            return builder == 0 || builder == owner;
        }

        private static bool IsSlot(Survivor s, int slot) => slot >= 0 && slot < s.Inventory.SlotCount;

        private static bool IsSlot(Inventory inventory, int slot) => slot >= 0 && slot < inventory.SlotCount;

        private static bool Privileged(Settlement settlement, int owner, (float X, float Y, float Z) p) =>
            settlement.HasPrivilege(owner, p.X, p.Z);

        private static bool Near(float x, float y, float z, (float X, float Y, float Z) p, float reach) =>
            Near(x, y, z, p.X, p.Y, p.Z, reach);

        private static bool Near(float x, float y, float z, float px, float py, float pz, float reach)
        {
            float dx = x - px, dz = z - pz;
            return dx * dx + dz * dz <= reach * reach && Math.Abs(y - py) <= reach;
        }
    }
}
