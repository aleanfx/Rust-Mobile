using System;
using System.Collections.Generic;
using Oxfall.Core.Items;

namespace Oxfall.Core.Crafting
{
    /// <summary>
    /// Crafts one job at a time. Ingredients are taken when a job is queued and refunded if it is
    /// cancelled, so nothing can be spent twice. Time is driven by <see cref="Tick"/>, which keeps
    /// the queue deterministic for the server and for tests.
    /// </summary>
    public sealed class CraftingQueue
    {
        public const int MaxJobs = 8;

        public sealed class Job
        {
            public Recipe Recipe { get; }
            public float Elapsed { get; internal set; }
            public float Progress => Recipe.CraftSeconds <= 0 ? 1f : Math.Min(1f, Elapsed / Recipe.CraftSeconds);
            internal Job(Recipe recipe) { Recipe = recipe; }
        }

        public enum EnqueueResult { Queued, MissingIngredients, QueueFull }

        private readonly Inventory _inventory;
        private readonly List<Job> _jobs = new List<Job>();

        /// <summary>Raised when a job finishes. The int is how many items did not fit and were lost to the overflow handler.</summary>
        public event Action<Recipe, int> Completed;

        /// <summary>Called with items that finished but did not fit. The game decides whether to drop them on the ground.</summary>
        public Action<string, int> Overflow { get; set; }

        public IReadOnlyList<Job> Jobs => _jobs;
        public bool IsBusy => _jobs.Count > 0;

        public CraftingQueue(Inventory inventory)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public EnqueueResult Enqueue(Recipe recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (_jobs.Count >= MaxJobs) return EnqueueResult.QueueFull;
            if (!recipe.CanAfford(_inventory)) return EnqueueResult.MissingIngredients;
            foreach (var ing in recipe.Ingredients)
            {
                if (!_inventory.Remove(ing.ItemId, ing.Amount))
                    throw new InvalidOperationException("Ingredients vanished between the check and the removal.");
            }
            _jobs.Add(new Job(recipe));
            return EnqueueResult.Queued;
        }

        /// <summary>Cancels a job and gives its ingredients back. Returns false if the index is not a job.</summary>
        public bool Cancel(int index)
        {
            if (index < 0 || index >= _jobs.Count) return false;
            var job = _jobs[index];
            _jobs.RemoveAt(index);
            foreach (var ing in job.Recipe.Ingredients)
            {
                int lost = _inventory.Add(ing.ItemId, ing.Amount);
                if (lost > 0) Overflow?.Invoke(ing.ItemId, lost);
            }
            return true;
        }

        /// <summary>Advances the front job. Several short jobs can finish in one long tick.</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            float time = deltaSeconds;
            while (_jobs.Count > 0)
            {
                var job = _jobs[0];
                float needed = job.Recipe.CraftSeconds - job.Elapsed;
                if (time < needed)
                {
                    job.Elapsed += time;
                    return;
                }
                time -= Math.Max(0f, needed);
                job.Elapsed = job.Recipe.CraftSeconds;
                _jobs.RemoveAt(0);
                int lost = _inventory.Add(job.Recipe.OutputItemId, job.Recipe.OutputAmount);
                if (lost > 0) Overflow?.Invoke(job.Recipe.OutputItemId, lost);
                Completed?.Invoke(job.Recipe, lost);
            }
        }
    }
}
