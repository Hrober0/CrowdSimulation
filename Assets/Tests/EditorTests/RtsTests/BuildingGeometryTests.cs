using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Where a turned building's cells and its door land (design §6, §14.1).
    ///
    /// The placement check and <see cref="BuildingFootprintSystem"/> both answer this, and for a while they
    /// answered differently - the system rotated, the check did not - so the last test here is the one that
    /// matters: the two agree about the doorstep for every quarter turn.
    /// </summary>
    public class BuildingGeometryTests
    {
        private static readonly int2 Origin = new(4, 4);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        [Test]
        public void AnUnturnedSouthDoorOpensSouth()
        {
            BuildingGeometry.SideOf(Direction.South, GridRotation.None).Should().Be(Direction.South);
            BuildingGeometry.DoorstepOf(Origin, int2.zero, Direction.South, GridRotation.None).Should()
                            .Be(Origin + new int2(0, -1));
        }

        [Test]
        public void AQuarterTurnClockwiseSwingsTheDoorWest()
        {
            BuildingGeometry.SideOf(Direction.South, GridRotation.Clockwise90).Should().Be(Direction.West);
            BuildingGeometry.DoorstepOf(Origin, int2.zero, Direction.South, GridRotation.Clockwise90).Should()
                            .Be(Origin + new int2(-1, 0), "the doorstep swings round with the wall it is cut into");
        }

        [Test]
        public void TheFootprintTurnsAroundTheOriginCellRatherThanItsMiddle()
        {
            // A 2x2 laid from the origin reaches up and right; turned clockwise it reaches down and right,
            // which is why a preview drawn from an unrotated bounding box shows the wrong cells.
            BuildingGeometry.CellOf(Origin, new int2(0, 1), GridRotation.Clockwise90).Should()
                            .Be(Origin + new int2(1, 0));

            BuildingGeometry.CellOf(Origin, new int2(1, 1), GridRotation.Clockwise90).Should()
                            .Be(Origin + new int2(1, -1));
        }

        [Test]
        public void FourQuarterTurnsIsWhereItStarted()
        {
            GridRotation rotation = GridRotation.None;
            for (int turn = 0; turn < 4; turn++)
            {
                rotation = BuildingGeometry.NextClockwise(rotation);
            }

            rotation.Should().Be(GridRotation.None);
            BuildingGeometry.DoorstepOf(Origin, int2.zero, Direction.South, rotation).Should()
                            .Be(Origin + new int2(0, -1));
        }

        /// <summary>
        /// The regression the shared helper exists for. Whatever the placement check believes about where the
        /// doorstep is, the grid has to agree - a building validated against one cell and doored onto another
        /// is one that can be approved onto ground that seals it.
        /// </summary>
        [TestCase(GridRotation.None)]
        [TestCase(GridRotation.Clockwise90)]
        [TestCase(GridRotation.Clockwise180)]
        [TestCase(GridRotation.CounterClockwise90)]
        public void ThePlacedDoorstepIsWhereTheGeometrySaidItWouldBe(GridRotation rotation)
        {
            Entity building = _world.CreateBuilding(Origin, rotation, int2.zero);
            _world.AddEntrance(building, int2.zero, Direction.South);

            _world.Tick();

            int2 expected = BuildingGeometry.DoorstepOf(Origin, int2.zero, Direction.South, rotation);

            DynamicBuffer<BuildingEntranceCell> doors =
                _world.Entities.GetBuffer<BuildingEntranceCell>(building);

            doors.Length.Should().Be(1);
            doors[0].Cell.Should().Be(expected);
            _world.Map.GetCell(expected).Has(CellFlags.Entrance).Should().BeTrue();
        }
    }
}
