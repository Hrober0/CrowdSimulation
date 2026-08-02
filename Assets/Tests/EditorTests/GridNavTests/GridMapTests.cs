using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Tests.EditorTests.GridNavTests
{
    public class GridMapTests
    {
        private static readonly int2 MinCell = new(-32, -32);
        private static readonly int2 ChunkCount = new(2, 2);

        private GridMap _map;

        [SetUp]
        public void Setup()
        {
            _map = new GridMap(MinCell, ChunkCount, Allocator.Persistent);
        }

        [TearDown]
        public void Teardown()
        {
            _map.Dispose();
        }

        private void Apply(GridEdit edit) => _map.Apply(edit);

        [Test]
        public void NewMap_IsFullyWalkableWithEveryExitAllowed()
        {
            CellData cell = _map.GetCell(int2.zero);

            cell.CostSum.Should().Be(0);
            cell.IsPassable.Should().BeTrue();
            cell.Exits.Should().Be(DirectionUtils.ALL_EXITS);
            cell.IsOneWay.Should().BeFalse();
        }

        [Test]
        public void Bounds_CoverExactlyTheChunks()
        {
            _map.SizeInCells.Should().Be(new int2(64, 64));
            _map.MaxCell.Should().Be(new int2(31, 31));
            _map.InBounds(new int2(-32, -32)).Should().BeTrue();
            _map.InBounds(new int2(31, 31)).Should().BeTrue();
            _map.InBounds(new int2(32, 0)).Should().BeFalse();
            _map.InBounds(new int2(0, -33)).Should().BeFalse();
        }

        [Test]
        public void OutsideTheMap_CountsAsBlocked()
        {
            int2 outside = new(1000, 1000);

            _map.IsPassable(outside).Should().BeFalse();
            _map.GetCell(outside).CostSum.Should().Be(CellData.BLOCKED);
            _map.CanTraverse(new int2(31, 0), Direction.East).Should().BeFalse("the neighbour is outside the map");
        }

        [Test]
        public void EditOutsideTheMap_IsDroppedInsteadOfWrappingOntoAnotherCell()
        {
            Apply(GridEdit.CostDelta(new int2(1000, 1000), 10));
            Apply(GridEdit.CostDelta(new int2(-1000, 0), 10));

            for (int y = 0; y < ChunkCount.y; y++)
            {
                for (int x = 0; x < ChunkCount.x; x++)
                {
                    _map.GetChunkVersions(new int2(x, y)).CostVersion.Should().Be(0, "nothing was written");
                }
            }
        }

        [Test]
        public void CellIndex_IsContiguousInsideAChunkAndUniqueAcrossChunks()
        {
            _map.CellIndex(new int2(1, 0)).Should().Be(_map.CellIndex(int2.zero) + 1);
            _map.CellIndex(new int2(0, 1)).Should().Be(_map.CellIndex(int2.zero) + GridMap.CHUNK_SIZE);

            // (-1, 0) is the last column of the chunk to the left, so it must not be adjacent to (0, 0).
            _map.CellIndex(new int2(-1, 0)).Should().NotBe(_map.CellIndex(int2.zero) - 1);

            using var seen = new NativeHashSet<int>(_map.CellCount, Allocator.Temp);
            for (int y = MinCell.y; y <= _map.MaxCell.y; y++)
            {
                for (int x = MinCell.x; x <= _map.MaxCell.x; x++)
                {
                    int index = _map.CellIndex(new int2(x, y));
                    index.Should().BeInRange(0, _map.CellCount - 1);
                    seen.Add(index).Should().BeTrue($"cell ({x}, {y}) must map to its own index");
                }
            }
        }

        [Test]
        public void CostSum_IsExactSoRemovingOneOfThreeBlockersKeepsTheCellBlocked()
        {
            int2 cell = new(3, 4);

            for (int i = 0; i < 3; i++)
            {
                Apply(GridEdit.CostDelta(cell, CellData.BLOCKED));
            }

            _map.GetCost(cell).Should().Be(3 * CellData.BLOCKED, "a byte would have saturated here");
            _map.IsPassable(cell).Should().BeFalse();

            Apply(GridEdit.CostDelta(cell, -CellData.BLOCKED));
            _map.IsPassable(cell).Should().BeFalse("two blockers are still standing on the cell");

            Apply(GridEdit.CostDelta(cell, -CellData.BLOCKED));
            Apply(GridEdit.CostDelta(cell, -CellData.BLOCKED));
            _map.GetCost(cell).Should().Be(0);
            _map.IsPassable(cell).Should().BeTrue();
        }

        [Test]
        public void BlockedThreshold_IsInclusive()
        {
            int2 cell = new(1, 1);

            Apply(GridEdit.CostDelta(cell, CellData.BLOCKED - 1));
            _map.IsPassable(cell).Should().BeTrue();

            Apply(GridEdit.CostDelta(cell, 1));
            _map.IsPassable(cell).Should().BeFalse();
        }

        [Test]
        public void CostChange_BumpsCostVersion_AndPassabilityOnlyWhenTheThresholdIsCrossed()
        {
            int2 cell = new(5, 5);
            int2 chunk = _map.ChunkCoordOf(cell);
            ChunkVersions before = _map.GetChunkVersions(chunk);

            Apply(GridEdit.CostDelta(cell, 10));

            ChunkVersions afterCheapEdit = _map.GetChunkVersions(chunk);
            afterCheapEdit.CostVersion.Should().Be(before.CostVersion + 1);
            afterCheapEdit.PassabilityVersion.Should().Be(before.PassabilityVersion,
                "harvesting churns cost and must not rebuild the gate graph");

            Apply(GridEdit.CostDelta(cell, CellData.BLOCKED));

            ChunkVersions afterBlocking = _map.GetChunkVersions(chunk);
            afterBlocking.CostVersion.Should().Be(afterCheapEdit.CostVersion + 1);
            afterBlocking.PassabilityVersion.Should().Be(afterCheapEdit.PassabilityVersion + 1);
        }

        [Test]
        public void EditThatChangesNothing_BumpsNoVersion()
        {
            int2 cell = new(5, 5);
            ChunkVersions before = _map.GetChunkVersions(_map.ChunkCoordOf(cell));

            Apply(GridEdit.CostDelta(cell, 0));
            Apply(GridEdit.SetExits(cell, DirectionUtils.ALL_EXITS));

            _map.GetChunkVersions(_map.ChunkCoordOf(cell)).Should().Be(before);
        }

        [Test]
        public void BlockingABorderCell_BumpsTheNeighbourChunkToo()
        {
            int2 borderCell = new(-1, 0); // last column of the lower-left chunk
            int2 ownChunk = _map.ChunkCoordOf(borderCell);
            int2 neighbourChunk = _map.ChunkCoordOf(new int2(0, 0));
            neighbourChunk.Should().NotBe(ownChunk);

            uint neighbourBefore = _map.GetChunkVersions(neighbourChunk).PassabilityVersion;

            Apply(GridEdit.CostDelta(borderCell, CellData.BLOCKED));

            _map.GetChunkVersions(ownChunk).PassabilityVersion.Should().Be(1);
            _map.GetChunkVersions(neighbourChunk).PassabilityVersion.Should().Be(neighbourBefore + 1,
                "the border pair belongs to both chunks' gates");
        }

        [Test]
        public void BlockingAnInteriorCell_LeavesOtherChunksAlone()
        {
            Apply(GridEdit.CostDelta(new int2(5, 5), CellData.BLOCKED));

            _map.GetChunkVersions(new int2(0, 0)).PassabilityVersion.Should().Be(0);
            _map.GetChunkVersions(new int2(0, 1)).PassabilityVersion.Should().Be(0);
            _map.GetChunkVersions(new int2(1, 0)).PassabilityVersion.Should().Be(0);
        }

        [Test]
        public void Flags_AreAddedAndRemovedWithoutTouchingRouting()
        {
            int2 cell = new(2, 2);
            ChunkVersions before = _map.GetChunkVersions(_map.ChunkCoordOf(cell));

            Apply(GridEdit.AddFlags(cell, CellFlags.Road | CellFlags.NoIdle));
            _map.GetFlags(cell).Should().Be(CellFlags.Road | CellFlags.NoIdle);

            Apply(GridEdit.RemoveFlags(cell, CellFlags.NoIdle));
            _map.GetFlags(cell).Should().Be(CellFlags.Road);

            _map.GetChunkVersions(_map.ChunkCoordOf(cell)).Should().Be(before,
                "flags annotate a cell, they do not change how it is routed through");
        }

        [Test]
        public void OneWayExit_BlocksTheForbiddenDirectionOnly()
        {
            int2 cell = new(0, 0);

            Apply(GridEdit.SetExits(cell, DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, Direction.North)));

            _map.CanTraverse(cell, Direction.North).Should().BeFalse();
            _map.CanTraverse(cell, Direction.South).Should().BeTrue();
            _map.CanTraverse(cell, Direction.East).Should().BeTrue();
            _map.CanTraverse(cell, Direction.West).Should().BeTrue();
        }

        [Test]
        public void OneWayExit_IsCheckedOnTheMoverWhenExpandingBackwards()
        {
            // A road that may only be walked northwards: the cell below may leave north, the cell above may
            // not come back south.
            int2 south = new(0, 0);
            int2 north = new(0, 1);
            Apply(GridEdit.SetExits(north, DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, Direction.South)));

            _map.CanTraverse(south, Direction.North).Should().BeTrue();
            _map.CanTraverse(north, Direction.South).Should().BeFalse();

            // A flow field expands backwards from its goal, so reaching `north` from `south` is relaxed while
            // standing on `north` and looking south. Testing north's own exit bit there would wrongly reject it.
            _map.CanTraverseFromNeighbour(north, Direction.South).Should().BeTrue(
                "the mover is the southern cell, and it is allowed to go north");
            _map.CanTraverseFromNeighbour(south, Direction.North).Should().BeFalse(
                "the mover is the northern cell, and it is not allowed to come back south");
        }

        [Test]
        public void ExitChange_BumpsPassabilityBecauseGatesDependOnIt()
        {
            int2 cell = new(7, 7);
            int2 chunk = _map.ChunkCoordOf(cell);

            Apply(GridEdit.SetExits(cell, DirectionUtils.Bit(Direction.North)));

            ChunkVersions versions = _map.GetChunkVersions(chunk);
            versions.PassabilityVersion.Should().Be(1);
            versions.CostVersion.Should().Be(1);
        }

        [Test]
        public void BlockedCell_CannotBeTraversedInAnyDirection()
        {
            int2 cell = new(4, 4);
            Apply(GridEdit.CostDelta(cell, CellData.BLOCKED));

            for (int i = 0; i < DirectionUtils.DIRECTION_COUNT; i++)
            {
                var direction = (Direction)i;
                _map.CanTraverse(cell, direction).Should().BeFalse("a blocked cell cannot be left");
                _map.CanTraverseFromNeighbour(cell, direction).Should().BeFalse("a blocked cell cannot be entered");
            }
        }
    }
}
