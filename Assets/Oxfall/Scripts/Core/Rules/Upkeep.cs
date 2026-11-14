using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Crafting;
using Oxfall.Core.Items;

namespace Oxfall.Core.Rules
{
    /// <summary>
    /// Keeping a base standing, as on the PC game. What is built slowly decays unless a tool cupboard
    /// within its reach keeps it up: every few minutes each cupboard pays for the pieces round it from
    /// what is stored in it (a tenth a day of what each piece's tier costs: wood for twig and wood,
    /// stone for stone, metal fragments for metal). Pieces no paying cupboard reaches lose health, twig
    /// gone within the hour, metal over eight. An abandoned base so falls by itself, and the island
    /// stays clear for those who play.
    /// </summary>
    public sealed class Upkeep
    {
        /// <summary>How often cupboards pay and unkept pieces decay, in seconds.</summary>
        public const float Interval = 600f;

        /// <summary>The share of a piece's tier cost its upkeep takes each day.</summary>
        public const float DailyShare = 0.1f;

        public const float Day = 86400f;

        /// <summary>A cupboard holds this many stacks of materials for its upkeep.</summary>
        public const int CupboardSlots = 6;

        /// <summary>What a cupboard takes in: only what pays for upkeep.</summary>
        public static bool Takes(string itemId) =>
            itemId == ItemIds.Wood || itemId == ItemIds.Stone || itemId == ItemIds.MetalFragments;

        /// <summary>How long an unkept piece of this tier takes to decay from whole to nothing, in seconds.</summary>
        public static float DecaySeconds(BuildingTier tier)
        {
            switch (tier)
            {
                case BuildingTier.Twig: return 3600f;
                case BuildingTier.Wood: return 3f * 3600f;
                case BuildingTier.Stone: return 5f * 3600f;
                case BuildingTier.Metal: return 8f * 3600f;
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
        }

        /// <summary>What keeping one piece up costs a day: one material, and how much of it.</summary>
        public static Ingredient PerDay(PieceType type, BuildingTier tier, out float amount)
        {
            var cost = tier == BuildingTier.Twig ? BuildCosts.For(type)[0] : BuildCosts.Upgrade(type, tier)[0];
            amount = cost.Amount * DailyShare;
            return cost;
        }

        /// <summary>The cupboard that keeps a piece up, if any: the nearest one whose reach takes it in.</summary>
        public static bool KeeperOf(Settlement settlement, PieceRef piece, out Deployable cupboard)
        {
            BuildingGrid.Centre(piece, out float x, out float z);
            return settlement.TryGetCupboardAt(x, z, out cupboard);
        }

        /// <summary>What the pieces a cupboard keeps up cost a day, by material (cleared first), and how many pieces.</summary>
        public static int DailyCost(Settlement settlement, int cupboard, Dictionary<string, float> into)
        {
            into.Clear();
            int pieces = 0;
            var grid = settlement.Grid;
            foreach (var piece in grid.Pieces)
            {
                if (!KeeperOf(settlement, piece, out var keeper) || keeper.Id != cupboard) continue;
                if (!grid.TryGetTier(piece, out var tier)) continue;
                var cost = PerDay(piece.Type, tier, out float amount);
                into.TryGetValue(cost.ItemId, out float sum);
                into[cost.ItemId] = sum + amount;
                pieces++;
            }
            return pieces;
        }

        /// <summary>
        /// How long what is stored in a cupboard keeps its pieces up, in seconds: the material that
        /// runs out first decides. Infinity when it keeps nothing up.
        /// </summary>
        public static float SecondsCovered(Settlement settlement, int cupboard, Dictionary<string, float> cost)
        {
            var box = settlement.Container(cupboard);
            float covered = float.PositiveInfinity;
            foreach (var pair in cost)
            {
                if (pair.Value <= 0f) continue;
                float stored = box?.Count(pair.Key) ?? 0;
                covered = Math.Min(covered, stored / pair.Value * Day);
            }
            return covered;
        }

        // ---- Over time

        private float _due;
        private readonly Dictionary<int, Dictionary<string, float>> _owed = new Dictionary<int, Dictionary<string, float>>();
        private readonly Dictionary<int, Dictionary<string, float>> _costs = new Dictionary<int, Dictionary<string, float>>();
        private readonly HashSet<int> _unpaid = new HashSet<int>();
        private readonly List<PieceRef> _decaying = new List<PieceRef>();
        private readonly List<int> _gone = new List<int>();
        private readonly Dictionary<PieceRef, int> _keepers = new Dictionary<PieceRef, int>();

        /// <summary>Seconds until the next round of paying and decay.</summary>
        public float NextIn => Math.Max(0f, Interval - _due);

        /// <summary>Whether a cupboard failed to pay at the last round (its pieces are decaying).</summary>
        public bool IsUnpaid(int cupboard) => _unpaid.Contains(cupboard);

        /// <summary>Brings the next round forward to now (for tests and the debug console).</summary>
        public void Hurry() => _due = Interval;

        /// <summary>
        /// Time passing: every <see cref="Interval"/> each cupboard pays for its pieces, and the pieces
        /// no paying cupboard keeps up lose health (their new health listed in <paramref name="decayed"/>,
        /// at 0 when they fell).
        /// </summary>
        public void Tick(Settlement settlement, float dt, List<BuildOp> decayed = null)
        {
            _due += dt;
            if (_due < Interval) return;
            float span = Math.Min(_due, Interval);
            _due = 0f;
            Round(settlement, span, decayed);
        }

        private void Round(Settlement settlement, float span, List<BuildOp> decayed)
        {
            var grid = settlement.Grid;
            // Who keeps what up, and what that costs each cupboard.
            foreach (var cost in _costs.Values) cost.Clear();
            _keepers.Clear();
            foreach (var piece in grid.Pieces)
            {
                if (!KeeperOf(settlement, piece, out var keeper) || !grid.TryGetTier(piece, out var tier)) continue;
                _keepers[piece] = keeper.Id;
                if (!_costs.TryGetValue(keeper.Id, out var cost)) _costs[keeper.Id] = cost = new Dictionary<string, float>();
                var material = PerDay(piece.Type, tier, out float amount);
                cost.TryGetValue(material.ItemId, out float sum);
                cost[material.ItemId] = sum + amount;
            }
            // Each cupboard pays its share for the time gone by, in whole pieces (the fractions are
            // owed until they add up), all of it or nothing.
            _unpaid.Clear();
            foreach (var pair in _costs)
            {
                if (pair.Value.Count == 0) continue;
                var box = settlement.Container(pair.Key);
                if (!_owed.TryGetValue(pair.Key, out var owed)) _owed[pair.Key] = owed = new Dictionary<string, float>();
                bool pays = box != null;
                foreach (var cost in pair.Value)
                {
                    owed.TryGetValue(cost.Key, out float o);
                    o += cost.Value * span / Day;
                    owed[cost.Key] = o;
                    // It must hold some of every material it owes, and enough for what has added up.
                    if (box == null || !box.Has(cost.Key, Math.Max(1, (int)Math.Ceiling(o)))) pays = false;
                }
                if (!pays)
                {
                    _unpaid.Add(pair.Key);
                    // What it could not pay is not kept owing (the pieces paid in health instead).
                    owed.Clear();
                    continue;
                }
                foreach (var cost in pair.Value)
                {
                    float o = owed[cost.Key];
                    int whole = (int)o;
                    if (whole > 0) box.Remove(cost.Key, whole);
                    owed[cost.Key] = o - whole;
                }
            }
            // Cupboards that are gone owe nothing.
            _gone.Clear();
            foreach (int id in _costs.Keys)
                if (!settlement.TryGet(id, out _)) _gone.Add(id);
            foreach (int id in _gone)
            {
                _owed.Remove(id);
                _costs.Remove(id);
            }
            // The pieces nobody keeps up decay.
            _decaying.Clear();
            foreach (var piece in grid.Pieces)
                if (!_keepers.TryGetValue(piece, out int keeper) || _unpaid.Contains(keeper))
                    _decaying.Add(piece);
            foreach (var piece in _decaying)
            {
                // A piece below may have taken this one down with it.
                if (!grid.Exists(piece) || !grid.TryGetTier(piece, out var tier)) continue;
                float max = BuildingTiers.MaxHealth(tier);
                float left = grid.Health(piece) - max * span / DecaySeconds(tier);
                // What is left of a rounding is nothing.
                if (left < max * 0.001f) left = 0f;
                grid.SetHealth(piece, left);
                decayed?.Add(BuildOp.Health(piece, Math.Max(0f, left)));
            }
        }

        /// <summary>Forgets everything owed (the island was cleared).</summary>
        public void Clear()
        {
            _due = 0f;
            _owed.Clear();
            _costs.Clear();
            _unpaid.Clear();
        }
    }
}
