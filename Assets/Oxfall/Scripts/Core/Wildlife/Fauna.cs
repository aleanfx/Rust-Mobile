using System;
using System.Collections.Generic;
using Oxfall.Core.Building;
using Oxfall.Core.Rules;
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
        private readonly List<(float X, float Z, float Yaw, float Timer)> _posts = new List<(float, float, float, float)>();
        private readonly List<int> _gone = new List<int>();
        private int _nextId = 1;
        private int _nextPack = 1;

        public Fauna(IslandGround ground, int seed)
        {
            _ground = ground ?? throw new ArgumentNullException(nameof(ground));
            _random = new Random(seed ^ 0x2b0a7);
        }

        /// <summary>Spots animals will not walk onto (a building's floor): set by whoever owns the buildings.</summary>
        public Func<float, float, bool> Blocked { get; set; }

        /// <summary>Whether a building's wall or floor stands between two points (none known: nothing does).</summary>
        public Func<float, float, float, float, float, float, bool> WallBetween { get; set; }

        /// <summary>One has just died (a bandit drops what he carried).</summary>
        public event Action<Animal> Died;

        public IEnumerable<Animal> All => _animals.Values;
        public int Count => _animals.Count;

        public bool TryGet(int id, out Animal animal) => _animals.TryGetValue(id, out animal);

        /// <summary>No animals at all, and none to come (for tests).</summary>
        public void Clear()
        {
            _animals.Clear();
            _waiting.Clear();
            _posts.Clear();
        }

        /// <summary>How many of each roam the island (it is about 350 m across).</summary>
        public const int Boars = 7, Wolves = 6, Deer = 7;

        /// <summary>The herds a fresh island starts with.</summary>
        public void Populate() => Populate(Boars, Wolves, Deer);

        /// <summary>
        /// What animals walk round: trunks and boulders, and any building's floor (with a little room
        /// so a wolf does not push its head through a wall).
        /// </summary>
        public static Func<float, float, bool> Barriers(BuildingGrid grid, Obstacles obstacles, IslandGround ground = null) => (x, z) =>
        {
            if (obstacles != null && obstacles.Blocks(x, z, 0.3f)) return true;
            if (ground != null && Monuments.Solid(ground, x, z, 0.3f)) return true;
            if (grid == null || grid.FoundationCount == 0) return false;
            const float m = 0.7f;
            return grid.HasFoundation(BuildingGrid.CellAt(x, z))
                || grid.HasFoundation(BuildingGrid.CellAt(x + m, z)) || grid.HasFoundation(BuildingGrid.CellAt(x - m, z))
                || grid.HasFoundation(BuildingGrid.CellAt(x, z + m)) || grid.HasFoundation(BuildingGrid.CellAt(x, z - m));
        };

        /// <summary>What blocks a walk: <see cref="Barriers(BuildingGrid, Obstacles, IslandGround)"/>, and high walls.</summary>
        public static Func<float, float, bool> Barriers(Settlement settlement, Obstacles obstacles, IslandGround ground = null)
        {
            var built = Barriers(settlement.Grid, obstacles, ground);
            return (x, z) => built(x, z) || HighWalls.Near(settlement, x, z, 0.4f);
        }

        /// <summary>What blocks a line of sight: <see cref="Walls(BuildingGrid)"/>, and high walls.</summary>
        public static Func<float, float, float, float, float, float, bool> Walls(Settlement settlement)
        {
            var built = Walls(settlement.Grid);
            return (x0, y0, z0, x1, y1, z1) => built(x0, y0, z0, x1, y1, z1) || HighWalls.LineBlocked(settlement, x0, y0, z0, x1, y1, z1);
        }

        /// <summary>
        /// What a building puts in the way of a line of sight: its foundations, and its walls storey by
        /// storey (an opening in one is not counted). Sampled every 40 cm along the line.
        /// </summary>
        public static Func<float, float, float, float, float, float, bool> Walls(BuildingGrid grid) => (x0, y0, z0, x1, y1, z1) =>
        {
            if (grid == null || grid.FoundationCount == 0) return false;
            float dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
            int steps = Math.Max(1, (int)(Math.Sqrt(dx * dx + dz * dz) / 0.4f));
            const float s = BuildingGrid.CellSize, near = 0.12f;
            for (int i = 1; i < steps; i++)
            {
                float t = i / (float)steps, x = x0 + dx * t, y = y0 + dy * t, z = z0 + dz * t;
                var cell = BuildingGrid.CellAt(x, z);
                if (grid.TryGetFoundationTop(cell, out float top) && y < top) return true;
                float fx = x / s - cell.X, fz = z / s - cell.Z;
                if ((fx < near || fx > 1f - near) && WallAt(grid, (int)Math.Round(x / s), cell.Z, EdgeAxis.AlongZ, y)) return true;
                if ((fz < near || fz > 1f - near) && WallAt(grid, cell.X, (int)Math.Round(z / s), EdgeAxis.AlongX, y)) return true;
            }
            return false;
        };

        private static bool WallAt(BuildingGrid grid, int x, int z, EdgeAxis axis, float y)
        {
            var b = new Cell(x, z);
            var a = axis == EdgeAxis.AlongX ? new Cell(x, z - 1) : new Cell(x - 1, z);
            if (!grid.TryGetFoundationTop(b, out float top) && !grid.TryGetFoundationTop(a, out top)) return false;
            int level = (int)Math.Floor((y - top) / BuildingGrid.WallHeight);
            return level >= 0 && level <= BuildingGrid.MaxLevel && grid.HasEdgePiece(new Edge(x, z, axis, level));
        }

        /// <summary>Bandits at their posts (see <see cref="Posts"/>): one at each, keeping to it.</summary>
        public void Guard(IEnumerable<(float X, float Z, float Yaw)> posts)
        {
            foreach (var (x, z, yaw) in posts) AddGuard(x, z, yaw);
        }

        /// <summary>Where the island's ruins are guarded.</summary>
        public static List<(float X, float Z, float Yaw)> Posts(IslandGround ground)
        {
            var posts = new List<(float, float, float)>();
            foreach (var m in Monuments.Find(ground)) posts.AddRange(Monuments.GuardPosts(m));
            return posts;
        }

        private Animal AddGuard(float x, float z, float yaw)
        {
            var a = Add(AnimalKind.Bandit, x, z, yaw);
            a.Guard = true;
            a.PostYaw = yaw;
            return a;
        }

        /// <summary>The herds a fresh island starts with.</summary>
        public void Populate(int boars, int wolves, int deer)
        {
            for (int i = 0; i < boars; i++) Spawn(AnimalKind.Boar, null);
            for (int left = wolves; left > 0; left -= Animals.PackSize) SpawnPack(Math.Min(Animals.PackSize, left), null);
            for (int i = 0; i < deer; i++) Spawn(AnimalKind.Deer, null);
        }

        /// <summary>Puts an animal of this kind somewhere it likes, out of sight of the players. False if nowhere fit.</summary>
        public bool Spawn(AnimalKind kind, IReadOnlyList<Prey> players) => SpawnOne(kind, players) != null;

        /// <summary>A pack of wolves together somewhere in the woods, out of sight of the players. False if nowhere fit.</summary>
        public bool SpawnPack(int size, IReadOnlyList<Prey> players)
        {
            var leader = SpawnOne(AnimalKind.Wolf, players);
            if (leader == null) return false;
            leader.Pack = _nextPack++;
            for (int i = 1; i < size; i++) JoinPack(leader);
            return true;
        }

        /// <summary>A pack of wolves set down at a spot (for tests): the leader there, the others beside it.</summary>
        public List<Animal> AddPack(float x, float z, int size)
        {
            var leader = Add(AnimalKind.Wolf, x, z, 0f);
            leader.Pack = _nextPack++;
            var pack = new List<Animal> { leader };
            for (int i = 1; i < size; i++) pack.Add(JoinPack(leader));
            return pack;
        }

        /// <summary>Another wolf for <paramref name="member"/>'s pack, beside it, taking a side to come in from.</summary>
        private Animal JoinPack(Animal member)
        {
            float x = member.X, z = member.Z;
            for (int i = 0; i < 12; i++)
            {
                double angle = _random.NextDouble() * Math.PI * 2.0;
                float r = 1.5f + (float)_random.NextDouble() * 2.5f;
                float tx = member.X + (float)Math.Cos(angle) * r, tz = member.Z + (float)Math.Sin(angle) * r;
                if (!Walkable(tx, tz)) continue;
                x = tx;
                z = tz;
                break;
            }
            var wolf = Add(AnimalKind.Wolf, x, z, member.Yaw);
            wolf.Pack = member.Pack;
            // A side nobody in the pack takes yet: straight in, from the left, from the right.
            bool left = false, right = false, straight = false;
            foreach (var b in _animals.Values)
            {
                if (b == wolf || b.Pack != wolf.Pack || b.IsDead) continue;
                if (b.Flank > 1f) left = true;
                else if (b.Flank < -1f) right = true;
                else straight = true;
            }
            wolf.Flank = !straight ? 0f : !left ? 70f : !right ? -70f : (float)(_random.NextDouble() * 120.0 - 60.0);
            return wolf;
        }

        /// <summary>A wolf back on the island: with a pack short of wolves (beside one of it, out of the players' sight), or a new pack.</summary>
        private bool SpawnWolf(IReadOnlyList<Prey> players)
        {
            var counts = new Dictionary<int, int>();
            foreach (var b in _animals.Values)
                if (b.Kind == AnimalKind.Wolf && !b.IsDead && b.Pack != 0) counts[b.Pack] = counts.TryGetValue(b.Pack, out int n) ? n + 1 : 1;
            foreach (var b in _animals.Values)
            {
                if (b.Kind != AnimalKind.Wolf || b.IsDead || b.Pack == 0 || counts[b.Pack] >= Animals.PackSize) continue;
                if (b.State == AnimalState.Chase || (players != null && Near(players, b.X, b.Z, SpawnClearance))) continue;
                JoinPack(b);
                return true;
            }
            return SpawnPack(1, players);
        }

        private Animal SpawnOne(AnimalKind kind, IReadOnlyList<Prey> players)
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
                return Add(kind, x, z, (float)_random.NextDouble() * 360f);
            }
            return null;
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
                Y = Monuments.SurfaceHeight(_ground, x, z),
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
                // A bandit leaves no carcass: his body lies a moment, and what he carried drops (see Died).
                bool bandit = a.Kind == AnimalKind.Bandit;
                a.Carcass = bandit ? 0 : Animals.CarcassHits(a.Kind);
                a.Timer = bandit ? Bandits.BodySeconds : Animals.CarcassSeconds;
                Died?.Invoke(a);
                return true;
            }
            if (a.Kind == AnimalKind.Deer) Flee(a, fromX, fromZ, 6f);
            else if (a.Kind == AnimalKind.Bandit)
            {
                // Shot at: turns on whoever did it, wherever they are, and calls the others in.
                bool fresh = a.State != AnimalState.Chase || a.Quarry != attacker;
                a.Angry = true;
                a.Quarry = attacker;
                a.Lost = 0f;
                a.SeenX = fromX;
                a.SeenZ = fromZ;
                a.HasSeen = true;
                if (a.Act == BanditAct.Cover || a.Act == BanditAct.Healing)
                {
                    // Already getting away: keeps at it.
                }
                else if (!a.Healed && a.Health < Bandits.CoverBelow * Animals.MaxHealth(a.Kind)) TakeCover(a, fromX, fromZ);
                else
                {
                    if (a.Act == BanditAct.Searching) a.Act = BanditAct.None;
                    a.State = AnimalState.Chase;
                    a.AttackTimer = Math.Max(a.AttackTimer, 0.4f);
                }
                if (fresh) Alert(a, attacker);
            }
            else if (a.Kind == AnimalKind.Wolf && a.Health < Animals.WolfFleesBelow * Animals.MaxHealth(a.Kind))
            {
                // Badly hurt: off it goes, whimpering.
                a.Angry = false;
                Flee(a, fromX, fromZ, 6f);
            }
            else
            {
                a.Angry = true;
                Chase(a, attacker, fromX, fromZ);
                if (a.Kind == AnimalKind.Wolf) Rally(a, attacker, fromX, fromZ);
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
                var a = _animals[id];
                // A bandit's post is held again later; an animal turns up anywhere.
                if (a.Guard) _posts.Add((a.HomeX, a.HomeZ, a.PostYaw, Bandits.RespawnSeconds));
                else _waiting.Add((a.Kind, Animals.RespawnSeconds));
                _animals.Remove(id);
            }
            for (int i = _posts.Count - 1; i >= 0; i--)
            {
                var (x, z, yaw, timer) = _posts[i];
                timer -= dt;
                if (timer > 0f || (players != null && Near(players, x, z, Bandits.RespawnClearance)))
                {
                    _posts[i] = (x, z, yaw, Math.Max(timer, 0f) + (timer > 0f ? 0f : 10f));
                    continue;
                }
                AddGuard(x, z, yaw);
                _posts.RemoveAt(i);
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
                if (kind == AnimalKind.Wolf ? SpawnWolf(players) : Spawn(kind, players)) _waiting.RemoveAt(i);
                else _waiting[i] = (kind, 20f);
            }
        }

        private void Think(Animal a, float dt, IReadOnlyList<Prey> players, List<Bite> bites)
        {
            if (a.Kind == AnimalKind.Bandit)
            {
                ThinkBandit(a, dt, players, bites);
                return;
            }
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
                    if (a.Kind == AnimalKind.Wolf && !a.Flanked && Math.Abs(a.Flank) > 1f)
                    {
                        // Round to its side first (the bearing fixed when the chase began), then in.
                        float b = a.FlankBearing * (float)(Math.PI / 180.0);
                        float sx = prey.X + (float)Math.Sin(b) * Animals.FlankMetres, sz = prey.Z + (float)Math.Cos(b) * Animals.FlankMetres;
                        if (d <= Animals.FlankMetres * 0.75f || Distance(a.X, a.Z, sx, sz) < 1.5f || !Walkable(sx, sz)) a.Flanked = true;
                        else
                        {
                            a.GoalX = sx;
                            a.GoalZ = sz;
                        }
                    }
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
                            Chase(a, p.Key, p.X, p.Z);
                            Rally(a, p.Key, p.X, p.Z);
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
            if (a.Pace > 0f)
            {
                Slide(a, dt);
                return;
            }
            float speed = a.State == AnimalState.Wander ? Animals.WalkSpeed
                : a.State == AnimalState.Chase || a.State == AnimalState.Flee ? Animals.RunSpeed(a.Kind) : 0f;
            float dx = a.GoalX - a.X, dz = a.GoalZ - a.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            // A chaser stops at biting range; a walker at its spot.
            float stop = a.State == AnimalState.Chase ? Animals.AttackReach * 0.8f : 0.3f;
            // (A hair short counts as there: turning as it walks, it could inch closer for ever.)
            if (speed <= 0f || d <= stop + 0.05f)
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
            a.Y = Monuments.SurfaceHeight(_ground, nx, nz);
            a.Speed = step / dt;
        }

        /// <summary>
        /// A bandit, a little like a soldier: watches from his post; whoever comes in sight is shot at,
        /// stepping from side to side between shots, and the others near him are called in on the radio.
        /// Six rounds, then a reload. Out of range or round a corner, he closes in (never far from his
        /// post). Badly hurt, he runs for cover once and bandages himself. Someone lost is looked for
        /// where they were last seen, then he goes back to his post.
        /// </summary>
        private void ThinkBandit(Animal a, float dt, IReadOnlyList<Prey> players, List<Bite> bites)
        {
            a.Timer -= dt;
            a.AttackTimer -= dt;
            a.ActTimer -= dt;
            a.StrafeTimer -= dt;
            a.Strafing = false;
            a.Pace = 0f;

            if (a.Act == BanditAct.Reloading && a.ActTimer <= 0f)
            {
                a.Rounds = Bandits.Magazine;
                a.Act = BanditAct.None;
            }
            if (a.Act == BanditAct.Cover)
            {
                // Running for it; once there (or it takes too long), the bandage.
                if (Distance(a.X, a.Z, a.GoalX, a.GoalZ) > 0.5f && a.ActTimer > 0f) return;
                a.Act = BanditAct.Healing;
                a.ActTimer = Bandits.HealSeconds;
                a.State = AnimalState.Idle;
            }
            if (a.Act == BanditAct.Healing)
            {
                a.GoalX = a.X;
                a.GoalZ = a.Z;
                if (a.ActTimer > 0f) return;
                a.Health = Math.Min(Animals.MaxHealth(a.Kind), a.Health + Bandits.HealAmount);
                a.Healed = true;
                a.Act = BanditAct.None;
                // He reloaded in cover too.
                a.Rounds = Bandits.Magazine;
                if (IndexOf(players, a.Quarry) >= 0)
                {
                    a.State = AnimalState.Chase;
                    a.Lost = 0f;
                    a.AttackTimer = Math.Max(a.AttackTimer, 0.6f);
                }
                else
                {
                    a.State = AnimalState.Idle;
                    a.Timer = 1f;
                }
            }

            int q = a.State == AnimalState.Chase ? IndexOf(players, a.Quarry) : -1;
            bool seen = false;
            if (q >= 0)
            {
                var p = players[q];
                seen = Sees(a, p);
                if (seen)
                {
                    a.Lost = 0f;
                    a.SeenX = p.X;
                    a.SeenZ = p.Z;
                    a.HasSeen = true;
                }
                else a.Lost += dt;
                if (a.Lost > Bandits.LoseSeconds || Distance(a.X, a.Z, p.X, p.Z) > Bandits.LoseRange) q = -1;
            }
            if (q < 0 && players != null)
            {
                // On the lookout: the nearest player in sight.
                float best = Bandits.Sight;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    float d = Distance(a.X, a.Z, p.X, p.Z);
                    if (d < best && Math.Abs(p.Y - a.Y) < 10f && Sees(a, p))
                    {
                        best = d;
                        q = i;
                    }
                }
                if (q >= 0)
                {
                    var p = players[q];
                    bool fresh = a.State != AnimalState.Chase || a.Quarry != p.Key;
                    a.Quarry = p.Key;
                    a.State = AnimalState.Chase;
                    a.Lost = 0f;
                    a.SeenX = p.X;
                    a.SeenZ = p.Z;
                    a.HasSeen = true;
                    seen = true;
                    if (a.Act == BanditAct.Searching) a.Act = BanditAct.None;
                    a.AttackTimer = Math.Max(a.AttackTimer, Bandits.FirstShotSeconds);
                    if (fresh) Alert(a, p.Key);
                }
            }
            if (q < 0)
            {
                if (a.State == AnimalState.Chase)
                {
                    // Gone: he fills his revolver, and looks where they were last seen, or goes back.
                    a.Angry = false;
                    a.Rounds = Bandits.Magazine;
                    a.Act = BanditAct.None;
                    a.State = AnimalState.Wander;
                    a.Timer = 25f;
                    a.GoalX = a.HomeX;
                    a.GoalZ = a.HomeZ;
                    if (a.HasSeen)
                    {
                        Leashed(a, a.SeenX, a.SeenZ, out a.GoalX, out a.GoalZ);
                        a.Act = BanditAct.Searching;
                        a.ActTimer = Bandits.SearchSeconds;
                        a.HasSeen = false;
                    }
                    return;
                }
                if (a.Act == BanditAct.Searching)
                {
                    if (a.State == AnimalState.Wander && a.Timer > 0f)
                    {
                        a.ActTimer = Bandits.SearchSeconds;
                        return;
                    }
                    if (a.State == AnimalState.Wander) Rest(a);
                    // There: a look all round, then back to his post.
                    a.Yaw = (a.Yaw + 75f * dt) % 360f;
                    if (a.ActTimer > 0f) return;
                    a.Act = BanditAct.None;
                    a.GoalX = a.HomeX;
                    a.GoalZ = a.HomeZ;
                    a.State = AnimalState.Wander;
                    a.Timer = 25f;
                    return;
                }
                if (a.Timer > 0f) return;
                if (a.State == AnimalState.Idle) Wander(a);
                else Rest(a);
                return;
            }

            var prey = players[q];
            float dx = prey.X - a.X, dz = prey.Z - a.Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (seen && dist <= Bandits.Range)
            {
                // Faces them, stepping aside now and then, and fires when he has a round and his aim.
                Turn(a, dx, dz, dt);
                if (a.StrafeTimer <= 0f) Strafe(a, dx, dz, dist);
                a.Pace = Bandits.StrafeSpeed;
                a.Strafing = Distance(a.X, a.Z, a.GoalX, a.GoalZ) > 0.25f;
                if (a.Act == BanditAct.Reloading) return;
                float want = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
                float off = Math.Abs(((want - a.Yaw) % 360f + 540f) % 360f - 180f);
                if (a.AttackTimer <= 0f && off < 15f)
                {
                    a.AttackTimer = Bandits.FireSeconds + (float)_random.NextDouble() * 0.4f;
                    a.Shots++;
                    a.Rounds--;
                    float dy = prey.Y - a.Y;
                    bool hit = _random.NextDouble() < Bandits.HitChance((float)Math.Sqrt(dist * dist + dy * dy));
                    bites?.Add(new Bite(a.Id, prey.Key, hit ? Bandits.Damage : 0f));
                    if (a.Rounds <= 0)
                    {
                        a.Act = BanditAct.Reloading;
                        a.ActTimer = Bandits.ReloadSeconds;
                    }
                }
                return;
            }
            // Closes in (round what is in the way), but not past his leash.
            Leashed(a, prey.X, prey.Z, out a.GoalX, out a.GoalZ);
        }

        /// <summary>A spot towards (x, z), no further than his leash from a bandit's post.</summary>
        private static void Leashed(Animal a, float x, float z, out float gx, out float gz)
        {
            float hx = x - a.HomeX, hz = z - a.HomeZ;
            float fromHome = (float)Math.Sqrt(hx * hx + hz * hz);
            if (fromHome > Bandits.Leash)
            {
                x = a.HomeX + hx / fromHome * Bandits.Leash;
                z = a.HomeZ + hz / fromHome * Bandits.Leash;
            }
            gx = x;
            gz = z;
        }

        /// <summary>A spot a step or two to one side of a bandit (across his line to the target), the other side each time.</summary>
        private void Strafe(Animal a, float dx, float dz, float dist)
        {
            a.StrafeTimer = 1.1f + (float)_random.NextDouble() * 0.9f;
            a.GoalX = a.X;
            a.GoalZ = a.Z;
            if (dist < 0.01f) return;
            float sx = dz / dist, sz = -dx / dist;
            float far = Bandits.StrafeMetres * (0.6f + 0.4f * (float)_random.NextDouble());
            for (int tries = 0; tries < 2; tries++)
            {
                a.StrafeSide = -a.StrafeSide;
                float x = a.X + sx * far * a.StrafeSide, z = a.Z + sz * far * a.StrafeSide;
                if (!Walkable(x, z) || Distance(x, z, a.HomeX, a.HomeZ) > Bandits.Leash) continue;
                a.GoalX = x;
                a.GoalZ = z;
                return;
            }
        }

        /// <summary>A bandit moving at his own pace straight to his spot, facing where he aims, not where he goes.</summary>
        private void Slide(Animal a, float dt)
        {
            float dx = a.GoalX - a.X, dz = a.GoalZ - a.Z;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d <= 0.2f)
            {
                a.Speed = 0f;
                a.Strafing = false;
                return;
            }
            float step = Math.Min(a.Pace * dt, d);
            float nx = a.X + dx / d * step, nz = a.Z + dz / d * step;
            if (!Walkable(nx, nz))
            {
                a.GoalX = a.X;
                a.GoalZ = a.Z;
                a.Speed = 0f;
                a.Strafing = false;
                return;
            }
            a.X = nx;
            a.Z = nz;
            a.Y = Monuments.SurfaceHeight(_ground, nx, nz);
            a.Speed = step / dt;
        }

        /// <summary>
        /// An alarm at (x, z) (a locked crate being hacked): every bandit within <paramref name="radius"/>
        /// that is not busy comes for <paramref name="quarry"/>. Returns how many came.
        /// </summary>
        public int CallBandits(float x, float z, float radius, int quarry)
        {
            int came = 0;
            foreach (var b in _animals.Values)
            {
                if (b.Kind != AnimalKind.Bandit || b.IsDead) continue;
                if (b.Act == BanditAct.Cover || b.Act == BanditAct.Healing) continue;
                if (Distance(x, z, b.X, b.Z) > radius) continue;
                came++;
                if (b.State == AnimalState.Chase) continue;
                if (b.Act == BanditAct.Searching) b.Act = BanditAct.None;
                b.Quarry = quarry;
                b.State = AnimalState.Chase;
                b.Angry = true;
                b.Lost = Bandits.LoseSeconds - Bandits.RadioSeconds;
                b.AttackTimer = Math.Max(b.AttackTimer, Bandits.FirstShotSeconds);
                b.Calls++;
            }
            return came;
        }

        /// <summary>A bandit's radio call: the others near him come for <paramref name="quarry"/>.</summary>
        private void Alert(Animal a, int quarry)
        {
            a.Calls++;
            foreach (var b in _animals.Values)
            {
                if (b == a || b.Kind != AnimalKind.Bandit || b.IsDead || b.State == AnimalState.Chase) continue;
                if (b.Act == BanditAct.Cover || b.Act == BanditAct.Healing) continue;
                if (Distance(a.X, a.Z, b.X, b.Z) > Bandits.RadioRange) continue;
                if (b.Act == BanditAct.Searching) b.Act = BanditAct.None;
                b.Quarry = quarry;
                b.State = AnimalState.Chase;
                b.Angry = true;
                // Not seen yet: he has a while longer than usual to find them.
                b.Lost = Bandits.LoseSeconds - Bandits.RadioSeconds;
                b.AttackTimer = Math.Max(b.AttackTimer, Bandits.FirstShotSeconds);
            }
        }

        /// <summary>Badly hurt: off to the nearest spot out of sight of (fromX, fromZ) near his post, to bandage himself there.</summary>
        private void TakeCover(Animal a, float fromX, float fromZ)
        {
            float fy = Monuments.SurfaceHeight(_ground, fromX, fromZ) + 1.5f;
            float bestX = a.X, bestZ = a.Z, fallbackX = a.X, fallbackZ = a.Z;
            float farthest = Distance(a.X, a.Z, fromX, fromZ);
            bool found = false;
            foreach (float r in CoverRings)
            {
                float bestAway = -1f;
                for (int k = 0; k < 12; k++)
                {
                    double angle = k * Math.PI / 6.0;
                    float x = a.X + (float)Math.Cos(angle) * r, z = a.Z + (float)Math.Sin(angle) * r;
                    if (!Walkable(x, z) || Distance(x, z, a.HomeX, a.HomeZ) > Bandits.Leash) continue;
                    float away = Distance(x, z, fromX, fromZ);
                    if (away > farthest)
                    {
                        farthest = away;
                        fallbackX = x;
                        fallbackZ = z;
                    }
                    float y = Monuments.SurfaceHeight(_ground, x, z);
                    if (LineClear(x, y + 1.2f, z, fromX, fy, fromZ) || away <= bestAway) continue;
                    bestAway = away;
                    bestX = x;
                    bestZ = z;
                    found = true;
                }
                if (found) break;
            }
            a.GoalX = found ? bestX : fallbackX;
            a.GoalZ = found ? bestZ : fallbackZ;
            a.Act = BanditAct.Cover;
            a.ActTimer = Bandits.CoverSeconds;
            a.State = AnimalState.Flee;
        }

        private static readonly float[] CoverRings = { 3f, 6f, 9f, 12f };

        /// <summary>Whether a bandit can see a player: no monument wall, hill or building between his eyes and their chest.</summary>
        private bool Sees(Animal a, Prey p) => LineClear(a.X, a.Y + 1.55f, a.Z, p.X, p.Y + 1.2f, p.Z);

        /// <summary>No monument wall, hill or building between two points.</summary>
        private bool LineClear(float ex, float ey, float ez, float tx, float ty, float tz)
        {
            for (int i = 1; i < 8; i++)
            {
                float t = i / 8f;
                if (_ground.Height(ex + (tx - ex) * t, ez + (tz - ez) * t) > ey + (ty - ey) * t) return false;
            }
            if (Monuments.LineBlocked(_ground, ex, ey, ez, tx, ty, tz)) return false;
            return WallBetween == null || !WallBetween(ex, ey, ez, tx, ty, tz);
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
            // A wolf of a pack keeps near its leader.
            var leader = Leader(a);
            if (leader != null)
            {
                a.HomeX = leader.X;
                a.HomeZ = leader.Z;
            }
            for (int i = 0; i < 8; i++)
            {
                double angle = _random.NextDouble() * Math.PI * 2.0;
                // A guard paces about his post; a pack's wolf keeps close; an animal roams.
                float r = a.Guard ? 1f + (float)_random.NextDouble() * 5f
                    : leader != null ? 2f + (float)_random.NextDouble() * 5f
                    : 4f + (float)_random.NextDouble() * 14f;
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
            // Wherever it ends up is home now (a guard's post stays where it is).
            if (a.Guard) return;
            a.HomeX = a.X;
            a.HomeZ = a.Z;
        }

        private static void Chase(Animal a, int quarry, float preyX, float preyZ)
        {
            if (a.State != AnimalState.Chase)
            {
                a.HomeX = a.X;
                a.HomeZ = a.Z;
                // Which side it comes in from: its own way to the prey, turned by its place in the pack.
                a.FlankBearing = (float)(Math.Atan2(a.X - preyX, a.Z - preyZ) * 180.0 / Math.PI) + a.Flank;
                a.Flanked = false;
            }
            a.State = AnimalState.Chase;
            a.Quarry = quarry;
        }

        /// <summary>A wolf's call to its pack: the others near it come for the same prey.</summary>
        private void Rally(Animal a, int quarry, float preyX, float preyZ)
        {
            if (a.Pack == 0) return;
            a.Calls++;
            foreach (var b in _animals.Values)
            {
                if (b == a || b.Pack != a.Pack || b.IsDead || b.State == AnimalState.Chase || b.State == AnimalState.Flee) continue;
                if (Distance(a.X, a.Z, b.X, b.Z) > Animals.PackRally) continue;
                b.Angry = true;
                Chase(b, quarry, preyX, preyZ);
                b.AttackTimer = Math.Max(b.AttackTimer, 0.3f);
            }
        }

        /// <summary>The pack's leader: its first wolf still alive (null for a lone wolf, or one that leads).</summary>
        private Animal Leader(Animal a)
        {
            if (a.Pack == 0) return null;
            Animal leader = null;
            foreach (var b in _animals.Values)
                if (b.Pack == a.Pack && !b.IsDead && (leader == null || b.Id < leader.Id)) leader = b;
            return leader == a ? null : leader;
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
