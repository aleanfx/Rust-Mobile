using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Items;

namespace Oxfall.Core.Tests
{
    public class BuildingTests
    {
        private BuildingGrid _grid;

        [SetUp]
        public void SetUp() => _grid = new BuildingGrid();

        [Test]
        public void Cells_are_found_from_world_positions_including_negatives()
        {
            Assert.That(BuildingGrid.CellAt(0.5f, 2.9f), Is.EqualTo(new Cell(0, 0)));
            Assert.That(BuildingGrid.CellAt(-0.1f, 3.1f), Is.EqualTo(new Cell(-1, 1)));
        }

        [Test]
        public void Neighbouring_cells_share_an_edge()
        {
            var a = new Cell(0, 0);
            var north = new Cell(0, 1);
            var east = new Cell(1, 0);
            Assert.That(a.North, Is.EqualTo(north.South));
            Assert.That(a.East, Is.EqualTo(east.West));
        }

        [Test]
        public void A_cell_takes_one_foundation()
        {
            Assert.That(_grid.PlaceFoundation(new Cell(0, 0), 1f), Is.EqualTo(PlaceResult.Ok));
            Assert.That(_grid.PlaceFoundation(new Cell(0, 0), 1f), Is.EqualTo(PlaceResult.Occupied));
        }

        [Test]
        public void Walls_need_a_foundation_on_either_side()
        {
            var cell = new Cell(2, 2);
            Assert.That(_grid.PlaceEdge(cell.North, PieceType.Wall), Is.EqualTo(PlaceResult.NeedsFoundation));
            _grid.PlaceFoundation(cell, 1f);
            Assert.That(_grid.PlaceEdge(cell.North, PieceType.Wall), Is.EqualTo(PlaceResult.Ok));
            Assert.That(_grid.PlaceEdge(cell.North, PieceType.Doorway), Is.EqualTo(PlaceResult.Occupied));
        }

        [Test]
        public void Roofs_need_a_foundation_and_a_wall()
        {
            var cell = new Cell(0, 0);
            Assert.That(_grid.PlaceRoof(cell), Is.EqualTo(PlaceResult.NeedsFoundation));
            _grid.PlaceFoundation(cell, 1f);
            Assert.That(_grid.PlaceRoof(cell), Is.EqualTo(PlaceResult.NeedsWall));
            _grid.PlaceEdge(cell.West, PieceType.Wall);
            Assert.That(_grid.PlaceRoof(cell), Is.EqualTo(PlaceResult.Ok));
        }

        [Test]
        public void Removing_the_only_wall_brings_the_roof_down()
        {
            var cell = new Cell(0, 0);
            _grid.PlaceFoundation(cell, 1f);
            _grid.PlaceEdge(cell.West, PieceType.Wall);
            _grid.PlaceRoof(cell);
            _grid.RemoveEdge(cell.West);
            Assert.That(_grid.HasRoof(cell), Is.False);
        }

        [Test]
        public void Removing_a_foundation_keeps_walls_held_by_the_neighbour()
        {
            var a = new Cell(0, 0);
            var b = new Cell(1, 0);
            _grid.PlaceFoundation(a, 1f);
            _grid.PlaceFoundation(b, 1f);
            _grid.PlaceEdge(a.East, PieceType.Wall);   // shared with b
            _grid.PlaceEdge(a.West, PieceType.Wall);   // only on a
            _grid.RemoveFoundation(a);
            Assert.That(_grid.HasEdgePiece(a.East), Is.True);
            Assert.That(_grid.HasEdgePiece(a.West), Is.False);
        }

        [Test]
        public void A_wall_stands_on_the_higher_of_its_two_floors()
        {
            var a = new Cell(0, 0);
            var b = new Cell(1, 0);
            _grid.PlaceFoundation(a, 1f);
            _grid.PlaceFoundation(b, 1.4f);
            Assert.That(_grid.EdgeBaseHeight(a.East), Is.EqualTo(1.4f));
        }

        [Test]
        public void Paying_for_a_piece_is_all_or_nothing()
        {
            var inv = new Inventory(ItemCatalog.CreateDefault());
            inv.Add(ItemIds.Wood, 40);
            Assert.That(BuildCosts.TryPay(inv, PieceType.Foundation), Is.False);
            Assert.That(inv.Count(ItemIds.Wood), Is.EqualTo(40));
            inv.Add(ItemIds.Wood, 10);
            Assert.That(BuildCosts.TryPay(inv, PieceType.Foundation), Is.True);
            Assert.That(inv.Count(ItemIds.Wood), Is.EqualTo(0));
        }
    }
}
