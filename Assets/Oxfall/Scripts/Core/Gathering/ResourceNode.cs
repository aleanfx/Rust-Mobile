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
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>Units of the main resource taken per hit. 0 means the tool does nothing here.</summary>
        public static int PerHit(ResourceNodeType type, ToolKind tool)
        {
            switch (type)
            {
                case ResourceNodeType.Tree:
                    return tool == ToolKind.Hatchet ? 30 : tool == ToolKind.Rock ? 15 : tool == ToolKind.Pickaxe ? 10 : 0;
                case ResourceNodeType.Stone:
                    return tool == ToolKind.Pickaxe ? 40 : tool == ToolKind.Rock ? 20 : tool == ToolKind.Hatchet ? 10 : 0;
                case ResourceNodeType.MetalOre:
                    return tool == ToolKind.Pickaxe ? 24 : tool == ToolKind.Rock ? 10 : tool == ToolKind.Hatchet ? 5 : 0;
                case ResourceNodeType.Hemp:
                case ResourceNodeType.BerryBush:
                    return Capacity(type); // picked by hand in one go, any tool or none
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
        /// Hits the node once. Returns what it gave; an empty list means the tool does nothing here or
        /// the node is depleted. The last hit never gives more than what is left.
        /// </summary>
        public IReadOnlyList<Yield> Hit(ToolKind tool)
        {
            if (IsDepleted) return Array.Empty<Yield>();
            int amount = Math.Min(GatherTable.PerHit(Type, tool), Remaining);
            if (amount <= 0) return Array.Empty<Yield>();

            Remaining -= amount;
            if (IsDepleted) RespawnTimer = GatherTable.RespawnSeconds(Type);

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
