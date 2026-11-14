using System;
using System.Collections.Generic;
using Oxfall.Core.Building;

namespace Oxfall.Core.Rules
{
    /// <summary>What a code lock is on: the door in a doorway, or a box or tool cupboard by its id.</summary>
    public readonly struct LockTarget : IEquatable<LockTarget>
    {
        public readonly bool IsDoor;
        public readonly Edge Edge;
        public readonly int Id;

        private LockTarget(bool isDoor, Edge edge, int id)
        {
            IsDoor = isDoor;
            Edge = edge;
            Id = id;
        }

        public static LockTarget Door(Edge edge) => new LockTarget(true, edge, 0);
        public static LockTarget On(int deployable) => new LockTarget(false, default, deployable);

        public bool Equals(LockTarget other) => IsDoor ? other.IsDoor && Edge.Equals(other.Edge) : !other.IsDoor && Id == other.Id;
        public override bool Equals(object obj) => obj is LockTarget t && Equals(t);
        public override int GetHashCode() => IsDoor ? Edge.GetHashCode() * 2 : Id * 2 + 1;
        public override string ToString() => IsDoor ? "door " + Edge : "deployable " + Id;
    }

    /// <summary>
    /// A code lock as everyone sees it: who put it on, whether it is locked, and who has entered its
    /// code. The code itself is only known to the server (and to the game played alone): copies of
    /// the settlement on phones hold <see cref="CodeLocks.Hidden"/> instead.
    /// </summary>
    public sealed class CodeLockState
    {
        public int Owner { get; internal set; }

        /// <summary>The four digits (0 to 9999), <see cref="CodeLocks.None"/> before one is chosen, or <see cref="CodeLocks.Hidden"/>.</summary>
        public int Code { get; internal set; } = CodeLocks.None;

        public bool Locked { get; internal set; }

        internal readonly HashSet<int> Authorized = new HashSet<int>();

        public bool HasCode => Code != CodeLocks.None;
        public IReadOnlyCollection<int> AuthorizedOwners => Authorized;
        public bool IsAuthorized(int owner) => Authorized.Contains(owner);

        /// <summary>Whether this player may manage the lock (lock, unlock, change the code, take it off).</summary>
        public bool CanManage(int owner) => !HasCode ? owner == Owner : Authorized.Contains(owner);
    }

    public static class CodeLocks
    {
        public const int None = -1;

        /// <summary>A code that is set but not sent to this copy.</summary>
        public const int Hidden = -2;

        public const int MaxCode = 9999;

        /// <summary>Health lost to the shock of a wrong code, as in the PC game (and so guessing costs).</summary>
        public const float WrongCodeShock = 5f;

        public static bool IsValidCode(int code) => code >= 0 && code <= MaxCode;

        /// <summary>The code as the four digits a player types.</summary>
        public static string Digits(int code) => IsValidCode(code) ? code.ToString("0000") : "····";

        /// <summary>Whether a lock can go on this kind of thing (doors aside): boxes and tool cupboards.</summary>
        public static bool Lockable(DeployableKind kind) => kind == DeployableKind.StorageBox || kind == DeployableKind.ToolCupboard;
    }
}
