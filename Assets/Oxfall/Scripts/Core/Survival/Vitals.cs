using System;

namespace Oxfall.Core.Survival
{
    /// <summary>What eating, drinking or a bandage gives back: health at once, and some more over the next seconds.</summary>
    public readonly struct Nourishment
    {
        public float Food { get; }
        public float Water { get; }
        public float Health { get; }

        /// <summary>Health that comes back over the next seconds (<see cref="Vitals.RegenPerSecond"/>).</summary>
        public float Regen { get; }

        public Nourishment(float food, float water, float health = 0f, float regen = 0f)
        {
            Food = food;
            Water = water;
            Health = health;
            Regen = regen;
        }
    }

    /// <summary>
    /// Health, food and water. Food and water run down over time (faster when running); with
    /// either empty, health drains; with both comfortably full, health slowly comes back. All the
    /// numbers live here so the server can run the same rules for every player.
    /// </summary>
    public sealed class Vitals
    {
        public const float MaxHealth = 100f;
        public const float MaxFood = 500f;
        public const float MaxWater = 250f;

        /// <summary>Per real second at rest: a full stomach lasts about an hour, full water about 40 minutes.</summary>
        public const float FoodPerSecond = MaxFood / 3600f;
        public const float WaterPerSecond = MaxWater / 2400f;

        /// <summary>Health lost per second for each of food and water that is empty.</summary>
        public const float StarvePerSecond = 0.5f;

        /// <summary>Health regained per second while food and water are both above a quarter.</summary>
        public const float HealPerSecond = 0.2f;

        /// <summary>How fast a bandage's or syringe's lasting healing comes back, health per second.</summary>
        public const float RegenPerSecond = 4f;

        /// <summary>The most lasting healing that can be waiting at once.</summary>
        public const float MaxRegen = 60f;

        public float Health { get; private set; }
        public float Food { get; private set; }
        public float Water { get; private set; }

        public bool IsDead => Health <= 0f;

        /// <summary>Health still to come back from a bandage or syringe.</summary>
        public float Regen { get; private set; }

        /// <summary>Raised once, the moment health reaches zero.</summary>
        public event Action Died;

        public Vitals(float health = MaxHealth, float food = 300f, float water = 200f)
        {
            Health = Clamp(health, MaxHealth);
            Food = Clamp(food, MaxFood);
            Water = Clamp(water, MaxWater);
        }

        public float Health01 => Health / MaxHealth;
        public float Food01 => Food / MaxFood;
        public float Water01 => Water / MaxWater;

        /// <param name="seconds">Real time passed.</param>
        /// <param name="exertion">1 at rest or walking; more while sprinting (food and water go faster).</param>
        public void Tick(float seconds, float exertion = 1f)
        {
            if (seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (IsDead || seconds == 0f) return;
            exertion = Math.Max(1f, exertion);
            Food = Clamp(Food - FoodPerSecond * exertion * seconds, MaxFood);
            Water = Clamp(Water - WaterPerSecond * exertion * seconds, MaxWater);

            float change = 0f;
            if (Food <= 0f) change -= StarvePerSecond;
            if (Water <= 0f) change -= StarvePerSecond;
            if (change == 0f && Food > MaxFood * 0.25f && Water > MaxWater * 0.25f && Health < MaxHealth)
                change = HealPerSecond;
            if (Regen > 0f)
            {
                float back = Math.Min(Regen, RegenPerSecond * seconds);
                Regen -= back;
                change += back / seconds;
            }
            if (change != 0f) SetHealth(Health + change * seconds);
        }

        /// <summary>Eats or drinks. Nothing happens once dead.</summary>
        public void Consume(Nourishment n)
        {
            if (IsDead) return;
            Food = Clamp(Food + n.Food, MaxFood);
            Water = Clamp(Water + n.Water, MaxWater);
            if (n.Health != 0f) SetHealth(Health + n.Health);
            if (n.Regen > 0f && !IsDead) Regen = Math.Min(MaxRegen, Regen + n.Regen);
        }

        /// <summary>Takes damage (a fall, the cold, another player). Returns true if this killed.</summary>
        public bool Damage(float amount)
        {
            if (amount < 0f) throw new ArgumentOutOfRangeException(nameof(amount));
            if (IsDead) return false;
            SetHealth(Health - amount);
            return IsDead;
        }

        /// <summary>Takes the values as they are somewhere else (the server). Dying here is announced as usual.</summary>
        public void Set(float health, float food, float water)
        {
            Food = Clamp(food, MaxFood);
            Water = Clamp(water, MaxWater);
            SetHealth(health);
        }

        /// <summary>Back to life, as a fresh player wakes up on the beach.</summary>
        public void Respawn(float health = 60f, float food = 200f, float water = 150f)
        {
            Regen = 0f;
            Health = Clamp(health, MaxHealth);
            Food = Clamp(food, MaxFood);
            Water = Clamp(water, MaxWater);
        }

        private void SetHealth(float value)
        {
            bool wasAlive = !IsDead;
            Health = Clamp(value, MaxHealth);
            if (IsDead) Regen = 0f;
            if (wasAlive && IsDead) Died?.Invoke();
        }

        private static float Clamp(float value, float max) => value < 0f ? 0f : value > max ? max : value;
    }
}
