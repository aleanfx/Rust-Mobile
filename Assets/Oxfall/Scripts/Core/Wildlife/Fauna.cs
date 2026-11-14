using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.World;

namespace Oxfall.Core.Wildlife
{
    /// <summary>
    /// The island's animals and what they do, as plain rules: the server runs them for everyone, a
    /// game played alone runs the same ones for its one player. Boars root about in the woods and
    /// charge whoever hurts them; wolves come for anyone who gets close; deer graze in the open and
    /// bolt. A dead one leaves a carcass to cut up for meat and leather until it is used up or rots,
    /// and later another of its kind turns up somewhere out of sight.
    /// </summary>
    public sealed class Fauna
    {
        /// <summary>A player as the animals see them: who (any number that tells players apart), and where.</summary>
        public readonly struct Prey
        {
            public readonly int Key;
            public readonly float X, Y, Z;

            public Prey(int key, float x, float y, float z)
            {
                Key = key;
                X = x;
                Y = y;
                Z = z;
            }
        }

        /// <summary>An animal's attack that landed this tick.</summary>
        public readonly struct Bite
        {
            public readonly int Animal;
            public readonly int Target;
            public readonly float Damage;

            public Bite(int animal, int target, float damage)
            {
                Animal = animal;
                Target = target;
                Damage = damage;
            }
        }

        /// <summary>Animals keep this far from where players are when they turn up.</summary>
        public const float SpawnClearance = 55f;

        private readonly IslandGround _ground;
        private readonly Random _random;
        private readonly Dictionary<int, Animal> _animals = new Dictionary<int, Animal>();
        private readonly List<(AnimalKind Kind, float Timer)> _waiting = new List<(AnimalKind, float)>();
        private readonly List<int> _gone = new List<int>();
        private int _nextId = 1;

        public Fauna(IslandGround ground, int seed)
        {
            _ground = ground ?? throw new ArgumentNullException(nameof(ground));
            _random = new Random(seed ^ 0x2b0a7);
        }

        /// <summary>Spots animals will not walk onto (a building's floor): set by whoever owns the buildings.</summary>
        public Func<float, float, bool> Blocked { get; set; }

        public IEnumerable<Animal> All => _animals.Values;
        public int Count => _animals.Count;

        public bool TryGet(int id, out Animal animal) => _animals.TryGetValue(id, out animal);

        /// <summary>No animals at all, and none to come (for tests).</summary>
        public void Clear()
        {
            _animals.Clear();
            _waiting.Clear();
        }

        /// <summary>How many of each roam the island (it is about 350 m across).</summary>
        public const int Boars = 7, Wolves = 4, Deer = 7;

        /// <summary>The herds a fresh island starts with.</summary>
        public void Populate() => Populate(Boars, Wolves, Deer);

        /// <summary>
        /// What animals walk round: trunks and boulders, and any building's floor (with a little room
        /// so a wolf does not push its head through a wall).
        /// </summary>
        public static Func<float, float, bool> Barriers(BuildingGrid grid, Obstacles obstacles) => (x, z) =>
        {
            if (obstacles != null && obstacles.Blocks(x, z, 0.3f)) return true;
            if (grid == null || grid.FoundationCount == 0) return false;
            const float m = 0.7f;
            return grid.HasFoundation(BuildingGrid.CellAt(x, z))
                || grid.HasFoundation(BuildingGrid.CellAt(x + m, z)) || grid.HasFoundation(BuildingGrid.CellAt(x - m, z))
                || grid.HasFoundation(BuildingGrid.CellAt(x, z + m)) || grid.HasFoundation(BuildingGrid.CellAt(x, z - m));
        };

        /// <summary>The herds a fresh island starts with.</summary>
        public void Populate(int boars, int wolves, int deer)
        {
            for (int i = 0; i < boars; i++) Spawn(AnimalKind.Boar, null);
            for (int i = 0; i < wolves; i++) Spawn(AnimalKind.Wolf, null);
            for (int i = 0; i < deer; i++) Spawn(AnimalKind.Deer, null);
        }

        /// <summary>Puts an animal of this kind somewhere it likes, out of sight of the players. False if nowhere fit.</summary>
        public bool Spawn(AnimalKind kind, IReadOnlyList<Prey> players)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                double angle = _random.NextDouble() * Math.PI * 2.0;
                float r = (float)Math.Sqrt(_random.NextDouble()) * IslandGround.IslandRadius * 0.92f;
                float x = (float)Math.Cos(angle) * r, z = (float)Math.Sin(angle) * r;
                if (!Walkable(x, z) || _ground.Flatness(x, z) < 0.8f) continue;
                float forest = _ground.Forest(x, z);
                // Boars and wolves keep to the woods, deer to the open (looser after many tries).
                float slack = attempt / 160f;
                if (kind == AnimalKind.Deer ? forest > 0.5f + slack : forest < 0.45f - slack) continue;
                if (players != null && Near(players, x, z, SpawnClearance)) continue;
                Add(kind, x, z, (float)_random.NextDouble() * 360f);
                return true;
            }
            return false;
        }

        /// <summary>Sets an animal down at a spot (for tests and spawning).</summary>
        public Animal Add(AnimalKind kind, float x, float z, float yaw)
        {
            var a = new Animal
            {
                Id = _nextId++,
                Kind = kind,
                X = x,
                Z = z,
                Y = _ground.Height(x, z),
                Yaw = yaw,
                Health = Animals.MaxHealth(kind),
                State = AnimalState.Idle,
                HomeX = x,
                HomeZ = z,
                Timer = 1f + (float)_random.NextDouble() * 4f,
            };
            _animals.Add(a.Id, a);
            return a;
        }

        // ---- Being hit

        /// <summary>
        /// A blow on a living animal from <paramref name="attacker"/> standing at (fromX, fromZ).
        /// Returns true if it killed it. A wolf or a boar turns on the attacker; a deer runs.
        /// </summary>
        public bool Hit(int id, float damage, int attacker, float fromX, float fromZ)
        {
            if (!_animals.TryGetValue(id, out var a) || a.IsDead || damage <= 0f) return false;
            a.Health -= damage;
            if (a.Health <= 0f)
            {
                a.Health = 0f;
                a.State = AnimalState.Dead;
                a.Speed = 0f;
                a.Carcass = Animals.CarcassHits(a.Kind);
                a.Timer = Animals.CarcassSeconds;
                return true;
            }
            if (a.Kind == AnimalKind.Deer) Flee(a, fromX, fromZ, 6f);
            else
            {
                a.Angry = true;
                Chase(a, attacker);
            }
            return false;
        }

        /// <summary>A blow on a carcass with a tool: what it gives (nothing for a living animal, a used-up carcass or a poor tool).</summary>
        public (string ItemId, int Amount)[] Harvest(int id, ToolKind tool)
        {
            if (!_animals.TryGetValue(id, out var a) || !a.IsDead || a.Carcass <= 0) return Array.Empty<(string, int)>();
            var yields = Animals.Harvest(a.Kind, tool);
            if (yields.Length == 0) return yields;
            a.Carcass--;
            // Used up: gone on the next tick.
            if (a.Carcass == 0) a.Timer = 0f;
            return yields;
        }

        // ---- Time passing

        /// <summary>
        /// Moves every animal on by <paramref name="dt"/> seconds given where the players are (living
        /// ones only), adding the attacks that land to <paramref name="bites"/>. Carcasses rot and
        /// new animals turn up.
        /// </summary>
        public void Tick(float dt, IReadOnlyList<Prey> players, List<Bite> bites)
        {
            if (dt <= 0f) return;
            _gone.Clear();
            foreach (var a in _animals.Values)
            {
                if (a.IsDead)
                {
                    a.Timer -= dt;
                    if (a.Timer <= 0f) _gone.Add(a.Id);
                    continue;
                }
                Think(a, dt, players, bites);
                Move(a, dt, players);
            }
            foreach (int id in _gone)
            {
                _waiting.Add((_animals[id].Kind, Animals.RespawnSeconds));
                _animals.Remove(id);
            }
            for (int i = _waiting.Count - 1; i >= 0; i--)
            {
                var (kind, timer) = _waiting[i];
                timer -= dt;
                if (timer > 0f)
                {
                    _waiting[i] = (kind, timer);
                    continue;
                }
                // Nowhere out of sight just now: try again in a while.
                if (Spawn(kind, players)) _waiting.RemoveAt(i);
                else _waiting[i] = (kind, 20f);
            }
        }

        private void Think(Animal a, float dt, IReadOnlyList<Prey> players, List<Bite> bites)
        {
            a.Timer -= dt;
            a.AttackTimer -= dt;
            int nearest = NearestPrey(players, a.X, a.Z, out float distance);

            switch (a.State)
            {
                case AnimalState.Chase:
                {
                    int q = IndexOf(players, a.Quarry);
                    float fromHome = Distance(a.X, a.Z, a.HomeX, a.HomeZ);
                    if (q < 0 || fromHome > Animals.ChaseLeash || Distance(a.X, a.Z, players[q].X, players[q].Z) > Animals.WolfSight * 1.6f)
                    {
                        a.Angry = false;
                        Rest(a);
                        break;
                    }
                    var prey = players[q];
                    a.GoalX = prey.X;
                    a.GoalZ = prey.Z;
                    float d = Distance(a.X, a.Z, prey.X, prey.Z);
                    if (d <= Animals.AttackReach && Math.Abs(prey.Y - a.Y) < 2f && a.AttackTimer <= 0f)
                    {
                        a.AttackTimer = Animals.AttackSeconds;
                        bites?.Add(new Bite(a.Id, prey.Key, Animals.Damage(a.Kind)));
                    }
                    break;
                }
                case AnimalState.Flee:
                    if (a.Timer <= 0f) Rest(a);
                    break;
                default:
                    // Noticing players.
                    if (nearest >= 0)
                    {
                        var p = players[nearest];
                        if (a.Kind == AnimalKind.Wolf && distance < Animals.WolfSight)
                        {
                            Chase(a, p.Key);
                            break;
                        }
                        if (a.Kind == AnimalKind.Deer && distance < Animals.Shyness)
                        {
                            Flee(a, p.X, p.Z, 5f);
                            break;
                        }
                        if (a.Kind == AnimalKind.Boar && distance < Animals.Shyness * 0.45f)
                        {
                            Flee(a, p.X, p.Z, 2.5f);
                            break;
                        }
                    }
                    if (a.Timer > 0f) break;
                    if (a.State == AnimalState.Idle) Wander(a);
                    else Rest(a);
                    break;
            }
        }

        private void Move(Animal a, float dt, IReadOnlyList<Prey> players)
        {
            float speed = a.State == AnimalState.Wander ? Animals.WalkSpeed
                : a.State == AnimalState.Chase || a.State == AnimalState.Flee ? Animals.RunSpeed(a.Kind) : 0f;
            float dx = a.GoalX - a.X, dz = a.GoalZ - a.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            // A chaser stops at biting range; a walker at its spot.
            float stop = a.State == AnimalState.Chase ? Animals.AttackReach * 0.8f : 0.3f;
            if (speed <= 0f || d <= stop)
            {
                a.Speed = 0f;
                if (a.State == AnimalState.Chase && d > 0.01f) Turn(a, dx, dz, dt);
                else if (a.State == AnimalState.Wander) Rest(a);
                return;
            }
            Turn(a, dx, dz, dt);
            float step = Math.Min(speed * dt, d - stop);
            float yaw = a.Yaw * (float)(Math.PI / 180.0);
            // Moves the way it faces, so it curves round instead of sliding.
            float nx = a.X + (float)Math.Sin(yaw) * step, nz = a.Z + (float)Math.Cos(yaw) * step;
            if (!Walkable(nx, nz))
            {
                // Something in the way (a trunk, a rock, a wall): round it to one side or the other.
                bool round = false;
                foreach (float turn in Detours)
                {
                    float y2 = yaw + turn * (float)(Math.PI / 180.0);
                    float tx = a.X + (float)Math.Sin(y2) * step, tz = a.Z + (float)Math.Cos(y2) * step;
                    if (!Walkable(tx, tz)) continue;
                    nx = tx;
                    nz = tz;
                    a.Yaw = (a.Yaw + turn * 0.5f + 360f) % 360f;
                    round = true;
                    break;
                }
                if (!round)
                {
                    a.Speed = 0f;
                    // Turned back by water or a wall: a new way.
                    if (a.State == AnimalState.Chase) return;
                    Wander(a);
                    return;
                }
            }
            a.X = nx;
            a.Z = nz;
            a.Y = _ground.Height(nx, nz);
            a.Speed = step / dt;
        }

        private static readonly float[] Detours = { 45f, -45f, 90f, -90f };

        private static void Turn(Animal a, float dx, float dz, float dt)
        {
            float want = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
            float delta = ((want - a.Yaw) % 360f + 540f) % 360f - 180f;
            float max = 300f * dt;
            a.Yaw = (a.Yaw + Math.Max(-max, Math.Min(max, delta)) + 360f) % 360f;
        }

        private void Wander(Animal a)
        {
            for (int i = 0; i < 8; i++)
            {
                double angle = _random.NextDouble() * Math.PI * 2.0;
                float r = 4f + (float)_random.NextDouble() * 14f;
                float x = a.HomeX + (float)Math.Cos(angle) * r, z = a.HomeZ + (float)Math.Sin(angle) * r;
                if (!Walkable(x, z)) continue;
                a.GoalX = x;
                a.GoalZ = z;
                a.State = AnimalState.Wander;
                a.Timer = 25f;
                return;
            }
            Rest(a);
        }

        private void Rest(Animal a)
        {
            a.State = AnimalState.Idle;
            a.Timer = 2f + (float)_random.NextDouble() * 6f;
            a.Speed = 0f;
            // Wherever it ends up is home now.
            a.HomeX = a.X;
            a.HomeZ = a.Z;
        }

        private static void Chase(Animal a, int quarry)
        {
            if (a.State != AnimalState.Chase)
            {
                a.HomeX = a.X;
                a.HomeZ = a.Z;
            }
            a.State = AnimalState.Chase;
            a.Quarry = quarry;
        }

        private void Flee(Animal a, float fromX, float fromZ, float seconds)
        {
            float dx = a.X - fromX, dz = a.Z - fromZ;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d < 0.01f)
            {
                dx = 1f;
                d = 1f;
            }
            // Away, a little to one side or the other, as far as it can run in that time.
            float side = ((float)_random.NextDouble() - 0.5f) * 0.8f;
            float ux = dx / d, uz = dz / d;
            float run = Animals.RunSpeed(a.Kind) * seconds;
            a.GoalX = a.X + (ux - uz * side) * run;
            a.GoalZ = a.Z + (uz + ux * side) * run;
            a.State = AnimalState.Flee;
            a.Timer = seconds;
        }

        /// <summary>Dry land, not a pond, not a building's floor.</summary>
        private bool Walkable(float x, float z)
        {
            if (_ground.Height(x, z) < IslandGround.BeachHeight + 0.4f) return false;
            if (_ground.PondAt(x, z, out _)) return false;
            return Blocked == null || !Blocked(x, z);
        }

        private static int NearestPrey(IReadOnlyList<Prey> players, float x, float z, out float distance)
        {
            distance = float.MaxValue;
            int best = -1;
            if (players == null) return best;
            for (int i = 0; i < players.Count; i++)
            {
                float d = Distance(x, z, players[i].X, players[i].Z);
                if (d < distance)
                {
                    distance = d;
                    best = i;
                }
            }
            return best;
        }

        private static int IndexOf(IReadOnlyList<Prey> players, int key)
        {
            if (players == null) return -1;
            for (int i = 0; i < players.Count; i++)
                if (players[i].Key == key) return i;
            return -1;
        }

        private static bool Near(IReadOnlyList<Prey> players, float x, float z, float range)
        {
            foreach (var p in players)
                if (Distance(x, z, p.X, p.Z) < range) return true;
            return false;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
