using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Net;

namespace Oxfall.Core.Rules
{
    /// <summary>Kinds of change to the settlement. Sent as numbers: only ever add at the end.</summary>
    public enum BuildOpKind : byte
    {
        Foundation = 1,
        Edge = 2,
        Roof = 3,
        Upgrade = 4,
        Remove = 5,
        Door = 6,
        ToggleDoor = 7,
        Deploy = 8,
        Pickup = 9,
        Light = 10,
        Authorize = 11,
        ClearAuthorized = 12,
        PlaceLock = 13,
        SetCode = 14,
        EnterCode = 15,
        LockAuthorize = 16,
        SetLocked = 17,
        RemoveLock = 18,
        /// <summary>A piece's or a door's health is now this (after a blast or a repair); at 0 it falls.</summary>
        Health = 19,
        /// <summary>Mend a piece or a door with the hammer (asked for; the server answers with <see cref="Health"/>).</summary>
        Repair = 20,
        /// <summary>A deployable's health is now this (after a blast); at 0 it is destroyed, what it held spilling into a bag.</summary>
        DeployableHealth = 21,
    }

    /// <summary>
    /// One change to what stands on the island. A player asks for one (the server checks and charges
    /// for it), and the server tells everyone the change it made, which each copy then applies as is.
    /// </summary>
    public readonly struct BuildOp
    {
        /// <summary>Coordinates past this are not on the island (and not worth trusting).</summary>
        private const int MaxCoordinate = 4096;

        public readonly BuildOpKind Kind;
        public readonly PieceType Type;
        public readonly int X, Z;
        public readonly EdgeAxis Axis;
        public readonly BuildingTier Tier;
        public readonly bool Open;
        public readonly float Top;

        /// <summary>The thing set down (Deploy), or the one acted on: its id, and for Authorize who.</summary>
        public readonly Deployable Deployed;

        /// <summary>A lock's code (SetCode, EnterCode), or what a door is made of (Door).</summary>
        public readonly int Code;

        private BuildOp(BuildOpKind kind, PieceType type, int x, int z, EdgeAxis axis, BuildingTier tier = BuildingTier.Twig,
            bool open = false, float top = 0f, Deployable deployed = default, int code = 0)
        {
            Code = code;
            Kind = kind;
            Type = type;
            X = x;
            Z = z;
            Axis = axis;
            Tier = tier;
            Open = open;
            Top = top;
            Deployed = deployed;
        }

        public static BuildOp Foundation(Cell cell, float top) =>
            new BuildOp(BuildOpKind.Foundation, PieceType.Foundation, cell.X, cell.Z, default, top: top);

        public static BuildOp Edge(Edge edge, PieceType type) =>
            new BuildOp(BuildOpKind.Edge, type, edge.X, edge.Z, edge.Axis);

        public static BuildOp Roof(Cell cell) => new BuildOp(BuildOpKind.Roof, PieceType.Roof, cell.X, cell.Z, default);

        public static BuildOp Upgrade(PieceRef piece, BuildingTier tier) => OnPiece(BuildOpKind.Upgrade, piece, tier);

        public static BuildOp Remove(PieceRef piece) => OnPiece(BuildOpKind.Remove, piece, BuildingTier.Twig);

        /// <summary>A piece's health is now <paramref name="health"/> (0 or less: it fell).</summary>
        public static BuildOp Health(PieceRef piece, float health) =>
            piece.IsEdge
                ? new BuildOp(BuildOpKind.Health, piece.Type, piece.Edge.X, piece.Edge.Z, piece.Edge.Axis, top: health)
                : new BuildOp(BuildOpKind.Health, piece.Type, piece.Cell.X, piece.Cell.Z, default, top: health);

        /// <summary>The door in a doorway has <paramref name="health"/> left (0 or less: it broke).</summary>
        public static BuildOp DoorHealth(Edge edge, float health) =>
            new BuildOp(BuildOpKind.Health, PieceType.Doorway, edge.X, edge.Z, edge.Axis, open: true, top: health);

        /// <summary>A deployable's health is now this; at 0 it is destroyed.</summary>
        public static BuildOp DeployableHealth(int id, float health) => OnDeployable(BuildOpKind.DeployableHealth, id).WithTop(health);

        /// <summary>Mends a piece to full health, for some of what it is made of.</summary>
        public static BuildOp Repair(PieceRef piece) => piece.IsEdge
            ? new BuildOp(BuildOpKind.Repair, piece.Type, piece.Edge.X, piece.Edge.Z, piece.Edge.Axis)
            : new BuildOp(BuildOpKind.Repair, piece.Type, piece.Cell.X, piece.Cell.Z, default);

        /// <summary>Mends the door in a doorway.</summary>
        public static BuildOp RepairDoor(Edge edge) =>
            new BuildOp(BuildOpKind.Repair, PieceType.Doorway, edge.X, edge.Z, edge.Axis, open: true);

        /// <summary>For <see cref="BuildOpKind.Health"/> and <see cref="BuildOpKind.Repair"/>: about the door, not the piece.</summary>
        public bool OnDoor => Open;

        /// <summary>For <see cref="BuildOpKind.Health"/>: the health now.</summary>
        public float HealthLeft => Top;

        public static BuildOp Door(Edge edge, DoorKind kind = DoorKind.Wood) =>
            new BuildOp(BuildOpKind.Door, PieceType.Doorway, edge.X, edge.Z, edge.Axis, code: (int)kind);

        public DoorKind DoorKind => (DoorKind)Code;

        public static BuildOp ToggleDoor(Edge edge, bool open) =>
            new BuildOp(BuildOpKind.ToggleDoor, PieceType.Doorway, edge.X, edge.Z, edge.Axis, open: open);

        /// <summary>Sets a campfire, box, furnace, sleeping bag or tool cupboard down.</summary>
        public static BuildOp Deploy(Deployable deployable) =>
            new BuildOp(BuildOpKind.Deploy, PieceType.Foundation, 0, 0, default, deployed: deployable);

        /// <summary>Picks a deployable back up (it goes back in the inventory).</summary>
        public static BuildOp Pickup(int id) => OnDeployable(BuildOpKind.Pickup, id);

        /// <summary>Lights a furnace, or puts it out.</summary>
        public static BuildOp Light(int id, bool on) =>
            new BuildOp(BuildOpKind.Light, PieceType.Foundation, 0, 0, default, open: on, deployed: Target(id));

        /// <summary>Authorizes a player on a tool cupboard (asking, the server fills in who).</summary>
        public static BuildOp Authorize(int cupboard, int owner = 0) =>
            new BuildOp(BuildOpKind.Authorize, PieceType.Foundation, 0, 0, default, deployed: Target(cupboard).WithOwner(owner));

        /// <summary>Empties a tool cupboard's list.</summary>
        public static BuildOp ClearAuthorized(int cupboard) => OnDeployable(BuildOpKind.ClearAuthorized, cupboard);

        // ---- Code locks (on a door: the edge; on a box or cupboard: its id)

        /// <summary>Puts a code lock on (asking, the server fills in whose it is).</summary>
        public static BuildOp PlaceLock(LockTarget target, int owner = 0) => OnLock(BuildOpKind.PlaceLock, target, owner: owner);

        /// <summary>
        /// Sets the code and locks. The server tells everyone else <see cref="CodeLocks.Hidden"/> in
        /// place of the code, with who set it.
        /// </summary>
        public static BuildOp SetCode(LockTarget target, int code, int owner = 0) => OnLock(BuildOpKind.SetCode, target, code, owner);

        /// <summary>Tries a code (sent to the server only; it answers with <see cref="LockAuthorize"/>).</summary>
        public static BuildOp EnterCode(LockTarget target, int code) => OnLock(BuildOpKind.EnterCode, target, code);

        /// <summary>Someone got the code right.</summary>
        public static BuildOp LockAuthorize(LockTarget target, int owner) => OnLock(BuildOpKind.LockAuthorize, target, owner: owner);

        public static BuildOp SetLocked(LockTarget target, bool locked) => OnLock(BuildOpKind.SetLocked, target, open: locked);

        /// <summary>Takes a lock off (it goes back in the inventory).</summary>
        public static BuildOp RemoveLock(LockTarget target) => OnLock(BuildOpKind.RemoveLock, target);

        private static BuildOp OnLock(BuildOpKind kind, LockTarget target, int code = 0, int owner = 0, bool open = false) => target.IsDoor
            ? new BuildOp(kind, PieceType.Doorway, target.Edge.X, target.Edge.Z, target.Edge.Axis, open: open,
                deployed: Target(0).WithOwner(owner), code: code)
            : new BuildOp(kind, PieceType.Foundation, 0, 0, default, open: open, deployed: Target(target.Id).WithOwner(owner), code: code);

        /// <summary>What a lock op is about.</summary>
        public LockTarget Lock => Type == PieceType.Doorway ? LockTarget.Door(EdgeAt) : LockTarget.On(Id);

        public bool IsLockOp => Kind >= BuildOpKind.PlaceLock && Kind <= BuildOpKind.RemoveLock;

        private static BuildOp OnDeployable(BuildOpKind kind, int id) =>
            new BuildOp(kind, PieceType.Foundation, 0, 0, default, deployed: Target(id));

        private static Deployable Target(int id) => new Deployable(id, DeployableKind.Campfire, 0f, 0f, 0f, 0f);

        public int Id => Deployed.Id;

        private static BuildOp OnPiece(BuildOpKind kind, PieceRef piece, BuildingTier tier) => piece.IsEdge
            ? new BuildOp(kind, piece.Type, piece.Edge.X, piece.Edge.Z, piece.Edge.Axis, tier)
            : new BuildOp(kind, piece.Type, piece.Cell.X, piece.Cell.Z, default, tier);

        public Cell Cell => new Cell(X, Z);
        public Edge EdgeAt => new Edge(X, Z, Axis);

        /// <summary>The piece an upgrade or removal is about.</summary>
        public PieceRef Piece => PieceRef.IsEdgeType(Type)
            ? PieceRef.OnEdge(EdgeAt, Type)
            : Type == PieceType.Roof ? PieceRef.Roof(Cell) : PieceRef.Foundation(Cell);

        public BuildOp WithTop(float top) => new BuildOp(Kind, Type, X, Z, Axis, Tier, Open, top, Deployed, Code);
        public BuildOp WithTier(BuildingTier tier) => new BuildOp(Kind, Type, X, Z, Axis, tier, Open, Top, Deployed, Code);
        public BuildOp WithOpen(bool open) => new BuildOp(Kind, Type, X, Z, Axis, Tier, open, Top, Deployed, Code);

        public override string ToString()
        {
            switch (Kind)
            {
                case BuildOpKind.Deploy: return "Deploy " + Deployed;
                case BuildOpKind.Pickup: return "Pickup " + Id;
                case BuildOpKind.Light: return $"Light {Id} {(Open ? "on" : "off")}";
                case BuildOpKind.Authorize: return $"Authorize {Deployed.Owner} on {Id}";
                case BuildOpKind.ClearAuthorized: return "ClearAuthorized " + Id;
                case BuildOpKind.Door: return $"Door {DoorKind} {EdgeAt}";
                case BuildOpKind.PlaceLock: return $"PlaceLock {Lock} by {Deployed.Owner}";
                case BuildOpKind.SetCode: return $"SetCode {Lock} by {Deployed.Owner}";
                case BuildOpKind.EnterCode: return $"EnterCode {Lock}";
                case BuildOpKind.LockAuthorize: return $"LockAuthorize {Deployed.Owner} on {Lock}";
                case BuildOpKind.SetLocked: return $"SetLocked {Lock} {(Open ? "locked" : "open")}";
                case BuildOpKind.RemoveLock: return $"RemoveLock {Lock}";
                case BuildOpKind.Health: return $"Health {(OnDoor ? "door " + EdgeAt : Piece.ToString())} {Top:0.#}";
                case BuildOpKind.Repair: return $"Repair {(OnDoor ? "door " + EdgeAt : Piece.ToString())}";
                case BuildOpKind.DeployableHealth: return $"DeployableHealth {Id} {Top:0.#}";
                default: return $"{Kind} {Type} ({X},{Z}{(PieceRef.IsEdgeType(Type) ? "," + Axis : "")})";
            }
        }

        // ---- On the wire

        public void Write(NetWriter w)
        {
            w.Byte((byte)Kind);
            switch (Kind)
            {
                case BuildOpKind.Foundation:
                    w.Int(X);
                    w.Int(Z);
                    w.Float(Top);
                    break;
                case BuildOpKind.Edge:
                    w.Int(X);
                    w.Int(Z);
                    w.Byte((byte)Axis);
                    w.Byte((byte)Type);
                    break;
                case BuildOpKind.Roof:
                    w.Int(X);
                    w.Int(Z);
                    break;
                case BuildOpKind.Upgrade:
                case BuildOpKind.Remove:
                    w.Byte((byte)Type);
                    w.Int(X);
                    w.Int(Z);
                    w.Byte((byte)Axis);
                    w.Byte((byte)Tier);
                    break;
                case BuildOpKind.Door:
                case BuildOpKind.ToggleDoor:
                    w.Int(X);
                    w.Int(Z);
                    w.Byte((byte)Axis);
                    w.Bool(Open);
                    if (Kind == BuildOpKind.Door) w.Byte((byte)DoorKind);
                    break;
                case BuildOpKind.Deploy:
                    w.Int(Deployed.Id);
                    w.Byte((byte)Deployed.Kind);
                    w.Float(Deployed.X);
                    w.Float(Deployed.Y);
                    w.Float(Deployed.Z);
                    w.Float(Deployed.Yaw);
                    w.Int(Deployed.Owner);
                    break;
                case BuildOpKind.Pickup:
                case BuildOpKind.ClearAuthorized:
                    w.Int(Id);
                    break;
                case BuildOpKind.Light:
                    w.Int(Id);
                    w.Bool(Open);
                    break;
                case BuildOpKind.Authorize:
                    w.Int(Id);
                    w.Int(Deployed.Owner);
                    break;
                case BuildOpKind.PlaceLock:
                case BuildOpKind.LockAuthorize:
                    WriteLock(w);
                    w.Int(Deployed.Owner);
                    break;
                case BuildOpKind.SetCode:
                    WriteLock(w);
                    w.Int(Code);
                    w.Int(Deployed.Owner);
                    break;
                case BuildOpKind.EnterCode:
                    WriteLock(w);
                    w.Int(Code);
                    break;
                case BuildOpKind.SetLocked:
                    WriteLock(w);
                    w.Bool(Open);
                    break;
                case BuildOpKind.RemoveLock:
                    WriteLock(w);
                    break;
                case BuildOpKind.Health:
                case BuildOpKind.Repair:
                    w.Byte((byte)Type);
                    w.Int(X);
                    w.Int(Z);
                    w.Byte((byte)Axis);
                    w.Bool(Open);
                    if (Kind == BuildOpKind.Health) w.Float(Top);
                    break;
                case BuildOpKind.DeployableHealth:
                    w.Int(Id);
                    w.Float(Top);
                    break;
                default:
                    throw new InvalidOperationException("Unknown build op " + Kind);
            }
        }

        public static BuildOp Read(NetReader r)
        {
            var kind = (BuildOpKind)r.Byte();
            switch (kind)
            {
                case BuildOpKind.Foundation:
                {
                    var cell = new Cell(Coordinate(r), Coordinate(r));
                    return Foundation(cell, Finite(r.Float()));
                }
                case BuildOpKind.Edge:
                {
                    var edge = new Edge(Coordinate(r), Coordinate(r), ReadAxis(r));
                    var type = ReadType(r);
                    if (!PieceRef.IsEdgeType(type)) throw new ProtocolException("Not an edge piece.");
                    return Edge(edge, type);
                }
                case BuildOpKind.Roof:
                    return Roof(new Cell(Coordinate(r), Coordinate(r)));
                case BuildOpKind.Upgrade:
                case BuildOpKind.Remove:
                {
                    var type = ReadType(r);
                    int x = Coordinate(r), z = Coordinate(r);
                    var axis = ReadAxis(r);
                    var tier = (BuildingTier)r.Byte();
                    if (!Enum.IsDefined(typeof(BuildingTier), tier)) throw new ProtocolException("Unknown tier.");
                    var piece = PieceRef.IsEdgeType(type) ? PieceRef.OnEdge(new Edge(x, z, axis), type)
                        : type == PieceType.Roof ? PieceRef.Roof(new Cell(x, z)) : PieceRef.Foundation(new Cell(x, z));
                    return kind == BuildOpKind.Upgrade ? Upgrade(piece, tier) : Remove(piece);
                }
                case BuildOpKind.Door:
                case BuildOpKind.ToggleDoor:
                {
                    var edge = new Edge(Coordinate(r), Coordinate(r), ReadAxis(r));
                    bool open = r.Bool();
                    if (kind == BuildOpKind.ToggleDoor) return ToggleDoor(edge, open);
                    var door = (DoorKind)r.Byte();
                    if (!DoorKinds.IsKnown(door)) throw new ProtocolException("Unknown door.");
                    return Door(edge, door);
                }
                case BuildOpKind.Deploy:
                {
                    int id = r.Int();
                    var what = (DeployableKind)r.Byte();
                    if (!Deployables.IsKnown(what)) throw new ProtocolException("Unknown deployable.");
                    float x = Finite(r.Float()), y = Finite(r.Float()), z = Finite(r.Float()), yaw = Finite(r.Float());
                    int owner = r.Int();
                    return Deploy(new Deployable(id, what, x, y, z, yaw, owner));
                }
                case BuildOpKind.Pickup:
                    return Pickup(r.Int());
                case BuildOpKind.ClearAuthorized:
                    return ClearAuthorized(r.Int());
                case BuildOpKind.Light:
                {
                    int id = r.Int();
                    return Light(id, r.Bool());
                }
                case BuildOpKind.Authorize:
                {
                    int id = r.Int();
                    return Authorize(id, r.Int());
                }
                case BuildOpKind.PlaceLock:
                {
                    var target = ReadLock(r);
                    return PlaceLock(target, r.Int());
                }
                case BuildOpKind.LockAuthorize:
                {
                    var target = ReadLock(r);
                    return LockAuthorize(target, r.Int());
                }
                case BuildOpKind.SetCode:
                {
                    var target = ReadLock(r);
                    int code = r.Int();
                    return SetCode(target, code, r.Int());
                }
                case BuildOpKind.EnterCode:
                {
                    var target = ReadLock(r);
                    return EnterCode(target, r.Int());
                }
                case BuildOpKind.SetLocked:
                {
                    var target = ReadLock(r);
                    return SetLocked(target, r.Bool());
                }
                case BuildOpKind.RemoveLock:
                    return RemoveLock(ReadLock(r));
                case BuildOpKind.Health:
                case BuildOpKind.Repair:
                {
                    var type = ReadType(r);
                    int x = Coordinate(r), z = Coordinate(r);
                    var axis = ReadAxis(r);
                    bool door = r.Bool();
                    var piece = PieceRef.IsEdgeType(type) ? PieceRef.OnEdge(new Edge(x, z, axis), type)
                        : type == PieceType.Roof ? PieceRef.Roof(new Cell(x, z)) : PieceRef.Foundation(new Cell(x, z));
                    if (door && type != PieceType.Doorway) throw new ProtocolException("Only a doorway has a door.");
                    if (kind == BuildOpKind.Repair) return door ? RepairDoor(piece.Edge) : Repair(piece);
                    float health = Finite(r.Float());
                    return door ? DoorHealth(piece.Edge, health) : Health(piece, health);
                }
                case BuildOpKind.DeployableHealth:
                {
                    int id = r.Int();
                    return DeployableHealth(id, Finite(r.Float()));
                }
                default:
                    throw new ProtocolException("Unknown build op " + (byte)kind + ".");
            }
        }

        private void WriteLock(NetWriter w)
        {
            var target = Lock;
            w.Bool(target.IsDoor);
            if (target.IsDoor)
            {
                w.Int(target.Edge.X);
                w.Int(target.Edge.Z);
                w.Byte((byte)target.Edge.Axis);
            }
            else w.Int(target.Id);
        }

        private static LockTarget ReadLock(NetReader r) => r.Bool()
            ? LockTarget.Door(new Edge(Coordinate(r), Coordinate(r), ReadAxis(r)))
            : LockTarget.On(r.Int());

        private static int Coordinate(NetReader r)
        {
            int v = r.Int();
            if (v < -MaxCoordinate || v > MaxCoordinate) throw new ProtocolException("Coordinate off the island.");
            return v;
        }

        private static float Finite(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > MaxCoordinate * 4f) throw new ProtocolException("Bad number.");
            return v;
        }

        private static EdgeAxis ReadAxis(NetReader r)
        {
            var axis = (EdgeAxis)r.Byte();
            if (!Enum.IsDefined(typeof(EdgeAxis), axis)) throw new ProtocolException("Unknown edge axis.");
            return axis;
        }

        private static PieceType ReadType(NetReader r)
        {
            var type = (PieceType)r.Byte();
            if (!Enum.IsDefined(typeof(PieceType), type)) throw new ProtocolException("Unknown piece type.");
            return type;
        }
    }

    /// <summary>Applying the server's changes to a copy of the settlement, and describing one from scratch.</summary>
    public static class BuildOps
    {
        /// <summary>
        /// Applies a change the server already made (no checks, no charges). Returns false if it did
        /// not fit this copy, which would mean the copy has drifted.
        /// </summary>
        public static bool Apply(Settlement settlement, BuildOp op)
        {
            var grid = settlement.Grid;
            switch (op.Kind)
            {
                case BuildOpKind.Foundation: return grid.PlaceFoundation(op.Cell, op.Top) == PlaceResult.Ok;
                case BuildOpKind.Edge: return grid.PlaceEdge(op.EdgeAt, op.Type) == PlaceResult.Ok;
                case BuildOpKind.Roof: return grid.PlaceRoof(op.Cell) == PlaceResult.Ok;
                case BuildOpKind.Upgrade: return grid.Upgrade(Stored(grid, op.Piece), op.Tier) == PlaceResult.Ok;
                case BuildOpKind.Remove: return grid.Remove(Stored(grid, op.Piece));
                case BuildOpKind.Door: return grid.PlaceDoor(op.EdgeAt, op.DoorKind) == PlaceResult.Ok;
                case BuildOpKind.ToggleDoor:
                    if (!grid.HasDoor(op.EdgeAt)) return false;
                    return grid.IsDoorOpen(op.EdgeAt) == op.Open || grid.ToggleDoor(op.EdgeAt);
                case BuildOpKind.Deploy:
                    settlement.Add(op.Deployed);
                    return true;
                case BuildOpKind.Pickup:
                    return settlement.Remove(op.Id);
                case BuildOpKind.Light:
                    if (!settlement.TryGet(op.Id, out _)) return false;
                    settlement.SetLit(op.Id, op.Open);
                    return true;
                case BuildOpKind.Authorize:
                    if (!settlement.TryGet(op.Id, out _)) return false;
                    settlement.Authorize(op.Id, op.Deployed.Owner);
                    return true;
                case BuildOpKind.ClearAuthorized:
                    if (!settlement.TryGet(op.Id, out _)) return false;
                    settlement.ClearAuthorized(op.Id);
                    return true;
                case BuildOpKind.PlaceLock:
                    return settlement.AddLock(op.Lock, op.Deployed.Owner);
                case BuildOpKind.SetCode:
                    if (!settlement.HasLock(op.Lock)) return false;
                    settlement.SetCode(op.Lock, op.Code, op.Deployed.Owner);
                    return true;
                case BuildOpKind.LockAuthorize:
                    if (!settlement.HasLock(op.Lock)) return false;
                    settlement.AuthorizeLock(op.Lock, op.Deployed.Owner);
                    return true;
                case BuildOpKind.SetLocked:
                    if (!settlement.HasLock(op.Lock)) return false;
                    settlement.SetLocked(op.Lock, op.Open);
                    return true;
                case BuildOpKind.RemoveLock:
                    return settlement.RemoveLock(op.Lock);
                case BuildOpKind.Health:
                    if (op.OnDoor)
                    {
                        if (!grid.HasDoor(op.EdgeAt)) return false;
                        grid.SetDoorHealth(op.EdgeAt, op.HealthLeft);
                        return true;
                    }
                    {
                        var piece = Stored(grid, op.Piece);
                        if (!grid.Exists(piece)) return false;
                        grid.SetHealth(piece, op.HealthLeft);
                        return true;
                    }
                case BuildOpKind.DeployableHealth:
                    return settlement.SetDeployableHealth(op.Id, op.HealthLeft);
                default:
                    return false;
            }
        }

        /// <summary>The changes that build this settlement up from nothing, for a player who just joined.</summary>
        public static List<BuildOp> Describe(Settlement settlement)
        {
            var grid = settlement.Grid;
            var ops = new List<BuildOp>();
            var upgrades = new List<BuildOp>();
            void Tier(PieceRef piece)
            {
                if (grid.TryGetTier(piece, out var tier) && tier != BuildingTier.Twig) upgrades.Add(BuildOp.Upgrade(piece, tier));
            }

            foreach (var f in grid.Foundations)
            {
                ops.Add(BuildOp.Foundation(f.Key, f.Value));
                Tier(PieceRef.Foundation(f.Key));
            }
            foreach (var e in grid.EdgePieces)
            {
                ops.Add(BuildOp.Edge(e.Key, e.Value));
                Tier(PieceRef.OnEdge(e.Key, e.Value));
            }
            foreach (var cell in grid.Roofs)
            {
                ops.Add(BuildOp.Roof(cell));
                Tier(PieceRef.Roof(cell));
            }
            ops.AddRange(upgrades);
            // What blasts took off (after the upgrades, which mend a piece to its new tier's full health).
            foreach (var hurt in grid.Damaged) ops.Add(BuildOp.Health(hurt.Key, hurt.Value));
            foreach (var d in grid.Doors)
            {
                ops.Add(BuildOp.Door(d.Key, grid.DoorKindOf(d.Key)));
                if (d.Value) ops.Add(BuildOp.ToggleDoor(d.Key, true));
            }
            foreach (var hurt in grid.DamagedDoors) ops.Add(BuildOp.DoorHealth(hurt.Key, hurt.Value));
            foreach (var d in settlement.Deployables)
            {
                ops.Add(BuildOp.Deploy(d));
                if (settlement.IsLit(d.Id)) ops.Add(BuildOp.Light(d.Id, true));
                if (d.Kind != DeployableKind.ToolCupboard) continue;
                // A cupboard starts with its owner on it; the list as it is now replaces that.
                ops.Add(BuildOp.ClearAuthorized(d.Id));
                foreach (int owner in settlement.AuthorizedOn(d.Id)) ops.Add(BuildOp.Authorize(d.Id, owner));
            }
            foreach (var hurt in settlement.DamagedDeployables) ops.Add(BuildOp.DeployableHealth(hurt.Key, hurt.Value));
            // Locks, with their codes hidden: who put each on, who knows its code, and whether it is locked.
            foreach (var pair in settlement.Locks)
            {
                var l = pair.Value;
                ops.Add(BuildOp.PlaceLock(pair.Key, l.Owner));
                if (!l.HasCode) continue;
                // Setting the code leaves only its setter on the list: any one of those on it will do.
                bool first = true;
                foreach (int owner in l.AuthorizedOwners)
                {
                    ops.Add(first ? BuildOp.SetCode(pair.Key, CodeLocks.Hidden, owner) : BuildOp.LockAuthorize(pair.Key, owner));
                    first = false;
                }
                if (!l.Locked) ops.Add(BuildOp.SetLocked(pair.Key, false));
            }
            return ops;
        }

        /// <summary>The grid's own ref for a piece (an edge's ref may name a wall where a doorway stands).</summary>
        public static PieceRef Stored(BuildingGrid grid, PieceRef piece) =>
            piece.IsEdge && grid.TryGetEdgePiece(piece.Edge, out var stored) ? stored : piece;
    }
}
