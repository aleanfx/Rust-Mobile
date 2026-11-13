using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Items;

namespace Oxfall.Core.Tests
{
    public class BuildingTierTests
    {
        private BuildingGrid _grid;
        private Cell _cell;

        [SetUp]
        public void SetUp()
        {
            _grid = new BuildingGrid();
            _cell = new Cell(0, 0);
            _grid.PlaceFoundation(_cell, 1f);
        }

        [Test]
        public void Pieces_go_up_as_twig_with_twig_health()
        {
            var f = PieceRef.Foundation(_cell);
            Assert.That(_grid.TryGetTier(f, out var tier), Is.True);
            Assert.That(tier, Is.EqualTo(BuildingTier.Twig));
            Assert.That(_grid.Health(f), Is.EqualTo(BuildingTiers.MaxHealth(BuildingTier.Twig)));
        }

        [Test]
        public void Upgrading_raises_the_tier_and_heals_to_its_health()
        {
            var f = PieceRef.Foundation(_cell);
            PieceRef upgraded = default;
            _grid.PieceUpgraded += (p, t) => upgraded = p;
            Assert.That(_grid.Upgrade(f, BuildingTier.Stone), Is.EqualTo(PlaceResult.Ok));
            _grid.TryGetTier(f, out var tier);
            Assert.That(tier, Is.EqualTo(BuildingTier.Stone));
            Assert.That(_grid.Health(f), Is.EqualTo(500f));
            Assert.That(upgraded, Is.EqualTo(f));
        }

        [Test]
        public void Upgrades_only_go_up_and_need_a_piece()
        {
            var f = PieceRef.Foundation(_cell);
            _grid.Upgrade(f, BuildingTier.Wood);
            Assert.That(_grid.Upgrade(f, BuildingTier.Wood), Is.EqualTo(PlaceResult.NotAnUpgrade));
            Assert.That(_grid.Upgrade(f, BuildingTier.Twig), Is.EqualTo(PlaceResult.NotAnUpgrade));
            Assert.That(_grid.Upgrade(PieceRef.Roof(_cell), BuildingTier.Wood), Is.EqualTo(PlaceResult.NoPiece));
        }

        [Test]
        public void An_edge_piece_is_found_by_its_edge_whatever_type_the_ref_says()
        {
            _grid.PlaceEdge(_cell.North, PieceType.Doorway);
            Assert.That(_grid.TryGetEdgePiece(_cell.North, out var piece), Is.True);
            Assert.That(piece.Type, Is.EqualTo(PieceType.Doorway));
            Assert.That(_grid.Exists(PieceRef.OnEdge(_cell.North, PieceType.Wall)), Is.True);
        }

        [Test]
        public void Damage_wears_a_piece_down_and_brings_it_down_with_what_it_held()
        {
            _grid.PlaceEdge(_cell.West, PieceType.Wall);
            _grid.PlaceRoof(_cell);
            var wall = PieceRef.OnEdge(_cell.West, PieceType.Wall);
            Assert.That(_grid.Damage(wall, 4f), Is.False);
            Assert.That(_grid.Health(wall), Is.EqualTo(6f));
            Assert.That(_grid.Damage(wall, 6f), Is.True);
            Assert.That(_grid.HasEdgePiece(_cell.West), Is.False);
            Assert.That(_grid.HasRoof(_cell), Is.False, "the roof lost its only wall");
            Assert.That(_grid.PieceCount, Is.EqualTo(1));
        }

        [Test]
        public void Removing_a_foundation_forgets_every_piece_on_it()
        {
            _grid.PlaceEdge(_cell.West, PieceType.Wall);
            _grid.PlaceEdge(_cell.East, PieceType.Doorway);
            _grid.PlaceDoor(_cell.East);
            _grid.PlaceRoof(_cell);
            _grid.Remove(PieceRef.Foundation(_cell));
            Assert.That(_grid.PieceCount, Is.EqualTo(0));
            Assert.That(_grid.HasDoor(_cell.East), Is.False);
        }

        [Test]
        public void Every_piece_that_comes_down_is_announced_with_its_tier()
        {
            _grid.PlaceEdge(_cell.West, PieceType.Wall);
            _grid.PlaceRoof(_cell);
            _grid.Upgrade(PieceRef.OnEdge(_cell.West, PieceType.Wall), BuildingTier.Stone);
            var removed = new System.Collections.Generic.List<(PieceRef, BuildingTier)>();
            _grid.PieceRemoved += (p, t) => removed.Add((p, t));
            _grid.RemoveFoundation(_cell);
            Assert.That(removed, Has.Count.EqualTo(3));
            Assert.That(removed, Does.Contain((PieceRef.OnEdge(_cell.West, PieceType.Wall), BuildingTier.Stone)));
            Assert.That(removed, Does.Contain((PieceRef.Roof(_cell), BuildingTier.Twig)));
        }

        [Test]
        public void There_is_no_tier_above_the_highest()
        {
            Assert.That(BuildingTiers.TryNext(BuildingTier.Twig, out var next), Is.True);
            Assert.That(next, Is.EqualTo(BuildingTier.Wood));
            Assert.That(BuildingTiers.TryNext(BuildingTiers.Highest, out _), Is.False);
        }
    }

    public class DoorTests
    {
        [Test]
        public void Doors_hang_only_in_doorways_one_each()
        {
            var grid = new BuildingGrid();
            var cell = new Cell(0, 0);
            Assert.That(grid.PlaceDoor(cell.North), Is.EqualTo(PlaceResult.NeedsDoorway));
            grid.PlaceFoundation(cell, 1f);
            grid.PlaceEdge(cell.North, PieceType.Wall);
            grid.PlaceEdge(cell.South, PieceType.Doorway);
            Assert.That(grid.PlaceDoor(cell.North), Is.EqualTo(PlaceResult.NeedsDoorway));
            Assert.That(grid.PlaceDoor(cell.South), Is.EqualTo(PlaceResult.Ok));
            Assert.That(grid.PlaceDoor(cell.South), Is.EqualTo(PlaceResult.Occupied));
        }

        [Test]
        public void Doors_start_closed_and_toggle()
        {
            var grid = new BuildingGrid();
            var cell = new Cell(0, 0);
            grid.PlaceFoundation(cell, 1f);
            grid.PlaceEdge(cell.South, PieceType.Doorway);
            grid.PlaceDoor(cell.South);
            bool? last = null;
            grid.DoorToggled += (e, open) => last = open;
            Assert.That(grid.IsDoorOpen(cell.South), Is.False);
            Assert.That(grid.ToggleDoor(cell.South), Is.True);
            Assert.That(grid.IsDoorOpen(cell.South), Is.True);
            Assert.That(last, Is.True);
            grid.ToggleDoor(cell.South);
            Assert.That(grid.IsDoorOpen(cell.South), Is.False);
            Assert.That(grid.ToggleDoor(cell.North), Is.False);
        }

        [Test]
        public void Taking_the_doorway_down_takes_the_door()
        {
            var grid = new BuildingGrid();
            var cell = new Cell(0, 0);
            grid.PlaceFoundation(cell, 1f);
            grid.PlaceEdge(cell.South, PieceType.Doorway);
            grid.PlaceDoor(cell.South);
            int removed = 0;
            grid.DoorRemoved += e => removed++;
            grid.RemoveEdge(cell.South);
            Assert.That(grid.HasDoor(cell.South), Is.False);
            Assert.That(removed, Is.EqualTo(1));
        }
    }

    public class BuildCostTests
    {
        [Test]
        public void Upgrades_cost_wood_then_stone()
        {
            Assert.That(BuildCosts.Upgrade(PieceType.Wall, BuildingTier.Wood).Single().ItemId, Is.EqualTo(ItemIds.Wood));
            Assert.That(BuildCosts.Upgrade(PieceType.Wall, BuildingTier.Stone).Single().ItemId, Is.EqualTo(ItemIds.Stone));
        }

        [Test]
        public void Demolishing_gives_back_half_of_everything_put_in()
        {
            var twig = BuildCosts.Refund(PieceType.Wall, BuildingTier.Twig);
            Assert.That(twig.Single().Amount, Is.EqualTo(BuildCosts.TwigWood / 2));

            var stone = BuildCosts.Refund(PieceType.Wall, BuildingTier.Stone);
            Assert.That(stone.First(i => i.ItemId == ItemIds.Wood).Amount, Is.EqualTo((BuildCosts.TwigWood + BuildCosts.WoodUpgrade) / 2));
            Assert.That(stone.First(i => i.ItemId == ItemIds.Stone).Amount, Is.EqualTo(BuildCosts.StoneUpgrade / 2));
        }

        [Test]
        public void Paying_is_all_or_nothing()
        {
            var inventory = new Inventory(ItemCatalog.CreateDefault());
            inventory.Add(ItemIds.Wood, 100);
            var cost = BuildCosts.Upgrade(PieceType.Wall, BuildingTier.Wood);
            Assert.That(BuildCosts.TryPay(inventory, cost), Is.False);
            Assert.That(inventory.Count(ItemIds.Wood), Is.EqualTo(100));
            inventory.Add(ItemIds.Wood, 50);
            Assert.That(BuildCosts.TryPay(inventory, cost), Is.True);
            Assert.That(inventory.Count(ItemIds.Wood), Is.EqualTo(0));
        }

        [Test]
        public void Costs_read_as_amount_and_name()
        {
            var catalog = ItemCatalog.CreateDefault();
            Assert.That(BuildCosts.Describe(BuildCosts.Upgrade(PieceType.Roof, BuildingTier.Stone), catalog), Is.EqualTo("200 Piedra"));
        }
    }
}
