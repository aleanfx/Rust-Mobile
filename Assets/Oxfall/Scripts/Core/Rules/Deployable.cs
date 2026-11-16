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

        /// <summary>A blunderbuss on a box that fires shells at strangers in front of it (see <see cref="GunTraps"/>).</summary>
        ShotgunTrap = 10,

        /// <summary>A high external wall of sharpened logs (see <see cref="HighWalls"/>).</summary>
        WoodWall = 11,

        /// <summary>A high external wall of stone blocks.</summary>
        StoneWall = 12,

        /// <summary>A ladder leaned on a wall, to climb over it.</summary>
        Ladder = 13,

        /// <summary>A large wooden box: twice the room of a small one.</summary>
        LargeBox = 14,

        /// <summary>Hemp grown from a seed (see <see cref="Crops"/>).</summary>
        HempPlant = 15,

        /// <summary>A berry bush grown from a seed.</summary>
        BerryPlant = 16,

        /// <summary>An automatic turret: a rifle on a turning head (see <see cref="Turrets"/>).</summary>
        AutoTurret = 17,

        /// <summary>A vending machine: a shop that sells what its owner stocks (see <see cref="Vending"/>).</summary>
        VendingMachine = 18,

        /// <summary>A small generator: burns fuel for power.</summary>
        Generator = 19,

        /// <summary>A solar panel: power by day.</summary>
        SolarPanel = 20,

        /// <summary>A battery: stores power and gives it back.</summary>
        Battery = 21,

        /// <summary>A switch: lets power through when on.</summary>
        ElectricSwitch = 22,

        /// <summary>A lamp: light, for a little power.</summary>
        Lamp = 23,

        /// <summary>A presence sensor: lets power through while someone (a stranger, or anyone) is near.</summary>
        MotionSensor = 24,

        /// <summary>A siren light: spins and wails while it has power.</summary>
        SirenLight = 25,

        /// <summary>A door controller: opens the door beside it while it has power, shuts it when that stops.</summary>
        DoorController = 26,
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

        /// <summary>What mending all of a deployable's health costs (half of what it was made of), for the share it lost.</summary>
        public static System.Collections.Generic.IReadOnlyList<Crafting.Ingredient> RepairCost(DeployableKind kind, float missing)
        {
            missing = Math.Max(0f, Math.Min(1f, missing));
            string item;
            int full;
            switch (kind)
            {
                case DeployableKind.WoodWall: item = ItemIds.Wood; full = 300; break;
                case DeployableKind.StoneWall: item = ItemIds.Stone; full = 300; break;
                case DeployableKind.Ladder: item = ItemIds.Wood; full = 150; break;
                default: return Array.Empty<Crafting.Ingredient>();
            }
            return new[] { new Crafting.Ingredient(item, Math.Max(1, (int)Math.Ceiling(full * Repairs.CostShare * missing - 1e-4))) };
        }

        /// <summary>Whether the hammer mends it (a high wall or a ladder).</summary>
        public static bool Mendable(DeployableKind kind) => HighWalls.IsWall(kind) || kind == DeployableKind.Ladder;

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
                case DeployableKind.ShotgunTrap: return ItemIds.ShotgunTrap;
                case DeployableKind.WoodWall: return ItemIds.HighWoodWall;
                case DeployableKind.StoneWall: return ItemIds.HighStoneWall;
                case DeployableKind.Ladder: return ItemIds.Ladder;
                case DeployableKind.LargeBox: return ItemIds.LargeBox;
                case DeployableKind.HempPlant: return ItemIds.HempSeed;
                case DeployableKind.BerryPlant: return ItemIds.BerrySeed;
                case DeployableKind.AutoTurret: return ItemIds.AutoTurret;
                case DeployableKind.VendingMachine: return ItemIds.VendingMachine;
                case DeployableKind.Generator: return ItemIds.Generator;
                case DeployableKind.SolarPanel: return ItemIds.SolarPanel;
                case DeployableKind.Battery: return ItemIds.Battery;
                case DeployableKind.ElectricSwitch: return ItemIds.ElectricSwitch;
                case DeployableKind.Lamp: return ItemIds.Lamp;
                case DeployableKind.MotionSensor: return ItemIds.MotionSensor;
                case DeployableKind.SirenLight: return ItemIds.SirenLight;
                case DeployableKind.DoorController: return ItemIds.DoorController;
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
                case ItemIds.ShotgunTrap: kind = DeployableKind.ShotgunTrap; return true;
                case ItemIds.HighWoodWall: kind = DeployableKind.WoodWall; return true;
                case ItemIds.HighStoneWall: kind = DeployableKind.StoneWall; return true;
                case ItemIds.Ladder: kind = DeployableKind.Ladder; return true;
                case ItemIds.LargeBox: kind = DeployableKind.LargeBox; return true;
                case ItemIds.HempSeed: kind = DeployableKind.HempPlant; return true;
                case ItemIds.BerrySeed: kind = DeployableKind.BerryPlant; return true;
                case ItemIds.AutoTurret: kind = DeployableKind.AutoTurret; return true;
                case ItemIds.VendingMachine: kind = DeployableKind.VendingMachine; return true;
                case ItemIds.Generator: kind = DeployableKind.Generator; return true;
                case ItemIds.SolarPanel: kind = DeployableKind.SolarPanel; return true;
                case ItemIds.Battery: kind = DeployableKind.Battery; return true;
                case ItemIds.ElectricSwitch: kind = DeployableKind.ElectricSwitch; return true;
                case ItemIds.Lamp: kind = DeployableKind.Lamp; return true;
                case ItemIds.MotionSensor: kind = DeployableKind.MotionSensor; return true;
                case ItemIds.SirenLight: kind = DeployableKind.SirenLight; return true;
                case ItemIds.DoorController: kind = DeployableKind.DoorController; return true;
                default: kind = default; return false;
            }
        }

        /// <summary>How many slots it holds (0: it holds nothing).</summary>
        public static int Slots(DeployableKind kind)
        {
            switch (kind)
            {
                case DeployableKind.StorageBox: return 12;
                case DeployableKind.LargeBox: return 24;
                case DeployableKind.Furnace: return 6;
                // Meat to roast.
                case DeployableKind.Campfire: return 4;
                // Everything a player can carry.
                case DeployableKind.Bag: return Inventory.PlayerSlotCount;
                case DeployableKind.Recycler: return RecyclerInputSlots * 2;
                case DeployableKind.SupplyDrop: return 12;
                // Materials for its upkeep.
                case DeployableKind.ToolCupboard: return Upkeep.CupboardSlots;
                // Shells to fire.
                case DeployableKind.ShotgunTrap: return GunTraps.Slots;
                // Bullets to fire.
                case DeployableKind.AutoTurret: return Turrets.Slots;
                case DeployableKind.VendingMachine: return Vending.Slots;
                // Fuel to burn.
                case DeployableKind.Generator: return Electricity.GeneratorSlots;
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
                case DeployableKind.ShotgunTrap: return 0.45f;
                // Round a line (see HighWalls.FootprintHalfLength).
                case DeployableKind.WoodWall:
                case DeployableKind.StoneWall:
                    return HighWalls.Thickness * 0.5f;
                case DeployableKind.Ladder: return 0.12f;
                case DeployableKind.LargeBox: return 0.75f;
                case DeployableKind.AutoTurret: return 0.45f;
                case DeployableKind.VendingMachine: return 0.6f;
                case DeployableKind.Generator: return 0.5f;
                case DeployableKind.SolarPanel: return 0.65f;
                case DeployableKind.Battery: return 0.35f;
                case DeployableKind.ElectricSwitch: return 0.15f;
                case DeployableKind.Lamp: return 0.2f;
                case DeployableKind.MotionSensor: return 0.15f;
                case DeployableKind.SirenLight: return 0.18f;
                case DeployableKind.DoorController: return 0.18f;
                case DeployableKind.HempPlant:
                case DeployableKind.BerryPlant:
                    return Crops.Spacing * 0.5f;
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
                case DeployableKind.ShotgunTrap: return 200f;
                // As much as a wall of a building of the same stuff: two charges for logs, four for stone.
                case DeployableKind.WoodWall: return 250f;
                case DeployableKind.StoneWall: return 500f;
                case DeployableKind.Ladder: return 60f;
                case DeployableKind.LargeBox: return 225f;
                // Three charges.
                case DeployableKind.AutoTurret: return 400f;
                case DeployableKind.VendingMachine: return 500f;
                case DeployableKind.Generator: return 250f;
                case DeployableKind.SolarPanel: return 150f;
                case DeployableKind.Battery: return 150f;
                case DeployableKind.ElectricSwitch: return 60f;
                case DeployableKind.Lamp: return 60f;
                case DeployableKind.MotionSensor: return 60f;
                case DeployableKind.SirenLight: return 60f;
                case DeployableKind.DoorController: return 80f;
                case DeployableKind.HempPlant:
                case DeployableKind.BerryPlant:
                    return 20f;
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
                case DeployableKind.ShotgunTrap: return "Trampa de escopeta";
                case DeployableKind.WoodWall: return "Muro alto de madera";
                case DeployableKind.StoneWall: return "Muro alto de piedra";
                case DeployableKind.Ladder: return "Escalera";
                case DeployableKind.LargeBox: return "Caja grande";
                case DeployableKind.HempPlant: return "Cáñamo sembrado";
                case DeployableKind.BerryPlant: return "Moras sembradas";
                case DeployableKind.AutoTurret: return "Torreta automática";
                case DeployableKind.VendingMachine: return "Máquina expendedora";
                case DeployableKind.Generator: return "Generador";
                case DeployableKind.SolarPanel: return "Panel solar";
                case DeployableKind.Battery: return "Batería";
                case DeployableKind.ElectricSwitch: return "Interruptor";
                case DeployableKind.Lamp: return "Lámpara";
                case DeployableKind.MotionSensor: return "Sensor de presencia";
                case DeployableKind.SirenLight: return "Sirena";
                case DeployableKind.DoorController: return "Controlador de puerta";
                default: return "Objeto";
            }
        }
    }
}
