using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    public class BuildingFootprintSystemTests
    {
        /// <summary>An L, to catch anything that assumes a footprint is a rectangle.</summary>
        private static readonly int2[] LShape =
        {
            new(0, 0),
            new(0, 1),
            new(1, 0),
        };

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        [Test]
        public void EveryFootprintCell_IsBlockedAndFlagged()
        {
            int2 origin = new(2, 2);
            _world.CreateBuilding(origin, GridRotation.None, LShape);

            _world.Tick();

            foreach (int2 offset in LShape)
            {
                CellData cell = _world.Map.GetCell(origin + offset);
                cell.IsPassable.Should().BeFalse();
                cell.Has(CellFlags.Building).Should().BeTrue();
            }
        }

        [Test]
        public void CellsOutsideTheFootprint_AreLeftAlone()
        {
            int2 origin = new(2, 2);
            _world.CreateBuilding(origin, GridRotation.None, LShape);

            _world.Tick();

            // The L leaves (1, 1) open - a rectangle-assuming implementation would have taken it.
            CellData gap = _world.Map.GetCell(origin + new int2(1, 1));
            gap.IsPassable.Should().BeTrue();
            gap.Has(CellFlags.Building).Should().BeFalse();
        }

        [Test]
        public void ARingFootprint_LeavesItsCourtyardWalkable()
        {
            int2 origin = new(-4, -4);
            int2[] ring =
            {
                new(0, 0), new(1, 0), new(2, 0),
                new(0, 1), new(2, 1),
                new(0, 2), new(1, 2), new(2, 2),
            };
            _world.CreateBuilding(origin, GridRotation.None, ring);

            _world.Tick();

            _world.Map.IsPassable(origin + new int2(1, 1)).Should().BeTrue();
            _world.Map.IsPassable(origin + new int2(1, 0)).Should().BeFalse();
        }

        [Test]
        public void Rotation_MovesTheFootprintWithoutChangingItsShape()
        {
            int2 origin = new(0, 0);
            _world.CreateBuilding(origin, GridRotation.Clockwise90, LShape);

            _world.Tick();

            // (0,1) -> (1,0) and (1,0) -> (0,-1) under a clockwise quarter turn.
            _world.Map.IsPassable(origin).Should().BeFalse();
            _world.Map.IsPassable(origin + new int2(1, 0)).Should().BeFalse();
            _world.Map.IsPassable(origin + new int2(0, -1)).Should().BeFalse();

            _world.Map.IsPassable(origin + new int2(0, 1)).Should().BeTrue("that cell belongs to the unrotated shape");
        }

        [Test]
        public void Demolishing_GivesBackExactlyTheCellsThatWereTaken()
        {
            int2 origin = new(6, -6);
            Entity building = _world.CreateBuilding(origin, GridRotation.Clockwise180, LShape);
            _world.Tick();

            _world.Entities.DestroyEntity(building);
            _world.Tick();

            foreach (int2 offset in LShape)
            {
                int2 cell = origin + RotationUtils.Rotate(offset, GridRotation.Clockwise180);
                CellData data = _world.Map.GetCell(cell);
                data.CostSum.Should().Be(0);
                data.Has(CellFlags.Building).Should().BeFalse();
            }

            _world.Entities.Exists(building).Should().BeFalse();
        }

        [Test]
        public void ABuildingOnTopOfATree_StaysBlockedByTheTreeAfterDemolition()
        {
            int2 cell = new(3, 3);
            _world.CreateCellObject(cell, CellData.BLOCKED);
            Entity building = _world.CreateBuilding(cell, GridRotation.None, new int2(0, 0));
            _world.Tick();

            _world.Map.GetCost(cell).Should().Be(2 * CellData.BLOCKED, "both contributors are counted");

            _world.Entities.DestroyEntity(building);
            _world.Tick();

            _world.Map.GetCost(cell).Should().Be(CellData.BLOCKED);
            _world.Map.IsPassable(cell).Should().BeFalse("the tree is still standing");
        }

        [Test]
        public void PlacementHappensOnce_HoweverManyTimesThePhaseRuns()
        {
            int2 origin = new(0, 0);
            _world.CreateBuilding(origin, GridRotation.None, new int2(0, 0));

            _world.Tick();
            _world.Tick();

            _world.Map.GetCost(origin).Should().Be(CellData.BLOCKED);
        }

        [Test]
        public void TheOccupiedCells_AreReadableFromTheBuilding()
        {
            int2 origin = new(5, 5);
            Entity building = _world.CreateBuilding(origin, GridRotation.None, LShape);

            _world.Tick();

            DynamicBuffer<BuildingFootprintCell> occupied =
                _world.Entities.GetBuffer<BuildingFootprintCell>(building);

            occupied.Length.Should().Be(LShape.Length);
            occupied[0].Cell.Should().Be(origin + LShape[0]);
        }
    }
}
