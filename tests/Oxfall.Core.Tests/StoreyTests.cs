using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oxfall.Core.Building;
using Oxfall.Core.Items;
using Oxfall.Core.Net;
using Oxfall.Core.Rules;
using Oxfall.Core.World;

namespace Oxfall.Core.Tests
{
    /// <summary>Building up: storeys on roofs, walls on walls, stairs, windows.</summary>
    public class StoreyTests
    {
        private const float Top = 2f;
        private BuildingGrid _grid;

        [SetUp]
        public void SetUp() => _grid = new BuildingGrid();

        /// <summary>A walled, roofed room on (0,0) and the square east of it, walls round both.</summary>
        private void TwoRooms()
        {
            var a = new Cell(0, 0);
            var b = new Cell(1, 0);
            _grid.PlaceFoundation(a, Top);
            _grid.PlaceFoundation(b, Top);
            foreach (var e in a.Edges) _grid.PlaceEdge(e, PieceType.Wall);
            foreach (var e in b.Edges) if (!_grid.HasEdgePiece(e)) _grid.PlaceEdge(e, PieceType.Wall);
            Assert.AreEqual(PlaceResult.Ok, _grid.PlaceRoof(a));
            Assert.AreEqual(PlaceResult.Ok, _grid.PlaceRoof(b));
        }

        [Test]
        public void A_roof_is_the_floor_of_the_storey_above()
        {
            TwoRooms();
            var up = new Cell(0, 0, 1);
            Assert.IsTrue(_grid.HasFloor(up));
            Assert.IsTrue(_grid.TryGetFloorHeight(up, out float floor));
            Assert.AreEqual(Top + BuildingGrid.WallHeight + BuildingGrid.FloorThickness, floor, 1e-4);
            Assert.AreEqual(floor, _grid.EdgeBaseHeight(up.South), 1e-4, "walls up there stand on the roof");
            Assert.AreEqual(PlaceResult.Ok, _grid.PlaceEdge(up.South, PieceType.Window));
            Assert.AreEqual(PlaceResult.Ok, _grid.PlaceRoof(up));
            Assert.AreEqual(1, _grid.LevelAt(up, floor + 0.1f));
            Assert.AreEqual(0, _grid.LevelAt(up, Top + 1f));
        }

        [Test]
        public void Upper_walls_need_a_floor_or_a_wall_under_them()
        {
            var a = new Cell(0, 0);
            _grid.PlaceFoundation(a, Top);
            Assert.AreEqual(PlaceResult.NeedsFloor, _grid.PlaceEdge(a.South.Above, PieceType.Wall));
            _grid.PlaceEdge(a.South, PieceType.Wall);
            Assert.AreEqual(PlaceResult.Ok, _grid.PlaceEdge(a.South.Above, PieceType.Wall), "stacked on the wall below");
            Assert.AreEqual(PlaceResult.NeedsFloor, _grid.PlaceEdge(a.North.Above, PieceType.Wall));
            Assert.AreEqual(PlaceResult.TooHigh, _grid.PlaceEdge(new Edge(0, 0, EdgeAxis.AlongX, BuildingGrid.MaxLevel + 1), PieceType.Wall));
            Assert.AreEqual(PlaceResult.TooHigh, _grid.CanPlaceFoundation(new Cell(0, 0, 1)));
        }

        [Test]
        public void Taking_a_roof_away_brings_down_what_stood_only_on_it()
        {
            // Two squares with no wall between them, roofed.
            var a = new Cell(0, 0);
            var b = new Cell(1, 0);
            _grid.PlaceFoundation(a, Top);
            _grid.PlaceFoundation(b, Top);
            foreach (var e in new[] { a.South, a.North, a.West, b.South, b.North, b.East }) _grid.PlaceEdge(e, PieceType.Wall);
            _grid.PlaceRoof(a);
            _grid.PlaceRoof(b);
            var up = new Cell(0, 0, 1);
            // Upstairs: a wall over the open edge (on the floor only), one over a wall, and stairs.
            Assert.AreEqual(PlaceResult.Ok, _grid.PlaceEdge(up.East, PieceType.Wall));
            Assert.AreEqual(PlaceResult.Ok, _grid.PlaceEdge(up.South, PieceType.Wall));
            Assert.AreEqual(PlaceResult.Ok, _grid.PlaceStairs(up, 0));
            float dropped = 0f;
            _grid.FoundationRemoved += (cell, top) => dropped = top;

            Assert.IsTrue(_grid.RemoveRoof(a));
            Assert.IsFalse(_grid.HasStairs(up), "the stairs stood on that roof");
            Assert.IsTrue(_grid.HasEdgePiece(up.East), "still on the roof next door");
            Assert.IsTrue(_grid.HasEdgePiece(up.South), "stands on the wall under it");
            Assert.AreEqual(Top + BuildingGrid.WallHeight + BuildingGrid.FloorThickness, dropped, 1e-4, "what was set down up there falls");

            Assert.IsTrue(_grid.RemoveRoof(b));
            Assert.IsFalse(_grid.HasEdgePiece(up.East), "nothing under it now");
            Assert.IsTrue(_grid.HasEdgePiece(up.South));
        }

        [Test]
        public void Upper_walls_stacked_on_walls_fall_with_them()
        {
            var a = new Cell(0, 0);
            _grid.PlaceFoundation(a, Top);
            _grid.PlaceEdge(a.South, PieceType.Wall);
            _grid.PlaceEdge(a.South.Above, PieceType.Wall);
            _grid.PlaceEdge(a.South.Above.Above, PieceType.Window);
            Assert.AreEqual(4, _grid.PieceCount);
            Assert.IsTrue(_grid.RemoveEdge(a.South));
            Assert.AreEqual(1, _grid.PieceCount, "the two walls above it come down too");
        }

        [Test]
        public void Stairs_go_up_from_a_floor_and_no_roof_covers_them()
        {
            TwoRooms();
            var well = new Cell(2, 0);
            _grid.PlaceFoundation(well, Top);
            Assert.AreEqual(PlaceResult.Ok, _grid.PlaceStairs(well, 3));
            Assert.AreEqual(3, _grid.StairsFacing(well));
            Assert.AreEqual(PlaceResult.Occupied, _grid.CanPlaceRoof(well), "the stairwell stays open");
            Assert.AreEqual(PlaceResult.Occupied, _grid.PlaceStairs(well, 0));
            // Stairs stack up a stairwell.
            Assert.AreEqual(PlaceResult.Ok, _grid.PlaceStairs(new Cell(2, 0, 1), 1));
            Assert.AreEqual(PlaceResult.NeedsFloor, _grid.CanPlaceStairs(new Cell(1, 0, 2)));
            Assert.AreEqual(PlaceResult.TooHigh, _grid.CanPlaceStairs(new Cell(0, 0, BuildingGrid.MaxLevel)));
            // A roof can't go where stairs are; stairs can't go under a roof.
            Assert.AreEqual(PlaceResult.Occupied, _grid.CanPlaceStairs(new Cell(0, 0)));
            // Without their floor they fall, and so do the ones on them.
            Assert.IsTrue(_grid.RemoveFoundation(well));
            Assert.IsFalse(_grid.HasStairs(well));
            Assert.IsFalse(_grid.HasStairs(new Cell(2, 0, 1)));
        }

        [Test]
        public void A_floor_below_is_found_under_any_storey()
        {
            TwoRooms();
            float x = 1.5f, z = 1.5f;
            Assert.IsTrue(_grid.TryGetFloorBelow(x, Top + 0.1f, z, out float ground));
            Assert.AreEqual(Top, ground, 1e-4);
            Assert.IsTrue(_grid.TryGetFloorBelow(x, Top + 3.3f, z, out float upper));
            Assert.AreEqual(Top + BuildingGrid.WallHeight + BuildingGrid.FloorThickness, upper, 1e-4);
            Assert.IsTrue(_grid.TryGetFloorBelow(x, Top + 2.5f, z, out float under), "under the roof: the floor of the room");
            Assert.AreEqual(Top, under, 1e-4);
            Assert.IsFalse(_grid.TryGetFloorBelow(30f, Top, 30f, out _));
        }

        [Test]
        public void Storeys_and_stairs_go_over_the_wire_and_into_saves()
        {
            var settlement = new Settlement(new IslandGround(20261113), ItemCatalog.CreateDefault());
            _grid = settlement.Grid;
            TwoRooms();
            var up = new Cell(0, 0, 1);
            _grid.PlaceEdge(up.South, PieceType.Window);
            _grid.PlaceEdge(up.West, PieceType.Doorway);
            _grid.PlaceRoof(up);
            _grid.PlaceFoundation(new Cell(2, 0), Top);
            _grid.PlaceStairs(new Cell(2, 0), 2);
            _grid.PlaceStairs(new Cell(2, 0, 1), 0);
            _grid.Upgrade(PieceRef.OnEdge(up.South, PieceType.Window), BuildingTier.Stone);
            _grid.Upgrade(PieceRef.Stairs(new Cell(2, 0, 1)), BuildingTier.Wood);
            _grid.PlaceDoor(up.West);

            var ops = BuildOps.Describe(settlement);
            var writer = new NetWriter();
            Messages.Built(writer, ops);
            var bytes = writer.ToArray();
            var read = new List<BuildOp>();
            Messages.ReadBuilt(new NetReader(bytes, 1, bytes.Length - 1), read);

            var copy = new Settlement(new IslandGround(20261113), ItemCatalog.CreateDefault());
            foreach (var op in read) Assert.IsTrue(BuildOps.Apply(copy, op), op.ToString());
            Assert.AreEqual(_grid.PieceCount, copy.Grid.PieceCount);
            Assert.IsTrue(copy.Grid.HasRoof(up));
            Assert.IsTrue(copy.Grid.HasDoor(up.West));
            Assert.AreEqual(2, copy.Grid.StairsFacing(new Cell(2, 0)));
            Assert.IsTrue(copy.Grid.TryGetTier(PieceRef.OnEdge(up.South, PieceType.Window), out var tier));
            Assert.AreEqual(BuildingTier.Stone, tier);
            Assert.IsTrue(copy.Grid.TryGetTier(PieceRef.Stairs(new Cell(2, 0, 1)), out tier));
            Assert.AreEqual(BuildingTier.Wood, tier);
        }

        [Test]
        public void Ops_and_charges_keep_their_storey()
        {
            var edge = new Edge(3, -2, EdgeAxis.AlongZ, 2);
            foreach (var op in new[]
                     {
                         BuildOp.Edge(edge, PieceType.Window), BuildOp.Roof(new Cell(1, 1, 3)), BuildOp.Stairs(new Cell(-4, 5, 1), 3),
                         BuildOp.Remove(PieceRef.Stairs(new Cell(-4, 5, 1))), BuildOp.Upgrade(PieceRef.OnEdge(edge, PieceType.Wall), BuildingTier.Wood),
                         BuildOp.DoorHealth(edge, 40f), BuildOp.ToggleDoor(edge, true), BuildOp.PlaceLock(LockTarget.Door(edge)),
                     })
            {
                var w = new NetWriter();
                op.Write(w);
                var bytes = w.ToArray();
                var back = BuildOp.Read(new NetReader(bytes, 0, bytes.Length));
                Assert.AreEqual(op.ToString(), back.ToString());
                Assert.AreEqual(op.Level, back.Level, op.ToString());
                Assert.AreEqual(op.Code, back.Code, op.ToString());
            }

            var charge = ChargeTarget.OnPiece(PieceRef.Roof(new Cell(2, 2, 2)));
            var cw = new NetWriter();
            charge.Write(cw);
            var cb = cw.ToArray();
            Assert.AreEqual(charge, ChargeTarget.Read(new NetReader(cb, 0, cb.Length)));
        }

        [Test]
        public void Windows_let_a_blast_through_their_opening_and_floors_stop_it()
        {
            var a = new Cell(0, 0);
            var b = new Cell(0, 1);
            _grid.PlaceFoundation(a, 0f);
            _grid.PlaceFoundation(b, 0f);
            _grid.PlaceEdge(a.North, PieceType.Window);
            float x = 1.5f, z0 = 1.5f, z1 = 4.5f;
            Assert.IsFalse(Explosives.Shielded(_grid, x, 1.5f, z0, x, 1.5f, z1), "through the window");
            Assert.IsTrue(Explosives.Shielded(_grid, x, 0.5f, z0, x, 0.5f, z1), "under the sill");
            Assert.IsTrue(Explosives.Shielded(_grid, 0.3f, 1.5f, z0, 0.3f, 1.5f, z1), "beside the opening");

            // A roof between two storeys.
            foreach (var e in a.Edges) if (!_grid.HasEdgePiece(e)) _grid.PlaceEdge(e, PieceType.Wall);
            _grid.PlaceRoof(a);
            Assert.IsTrue(Explosives.Shielded(_grid, 1.5f, 3.6f, 1.5f, 1.4f, 1f, 1.4f), "the floor above stops it");
            Assert.IsFalse(Explosives.Shielded(_grid, 1.5f, 2.6f, 1.5f, 1.4f, 1f, 1.4f), "both in the room below");
        }

        [Test]
        public void Building_up_through_the_rules_costs_and_checks_reach()
        {
            var settlement = new Settlement(new IslandGround(20261113), ItemCatalog.CreateDefault());
            var s = new Survivor(ItemCatalog.CreateDefault());
            s.Inventory.Add(ItemIds.Wood, 3000);
            _grid = settlement.Grid;
            TwoRooms();
            float x = 1.5f, y = Top, z = 1.5f;
            int wood = s.Inventory.Count(ItemIds.Wood);
            var well = new Cell(2, 0);
            _grid.PlaceFoundation(well, Top);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(settlement, s, 1, BuildOp.Stairs(well, 3), x, y, z, out _));
            Assert.AreEqual(wood - BuildCosts.TwigWood * 2, s.Inventory.Count(ItemIds.Wood));
            Assert.AreEqual(3, _grid.StairsFacing(well));
            var up = new Cell(0, 0, 1);
            Assert.AreEqual(ActionResult.Ok, PlayerActions.Build(settlement, s, 1, BuildOp.Edge(up.South, PieceType.Window), x, y + 3.2f, z, out _));
            Assert.AreEqual(ActionResult.NeedsFloor, PlayerActions.Build(settlement, s, 1, BuildOp.Edge(new Cell(1, 0, 2).South, PieceType.Wall), x, y + 3.2f, z, out _),
                "no roof under that storey, nor a wall");
            Assert.AreEqual(ActionResult.TooFar,
                PlayerActions.Build(settlement, s, 1, BuildOp.Roof(new Cell(0, 0, 1)), x, y - 20f, z, out _));
        }
    }
}
