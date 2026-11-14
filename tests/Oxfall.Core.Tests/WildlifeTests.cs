using System.Collections.Generic;
using NUnit.Framework;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.Wildlife;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    public class WildlifeTests
    {
        private const int Seed = 20261113;

        private static ItemCatalog Catalog => ItemCatalog.CreateDefault();

        /// <summary>Open, flat, dry ground inland with room all round (no trunk or rock in the way).</summary>
        private static (IslandGround ground, float x, float z) Inland()
        {
            var ground = new IslandGround(Seed);
            for (float r = 20f; r < 150f; r += 7f)
            for (float a = 0f; a < 6.28f; a += 0.3f)
            {
                float x = r * (float)System.Math.Cos(a), z = r * (float)System.Math.Sin(a);
                bool ok = true;
                for (float dx = -25f; dx <= 25f && ok; dx += 5f)
                for (float dz = -25f; dz <= 25f && ok; dz += 5f)
                    ok = ground.Height(x + dx, z + dz) > IslandGround.BeachHeight + 1f && !ground.PondAt(x + dx, z + dz, out _);
                if (ok) return (ground, x, z);
            }
            Assert.Fail("no open ground on the test island");
            return default;
        }

        private static void Run(Fauna fauna, float seconds, IReadOnlyList<Fauna.Prey> prey, List<Fauna.Bite> bites = null)
        {
            for (float t = 0f; t < seconds; t += 0.05f) fauna.Tick(0.05f, prey, bites);
        }

        private static float Distance(Animal a, float x, float z)
        {
            float dx = a.X - x, dz = a.Z - z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        [Test]
        public void AWolfComesForAPlayerWhoGetsCloseAndBites()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var wolf = fauna.Add(AnimalKind.Wolf, x, z, 0f);
            var prey = new[] { new Fauna.Prey(4, x + 12f, ground.Height(x + 12f, z), z) };
            var bites = new List<Fauna.Bite>();
            Run(fauna, 6f, prey, bites);

            Assert.AreEqual(AnimalState.Chase, wolf.State);
            Assert.Less(Distance(wolf, prey[0].X, prey[0].Z), Animals.AttackReach + 0.1f, "ran up to them");
            Assert.IsNotEmpty(bites);
            Assert.AreEqual(4, bites[0].Target);
            Assert.AreEqual(Animals.Damage(AnimalKind.Wolf), bites[0].Damage);
            // One bite per attack time, not every tick.
            int expected = (int)(6f / Animals.AttackSeconds) + 1;
            Assert.LessOrEqual(bites.Count, expected);
        }

        [Test]
        public void ADeerBoltsAndAnUnhurtBoarKeepsToItself()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var deer = fauna.Add(AnimalKind.Deer, x, z, 0f);
            var boar = fauna.Add(AnimalKind.Boar, x + 30f, z, 0f);
            var prey = new[] { new Fauna.Prey(1, x + 6f, ground.Height(x + 6f, z), z) };
            var bites = new List<Fauna.Bite>();
            Run(fauna, 3f, prey, bites);

            Assert.Greater(Distance(deer, prey[0].X, prey[0].Z), 15f, "ran off");
            Assert.AreNotEqual(AnimalState.Chase, boar.State);
            Assert.IsEmpty(bites, "neither attacks");
        }

        [Test]
        public void ABoarHurtChargesWhoeverHurtItAndADeerHurtRuns()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var boar = fauna.Add(AnimalKind.Boar, x, z, 0f);
            var player = new Fauna.Prey(9, x + 3f, ground.Height(x + 3f, z), z);
            Assert.IsFalse(fauna.Hit(boar.Id, 20f, 9, player.X, player.Z));
            Assert.AreEqual(AnimalState.Chase, boar.State);
            Assert.AreEqual(Animals.MaxHealth(AnimalKind.Boar) - 20f, boar.Health, 1e-3f);
            var bites = new List<Fauna.Bite>();
            Run(fauna, 3f, new[] { player }, bites);
            Assert.IsNotEmpty(bites);
            Assert.AreEqual(9, bites[0].Target);
            Assert.AreEqual(Animals.Damage(AnimalKind.Boar), bites[0].Damage);

            var deer = fauna.Add(AnimalKind.Deer, x - 40f, z, 0f);
            fauna.Hit(deer.Id, 10f, 9, deer.X - 2f, deer.Z);
            Assert.AreEqual(AnimalState.Flee, deer.State);
        }

        [Test]
        public void ACarcassIsCutUpForMeatAndLeatherThenAnotherTurnsUp()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var deer = fauna.Add(AnimalKind.Deer, x, z, 0f);
            Assert.IsEmpty(fauna.Harvest(deer.Id, ToolKind.Hatchet), "not while it lives");
            Assert.IsTrue(fauna.Hit(deer.Id, 500f, 1, x + 2f, z));
            Assert.IsTrue(deer.IsDead);
            Assert.IsFalse(fauna.Hit(deer.Id, 10f, 1, x + 2f, z), "dead already");

            Assert.IsEmpty(fauna.Harvest(deer.Id, ToolKind.None), "bare hands cut nothing");
            Assert.IsEmpty(fauna.Harvest(deer.Id, ToolKind.Hammer));
            int meat = 0, leather = 0, blows = 0;
            while (deer.Carcass > 0)
            {
                foreach (var (item, amount) in fauna.Harvest(deer.Id, ToolKind.Hatchet))
                {
                    if (item == ItemIds.RawMeat) meat += amount;
                    if (item == ItemIds.Leather) leather += amount;
                }
                blows++;
            }
            Assert.AreEqual(Animals.CarcassHits(AnimalKind.Deer), blows);
            Assert.Greater(meat, 0);
            Assert.Greater(leather, meat, "a deer is mostly hide");

            // Used up, it goes; later another deer turns up somewhere.
            fauna.Tick(0.1f, null, null);
            Assert.IsFalse(fauna.TryGet(deer.Id, out _));
            Assert.AreEqual(0, fauna.Count);
            fauna.Tick(Animals.RespawnSeconds + 1f, null, null);
            Assert.AreEqual(1, fauna.Count);
            foreach (var a in fauna.All) Assert.AreEqual(AnimalKind.Deer, a.Kind);
        }

        [Test]
        public void AnUncutCarcassRotsAway()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var boar = fauna.Add(AnimalKind.Boar, x, z, 0f);
            fauna.Hit(boar.Id, 1000f, 1, x, z + 2f);
            fauna.Tick(Animals.CarcassSeconds - 1f, null, null);
            Assert.IsTrue(fauna.TryGet(boar.Id, out _));
            fauna.Tick(2f, null, null);
            Assert.IsFalse(fauna.TryGet(boar.Id, out _));
        }

        [Test]
        public void AnimalsGoRoundTrunksAndRocksAndStayOutOfBases()
        {
            var ground = new IslandGround(Seed);
            var spawns = ResourceLayout.Generate(ground);
            var obstacles = new Obstacles(spawns);
            Assert.Greater(obstacles.Count, 100);
            NodeSpawn tree = default;
            foreach (var s in spawns)
                if (s.Type == Gathering.ResourceNodeType.Tree) { tree = s; break; }
            Assert.IsTrue(obstacles.Blocks(tree.X, tree.Z, 0.3f), "a trunk");
            Assert.IsTrue(obstacles.Blocks(tree.X + 0.4f, tree.Z, 0.3f), "brushing it");

            var grid = new Building.BuildingGrid();
            var blocked = Fauna.Barriers(grid, obstacles);
            var (_, x, z) = Inland();
            var cell = Building.BuildingGrid.CellAt(x, z);
            Assert.IsFalse(blocked(x, z) && !obstacles.Blocks(x, z, 0.3f), "no base yet");
            Assert.AreEqual(Building.PlaceResult.Ok, grid.PlaceFoundation(cell, ground.Height(x, z) + 0.2f), "a foundation");
            Assert.IsTrue(blocked(x, z), "a floor is no place for a wolf");

            // A wolf kept from its prey by a wall of blocked ground never steps onto it.
            var fauna = new Fauna(ground, Seed) { Blocked = (px, pz) => px > x + 4f && px < x + 6f };
            var wolf = fauna.Add(AnimalKind.Wolf, x, z, 90f);
            var prey = new[] { new Fauna.Prey(1, x + 12f, ground.Height(x + 12f, z), z) };
            for (int i = 0; i < 200; i++)
            {
                fauna.Tick(0.05f, prey, null);
                Assert.IsFalse(wolf.X > x + 4f && wolf.X < x + 6f, "stepped onto blocked ground at tick " + i);
            }
        }

        [Test]
        public void AFreshIslandHasItsHerdsInTheirPlaces()
        {
            var ground = new IslandGround(Seed);
            var fauna = new Fauna(ground, Seed);
            fauna.Populate();
            Assert.AreEqual(Fauna.Boars + Fauna.Wolves + Fauna.Deer, fauna.Count);
            foreach (var a in fauna.All)
            {
                Assert.Greater(ground.Height(a.X, a.Z), IslandGround.BeachHeight, a.ToString());
                Assert.IsFalse(ground.PondAt(a.X, a.Z, out _), a.ToString());
            }
        }

        // ---- On the server

        private static (ServerWorld world, SurvivorRecord record, ServerState.Player player) Hunter()
        {
            var world = new ServerWorld(Seed);
            var record = world.Join(Protocol.Version, "Ale", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0f, out var player, out _, out _);
            return (world, record, player);
        }

        [Test]
        public void TheServerJudgesBlowsAtAnimalsAndHandsOutWhatTheCarcassGives()
        {
            var (world, record, player) = Hunter();
            record.Survivor.Inventory.Add(ItemIds.WoodenSpear, 1);
            var p = player.Pose;
            var deer = world.Fauna.Add(AnimalKind.Deer, p.X + 2f, p.Z, 0f);

            Assert.AreEqual(AnimalBlow.None, world.StrikeAnimal(player.Id, deer.Id, ToolKind.MetalHatchet, 1f, out _, out _), "a tool they do not have");
            Assert.AreEqual(AnimalBlow.None, world.StrikeAnimal(player.Id, 9999, ToolKind.Spear, 1f, out _, out _), "no such animal");
            var far = world.Fauna.Add(AnimalKind.Boar, p.X + 30f, p.Z, 0f);
            Assert.AreEqual(AnimalBlow.None, world.StrikeAnimal(player.Id, far.Id, ToolKind.Spear, 1f, out _, out _), "out of reach");

            float now = 2f;
            Assert.AreEqual(AnimalBlow.Hurt, world.StrikeAnimal(player.Id, deer.Id, ToolKind.Spear, now, out float damage, out _));
            Assert.AreEqual(Combat.MeleeDamage(ToolKind.Spear), damage);
            Assert.AreEqual(AnimalBlow.None, world.StrikeAnimal(player.Id, deer.Id, ToolKind.Spear, now + 0.1f, out _, out _), "too soon");
            // It ran: back where it was, for the test.
            deer.X = p.X + 2f;
            deer.Z = p.Z;
            now += 1f;
            Assert.AreEqual(AnimalBlow.Hurt, world.StrikeAnimal(player.Id, deer.Id, ToolKind.Spear, now, out _, out _));
            now += 1f;
            Assert.AreEqual(AnimalBlow.Killed, world.StrikeAnimal(player.Id, deer.Id, ToolKind.Spear, now, out _, out _));

            // Cut up with the rock everyone wakes with.
            int blows = 0;
            AnimalBlow blow;
            do
            {
                now += 1f;
                blow = world.StrikeAnimal(player.Id, deer.Id, ToolKind.Rock, now, out _, out int lost);
                Assert.AreEqual(0, lost);
                blows++;
            } while (blow == AnimalBlow.Cut && blows < 20);
            Assert.AreEqual(AnimalBlow.UsedUp, blow);
            Assert.AreEqual(Animals.CarcassHits(AnimalKind.Deer), blows);
            Assert.Greater(record.Survivor.Inventory.Count(ItemIds.RawMeat), 0);
            Assert.Greater(record.Survivor.Inventory.Count(ItemIds.Leather), 0);
            Assert.AreEqual(AnimalBlow.None, world.StrikeAnimal(player.Id, deer.Id, ToolKind.Rock, now + 1f, out _, out _), "nothing left");
        }

        [Test]
        public void AWolfBitesAPlayerOnTheServerAndCanKillThem()
        {
            var (world, record, player) = Hunter();
            record.Survivor.Inventory.Add(ItemIds.Wood, 50);
            var p = player.Pose;
            world.Fauna.Add(AnimalKind.Wolf, p.X + 1.2f, p.Z, 270f);
            world.Announcements.Clear();
            // It notices them, then bites.
            world.Tick(0.05f);
            world.Tick(0.05f);
            Assert.AreEqual(1, world.Bites.Count);
            Assert.AreEqual(player.Id, world.Bites[0].Target);
            Assert.AreEqual(AnimalKind.Wolf, world.Bites[0].Kind);
            Assert.AreEqual(Animals.Damage(AnimalKind.Wolf), world.Bites[0].Damage, 1e-3f);
            Assert.AreEqual(100f - Animals.Damage(AnimalKind.Wolf), record.Survivor.Vitals.Health, 0.5f);

            for (int i = 0; i < 400 && !record.Survivor.Vitals.IsDead; i++) world.Tick(0.05f);
            Assert.IsTrue(record.Survivor.Vitals.IsDead, "a wolf left alone kills");
            Assert.IsTrue(world.Bites[world.Bites.Count - 1].Killed);
            Assert.IsTrue(world.Announcements.Exists(op => op.Kind == BuildOpKind.Deploy && op.Deployed.Kind == DeployableKind.Bag), "and the bag drops");
            int count = world.Bites.Count;
            world.Tick(5f);
            Assert.AreEqual(count, world.Bites.Count, "the dead are left alone");
        }

        [Test]
        public void ATestServerPutsAnAnimalAheadOfThePlayer()
        {
            var (world, _, player) = Hunter();
            int before = world.Fauna.Count;
            Assert.AreEqual(ActionResult.Invalid, world.Act(player.Id, ActKind.DevSpawnAnimal, (int)AnimalKind.Wolf, 0, null));
            world.DevMode = true;
            Assert.AreEqual(ActionResult.Ok, world.Act(player.Id, ActKind.DevSpawnAnimal, (int)AnimalKind.Wolf, 0, null));
            Assert.AreEqual(ActionResult.Invalid, world.Act(player.Id, ActKind.DevSpawnAnimal, 77, 0, null));
            Assert.AreEqual(before + 1, world.Fauna.Count);
        }

        [Test]
        public void AnimalMessagesRoundTrip()
        {
            var (ground, x, z) = Inland();
            var fauna = new Fauna(ground, Seed);
            var wolf = fauna.Add(AnimalKind.Wolf, x, z, 123f);
            fauna.Add(AnimalKind.Deer, x + 500f, z, 0f);
            fauna.Hit(wolf.Id, 30f, 1, x + 2f, z);
            var w = new NetWriter();
            Messages.Animals(w, fauna.All, x, z, 100f);
            var r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Animals, (MessageType)r.Byte());
            var snaps = new List<AnimalSnap>();
            Messages.ReadAnimals(r, snaps);
            Assert.AreEqual(1, snaps.Count, "only those in range");
            var s = snaps[0];
            Assert.AreEqual(wolf.Id, s.Id);
            Assert.AreEqual(AnimalKind.Wolf, s.Kind);
            Assert.AreEqual(AnimalState.Chase, s.State);
            Assert.AreEqual(x, s.X, 1e-3f);
            Assert.AreEqual(123f, s.Yaw, 1.5f);
            Assert.AreEqual(60f / 90f, s.Health, 0.01f);

            Messages.AnimalHit(w, 70000, AnimalBlow.Killed, 33.4f);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.AnimalHit, (MessageType)r.Byte());
            Messages.ReadAnimalHit(r, out int id, out var blow, out int damage);
            Assert.AreEqual((70000, AnimalBlow.Killed, 33), (id, blow, damage));

            Messages.Bitten(w, 5, AnimalKind.Boar, 11f, true);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.Bitten, (MessageType)r.Byte());
            Messages.ReadBitten(r, out ushort target, out var kind, out damage, out bool killed);
            Assert.AreEqual((5, AnimalKind.Boar, 11, true), (target, kind, damage, killed));

            Messages.StrikeAnimal(w, 42, (byte)ToolKind.Spear);
            r = new NetReader(w.Buffer, 0, w.Length);
            Assert.AreEqual(MessageType.StrikeAnimal, (MessageType)r.Byte());
            Messages.ReadStrikeAnimal(r, out id, out byte tool);
            Assert.AreEqual((42, (byte)ToolKind.Spear), (id, tool));
        }

        // ---- Cooking

        private static int SlotOf(Inventory inventory, string itemId)
        {
            for (int i = 0; i < inventory.SlotCount; i++)
                if (inventory[i].ItemId == itemId) return i;
            return -1;
        }

        [Test]
        public void ACampfireRoastsRawMeat()
        {
            var ground = new IslandGround(Seed);
            var spawn = Spawns.Beach(ground, new System.Random(Seed));
            var w = new Settlement(ground, Catalog);
            var s = new Survivor(Catalog);
            s.Inventory.Add(ItemIds.Campfire, 1);
            s.Inventory.Add(ItemIds.RawMeat, 3);
            float y = ground.Height(spawn.X, spawn.Z);
            var fire = new Deployable(0, DeployableKind.Campfire, spawn.X + 1.5f, y, spawn.Z, 0f);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(w, s, 1, BuildOp.Deploy(fire), spawn.X, y, spawn.Z, out var done));
            int id = done.Deployed.Id;
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Open(w, s, 1, id, spawn.X, y, spawn.Z));
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Put(w, s, SlotOf(s.Inventory, ItemIds.RawMeat), -1));
            var box = w.Container(id);
            Assert.AreEqual(3, box.Count(ItemIds.RawMeat));
            w.Tick(Settlement.CookSeconds + 0.1f);
            Assert.AreEqual(2, box.Count(ItemIds.RawMeat));
            Assert.AreEqual(1, box.Count(ItemIds.CookedMeat));
            w.Tick(Settlement.CookSeconds * 3f);
            Assert.AreEqual(0, box.Count(ItemIds.RawMeat));
            Assert.AreEqual(3, box.Count(ItemIds.CookedMeat));

            // Roasted is far better eating than raw.
            var raw = Catalog.Get(ItemIds.RawMeat);
            var cooked = Catalog.Get(ItemIds.CookedMeat);
            Assert.Greater(cooked.Nourishment.Food, raw.Nourishment.Food);
            Assert.Less(raw.Nourishment.Water, 0f, "raw meat makes you thirsty");
        }
    }
}
