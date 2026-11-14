using System;
using Oxfall.Core.Items;

namespace Oxfall.Core.Rules
{
    /// <summary>Things a player sets down on the ground or a floor. Sent and saved as numbers: only ever add at the end.</summary>
    public enum DeployableKind : byte
    {
        Campfire = 1,
        StorageBox = 2,
        Furnace = 3,
        SleepingBag = 4,
        ToolCupboard = 5,

        /// <summary>What a player had on them when they died, left where they fell (no item makes one).</summary>
        Bag = 6,

        /// <summary>Near it, guns, metal tools and explosives can be made.</summary>
        Workbench = 7,

        /// <summary>
        /// A machine in the ruins (no item makes one, nobody owns it, nothing breaks it): what goes
        /// into its first slots comes out of its last ones as scrap and what it was made of.
        /// </summary>
        Recycler = 8,

        /// <summary>
        /// A crate of supplies dropped from a plane (no item makes one, nobody owns it, nothing breaks
        /// it): it only empties, and rots away a while after landing.
        /// </summary>
        SupplyDrop = 9,
    }

    /// <summary>One thing set down: what it is, where it stands, which way it faces, and who put it there.</summary>
    public readonly struct Deployable
    {
        public readonly int Id;
        public readonly DeployableKind Kind;
        public readonly float X, Y, Z, Yaw;

        /// <summary>Who set it down (0: nobody in particular, as in a game played alone).</summary>
        public readonly int Owner;

        public Deployable(int id, DeployableKind kind, float x, float y, float z, float yaw, int owner = 0)
        {
            Id = id;
            Kind = kind;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Owner = owner;
        }

        public Deployable WithId(int id) => new Deployable(id, Kind, X, Y, Z, Yaw, Owner);
        public Deployable WithOwner(int owner) => new Deployable(Id, Kind, X, Y, Z, Yaw, owner);

        public override string ToString() => $"{Kind} {Id} ({X:0.0}, {Y:0.0}, {Z:0.0})";
    }

    /// <summary>What each kind of deployable is made from, holds and takes up.</summary>
    public static class Deployables
    {
        /// <summary>How far round a tool cupboard (across the ground) only those authorized on it may build.</summary>
        public const float PrivilegeRadius = 24f;

        /// <summary>How long after waking up in a sleeping bag it can be used again.</summary>
        public const float BagCooldown = 120f;

        /// <summary>How long a dead player's bag lies there before it rots away (sooner once emptied).</summary>
        public const float DeathBagSeconds = 600f;

        /// <summary>How near a workbench (in metres, from its middle) its recipes can be made.</summary>
        public const float WorkbenchReach = 4f;

        /// <summary>A recycler's slots: this many take things in, as many again give out what they are made into.</summary>
        public const int RecyclerInputSlots = 3;

        /// <summary>How long a supply crate lies there before it rots away (sooner once emptied).</summary>
        public const float SupplyDropSeconds = 1200f;

        /// <summary>Whether it only empties and goes once empty (a dead player's bag, a supply crate).</summary>
        public static bool IsLoot(DeployableKind kind) => kind == DeployableKind.Bag || kind == DeployableKind.SupplyDrop;

        public static bool IsKnown(DeployableKind kind) => Enum.IsDefined(typeof(DeployableKind), kind);

        /// <summary>The item that becomes this deployable when set down (and comes back when it is picked up).</summary>
        public static string ItemOf(DeployableKind kind)
        {
            switch (kind)
            {
                case DeployableKind.Campfire: return ItemIds.Campfire;
                case DeployableKind.StorageBox: return ItemIds.StorageBox;
                case DeployableKind.Furnace: return ItemIds.Furnace;
                case DeployableKind.SleepingBag: return ItemIds.SleepingBag;
                case DeployableKind.ToolCupboard: return ItemIds.ToolCupboard;
                case DeployableKind.Workbench: return ItemIds.Workbench;
                // Nothing is made into a bag, a recycler or a supply crate, and none is picked up.
                case DeployableKind.Bag:
                case DeployableKind.Recycler:
                case DeployableKind.SupplyDrop:
                    return null;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>The deployable an item in hand sets down, if it is one.</summary>
        public static bool TryKindOf(string itemId, out DeployableKind kind)
        {
            switch (itemId)
            {
                case ItemIds.Campfire: kind = DeployableKind.Campfire; return true;
                case ItemIds.StorageBox: kind = DeployableKind.StorageBox; return true;
                case ItemIds.Furnace: kind = DeployableKind.Furnace; return true;
                case ItemIds.SleepingBag: kind = DeployableKind.SleepingBag; return true;
                case ItemIds.ToolCupboard: kind = DeployableKind.ToolCupboard; return true;
                case ItemIds.Workbench: kind = DeployableKind.Workbench; return true;
                default: kind = default; return false;
            }
        }

        /// <summary>How many slots it holds (0: it holds nothing).</summary>
        public static int Slots(DeployableKind kind)
        {
            switch (kind)
            {
                case DeployableKind.StorageBox: return 12;
                case DeployableKind.Furnace: return 6;
                // Meat to roast.
                case DeployableKind.Campfire: return 4;
                // Everything a player can carry.
                case DeployableKind.Bag: return Inventory.PlayerSlotCount;
                case DeployableKind.Recycler: return RecyclerInputSlots * 2;
                case DeployableKind.SupplyDrop: return 12;
                // Materials for its upkeep.
                case DeployableKind.ToolCupboard: return Upkeep.CupboardSlots;
                default: return 0;
            }
        }

        /// <summary>How much room it takes up round its middle, in metres; two never overlap.</summary>
        public static float Radius(DeployableKind kind)
        {
            switch (kind)
            {
                case DeployableKind.Campfire: return 0.5f;
                case DeployableKind.StorageBox: return 0.45f;
                case DeployableKind.Furnace: return 0.6f;
                case DeployableKind.SleepingBag: return 0.55f;
                case DeployableKind.ToolCupboard: return 0.4f;
                case DeployableKind.Bag: return 0.35f;
                case DeployableKind.Workbench: return 0.8f;
                case DeployableKind.Recycler: return 1.1f;
                case DeployableKind.SupplyDrop: return 0.8f;
                default: return 0.5f;
            }
        }

        /// <summary>
        /// How much a blast must take off it before it is destroyed: a sleeping bag goes with the first
        /// charge next to it, a box or a cupboard with two, a furnace with four. A dead player's bag
        /// does not burn (what it holds would be lost).
        /// </summary>
        public static float MaxHealth(DeployableKind kind)
        {
            switch (kind)
            {
                case DeployableKind.Campfire: return 100f;
                case DeployableKind.StorageBox: return 150f;
                case DeployableKind.Furnace: return 300f;
                case DeployableKind.SleepingBag: return 50f;
                case DeployableKind.ToolCupboard: return 150f;
                case DeployableKind.Workbench: return 300f;
                default: return 0f;
            }
        }

        public static string Name(DeployableKind kind)
        {
            switch (kind)
            {
                case DeployableKind.Campfire: return "Fogata";
                case DeployableKind.StorageBox: return "Caja";
                case DeployableKind.Furnace: return "Horno";
                case DeployableKind.SleepingBag: return "Saco de dormir";
                case DeployableKind.ToolCupboard: return "Armario";
                case DeployableKind.Workbench: return "Mesa de trabajo";
                case DeployableKind.Recycler: return "Recicladora";
                case DeployableKind.SupplyDrop: return "Suministros";
                default: return "Objeto";
            }
        }
    }
}
