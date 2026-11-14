using System;
using System.Collections.Generic;
using Oxfall.Core.Items;

namespace Oxfall.Core.Gathering
{
    public enum ResourceNodeType
    {
        Tree,
        Stone,
        MetalOre,
        Hemp,
        BerryBush,
        /// <summary>An old oil drum: broken open with a few blows, it gives what was inside (see <see cref="LootTable"/>).</summary>
        Barrel,
        /// <summary>A boulder veined with yellow: sulfur ore, for gunpowder.</summary>
        Sulfur,
        /// <summary>A supply crate in the ruins: opened by hand, it gives what was inside (see <see cref="LootTable.Crate"/>).</summary>
        Crate,
    }

    public readonly struct Yield
    {
        public string ItemId { get; }
        public int Amount { get; }
        public Yield(string itemId, int amount) { ItemId = itemId; Amount = amount; }
        public override string ToString() => $"{ItemId} x{Amount}";
    }

    /// <summary>What one hit on a node gives, by tool. Tuned so a rock works, but a proper tool is clearly better.</summary>
    public static class GatherTable
    {
        /// <summary>Total amount a fresh node holds, in units of its main resource.</summary>
        public static int Capacity(ResourceNodeType type)
        {
            switch (type)
            {
                case ResourceNodeType.Tree: return 300;
                case ResourceNodeType.Stone: return 400;
                case ResourceNodeType.MetalOre: return 240;
                case ResourceNodeType.Hemp: return 10;
                case ResourceNodeType.BerryBush: return 4;
                case ResourceNodeType.Barrel: return 3;
                case ResourceNodeType.Sulfur: return 240;
                case ResourceNodeType.Crate: return 1;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>Seconds before a depleted node grows back.</summary>
        public static float RespawnSeconds(ResourceNodeType type)
        {
            switch (type)
            {
                case ResourceNodeType.Tree: return 300f;
                case ResourceNodeType.Stone: return 360f;
                case ResourceNodeType.MetalOre: return 480f;
                case ResourceNodeType.Hemp: return 120f;
                case ResourceNodeType.BerryBush: return 150f;
                case ResourceNodeType.Barrel: return 600f;
                case ResourceNodeType.Sulfur: return 480f;
                // Restocked every quarter of an hour: worth coming back to, not worth camping.
                case ResourceNodeType.Crate: return 900f;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>Units of the main resource taken per hit. 0 means the tool does nothing here.</summary>
        public static int PerHit(ResourceNodeType type, ToolKind tool)
        {
            switch (type)
            {
                case ResourceNodeType.Tree:
                    switch (tool)
                    {
                        case ToolKind.MetalHatchet: return 50;
                        case ToolKind.Hatchet: return 30;
                        case ToolKind.MetalPickaxe: return 15;
                        case ToolKind.Rock: return 15;
                        case ToolKind.Pickaxe: return 10;
                        default: return 0;
                    }
                case ResourceNodeType.Stone:
                    switch (tool)
                    {
                        case ToolKind.MetalPickaxe: return 65;
                        case ToolKind.Pickaxe: return 40;
                        case ToolKind.Rock: return 20;
                        case ToolKind.MetalHatchet: return 15;
                        case ToolKind.Hatchet: return 10;
                        default: return 0;
                    }
                case ResourceNodeType.MetalOre:
                case ResourceNodeType.Sulfur:
                    switch (tool)
                    {
                        case ToolKind.MetalPickaxe: return 40;
                        case ToolKind.Pickaxe: return 24;
                        case ToolKind.Rock: return 10;
                        case ToolKind.MetalHatchet: return 8;
                        case ToolKind.Hatchet: return 5;
                        default: return 0;
                    }
                case ResourceNodeType.Hemp:
                case ResourceNodeType.BerryBush:
                case ResourceNodeType.Crate:
                    return Capacity(type); // picked (or opened) by hand in one go, any tool or none
                case ResourceNodeType.Barrel:
                    // Three blows with anything hard; metal tools in two. Not with bare hands, a torch or a bow.
                    switch (tool)
                    {
                        case ToolKind.MetalHatchet:
                        case ToolKind.MetalPickaxe:
                            return 2;
                        case ToolKind.Rock:
                        case ToolKind.Hatchet:
                        case ToolKind.Pickaxe:
                        case ToolKind.Spear:
                        case ToolKind.Hammer:
                            return 1;
                        default: return 0;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static string MainItem(ResourceNodeType type)
        {
            switch (type)
            {
                case ResourceNodeType.Tree: return ItemIds.Wood;
                case ResourceNodeType.Stone: return ItemIds.Stone;
                case ResourceNodeType.MetalOre: return ItemIds.MetalOre;
                case ResourceNodeType.Hemp: return ItemIds.Cloth;
                case ResourceNodeType.BerryBush: return ItemIds.Berries;
                case ResourceNodeType.Barrel: return ItemIds.MetalFragments;
                case ResourceNodeType.Sulfur: return ItemIds.SulfurOre;
                case ResourceNodeType.Crate: return ItemIds.Scrap;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }

    /// <summary>
    /// One tree, rock or plant in the world. Holds how much is left; the game owns its position and visuals.
    /// </summary>
    public sealed class ResourceNode
    {
        public int Id { get; }
        public ResourceNodeType Type { get; }
        public int Remaining { get; private set; }
        public int Capacity { get; }
        public bool IsDepleted => Remaining <= 0;

        /// <summary>Seconds left until it grows back. Only meaningful while depleted.</summary>
        public float RespawnTimer { get; private set; }

        public float Health01 => Capacity == 0 ? 0f : (float)Remaining / Capacity;

        public ResourceNode(int id, ResourceNodeType type)
        {
            Id = id;
            Type = type;
            Capacity = GatherTable.Capacity(type);
            Remaining = Capacity;
        }

        /// <summary>
        /// Hits the node once. Returns what it gave; an empty list means the tool does nothing here, the
        /// node is depleted, or (a barrel) it is not broken open yet. The last hit never gives more than
        /// what is left. <paramref name="random"/> rolls what a barrel held.
        /// </summary>
        public IReadOnlyList<Yield> Hit(ToolKind tool, Random random = null)
        {
            if (IsDepleted) return Array.Empty<Yield>();
            int amount = Math.Min(GatherTable.PerHit(Type, tool), Remaining);
            if (amount <= 0) return Array.Empty<Yield>();

            Remaining -= amount;
            if (IsDepleted) RespawnTimer = GatherTable.RespawnSeconds(Type);
            if (Type == ResourceNodeType.Barrel)
                return IsDepleted ? LootTable.Barrel(random ?? new Random(Id * 7919 + System.Environment.TickCount)) : (IReadOnlyList<Yield>)Array.Empty<Yield>();
            if (Type == ResourceNodeType.Crate)
                return LootTable.Crate(random ?? new Random(Id * 7919 + System.Environment.TickCount));

            var result = new List<Yield>(2) { new Yield(GatherTable.MainItem(Type), amount) };
            // Metal ore comes wrapped in stone.
            if (Type == ResourceNodeType.MetalOre) result.Add(new Yield(ItemIds.Stone, amount));
            return result;
        }

        /// <summary>Advances the regrow timer. Returns true on the tick the node comes back.</summary>
        public bool TickRespawn(float deltaSeconds)
        {
            if (!IsDepleted) return false;
            RespawnTimer -= deltaSeconds;
            if (RespawnTimer > 0f) return false;
            RespawnTimer = 0f;
            Remaining = Capacity;
            return true;
        }

        /// <summary>For loading saves and server snapshots.</summary>
        public void Restore(int remaining, float respawnTimer)
        {
            Remaining = Math.Max(0, Math.Min(Capacity, remaining));
            RespawnTimer = IsDepleted ? Math.Max(0f, respawnTimer) : 0f;
        }
    }
}
