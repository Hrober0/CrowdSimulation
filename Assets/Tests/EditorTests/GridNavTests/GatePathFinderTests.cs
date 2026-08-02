using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Tests.EditorTests.GridNavTests
{
    /// <summary>
    /// A 128x128 map, so 4x4 chunks of 32 cells covering (-64,-64) to (63,63).
    /// </summary>
    public class GatePathFinderTests
    {
        private const int MAP_SIZE = 128;

        private GridNavTestWorld _world;
        private NativeList<int> _gatePath;

        [SetUp]
        public void Setup()
        {
            _world = new GridNavTestWorld(MAP_SIZE);
            _gatePath = new NativeList<int>(16, Allocator.Persistent);
        }

        [TearDown]
        public void Teardown()
        {
            _gatePath.Dispose();
            _world.Dispose();
        }

        private bool FindPath(int2 start, int2 goal) =>
            GatePathFinder.TryFindGatePath(_world.Map, _world.Graph, start, goal, _gatePath);

        private void BlockColumn(int x)
        {
            for (int y = -64; y <= 63; y++)
            {
                _world.Enqueue(GridEdit.CostDelta(new int2(x, y), CellData.BLOCKED));
            }
        }

        [Test]
        public void WithinOneChunk_NoGatesAreNeeded()
        {
            _world.Tick();

            FindPath(new int2(-60, -60), new int2(-40, -40)).Should().BeTrue();
            _gatePath.Length.Should().Be(0, "the flow field covers the last stretch on its own");
        }

        [Test]
        public void BetweenNeighbouringChunks_ExactlyOneGateIsCrossed()
        {
            _world.Tick();

            FindPath(new int2(-10, 0), new int2(10, 0)).Should().BeTrue();

            _gatePath.Length.Should().Be(1);

            int gate = _gatePath[0];
            int startChunk = _world.Graph.ChunkIndex(_world.Map.ChunkCoordOf(new int2(-10, 0)));
            int goalChunk = _world.Graph.ChunkIndex(_world.Map.ChunkCoordOf(new int2(10, 0)));
            _world.Graph.OtherChunkOf(gate, startChunk).Should().Be(goalChunk);
        }

        [Test]
        public void AcrossTheMap_TheChainIsAsShortAsTheChunkGridAllows()
        {
            _world.Tick();

            FindPath(new int2(-50, -50), new int2(50, 50)).Should().BeTrue();

            // Corner to corner of a 4x4 chunk grid: three chunk steps in each axis, and every gate crossing
            // is worth exactly one of them.
            _gatePath.Length.Should().Be(6);
        }

        [Test]
        public void ThePathEndsInTheGoalChunk()
        {
            _world.Tick();

            FindPath(new int2(-50, -50), new int2(50, 50)).Should().BeTrue();

            int lastGate = _gatePath[_gatePath.Length - 1];
            int goalChunk = _world.Graph.ChunkIndex(_world.Map.ChunkCoordOf(new int2(50, 50)));

            bool endsAtGoal = _world.Graph.GateOwnerChunk(lastGate) == goalChunk
                              || _world.Graph.GateNeighbourChunk(lastGate) == goalChunk;

            endsAtGoal.Should().BeTrue();
        }

        [Test]
        public void EveryGateInTheChain_LeadsIntoTheNextOnesChunk()
        {
            _world.Tick();

            FindPath(new int2(-50, -50), new int2(50, 50)).Should().BeTrue();

            int chunk = _world.Graph.ChunkIndex(_world.Map.ChunkCoordOf(new int2(-50, -50)));
            for (int i = 0; i < _gatePath.Length; i++)
            {
                int gate = _gatePath[i];
                _world.Graph.CanCrossFrom(gate, chunk).Should().BeTrue($"gate {i} has to be crossable from the chunk the agent is in");
                chunk = _world.Graph.OtherChunkOf(gate, chunk);
            }

            chunk.Should().Be(_world.Graph.ChunkIndex(_world.Map.ChunkCoordOf(new int2(50, 50))));
        }

        [Test]
        public void AWallSplittingTheMap_MeansNoPath()
        {
            BlockColumn(0);
            _world.Tick();

            FindPath(new int2(-10, 0), new int2(10, 0)).Should().BeFalse();
        }

        [Test]
        public void AOneWayWall_IsWalkableOneWayOnly()
        {
            // The column at x = 0 may be entered from the west but never left westwards.
            for (int y = -64; y <= 63; y++)
            {
                _world.Enqueue(GridEdit.SetExits(
                    new int2(0, y),
                    DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, Direction.West)
                ));
            }

            _world.Tick();

            FindPath(new int2(-10, 0), new int2(10, 0)).Should().BeTrue("eastwards is still open");
            FindPath(new int2(10, 0), new int2(-10, 0)).Should().BeFalse("nothing may step back west across that line");
        }

        [Test]
        public void ADetourAroundAWall_IsFoundWhenOneExists()
        {
            // A wall with a gap at the top, so the route has to go around instead of straight through.
            for (int y = -64; y <= 40; y++)
            {
                _world.Enqueue(GridEdit.CostDelta(new int2(0, y), CellData.BLOCKED));
            }

            _world.Tick();

            FindPath(new int2(-10, -50), new int2(10, -50)).Should().BeTrue();
            _gatePath.Length.Should().BeGreaterThan(1, "going around costs more chunk crossings than going straight through");
        }

        [Test]
        public void AnUnreachableGoal_IsReportedRatherThanApproximated()
        {
            // Seal one cell in behind blocked neighbours.
            int2 prison = new(20, 20);
            _world.Enqueue(GridEdit.CostDelta(prison + new int2(1, 0), CellData.BLOCKED));
            _world.Enqueue(GridEdit.CostDelta(prison + new int2(-1, 0), CellData.BLOCKED));
            _world.Enqueue(GridEdit.CostDelta(prison + new int2(0, 1), CellData.BLOCKED));
            _world.Enqueue(GridEdit.CostDelta(prison + new int2(0, -1), CellData.BLOCKED));

            _world.Tick();

            FindPath(new int2(-50, -50), prison).Should().BeFalse();
            _gatePath.Length.Should().Be(0);
        }

        [Test]
        public void AStartOnABlockedCell_HasNoPath()
        {
            int2 blocked = new(-50, -50);
            _world.Enqueue(GridEdit.CostDelta(blocked, CellData.BLOCKED));
            _world.Tick();

            FindPath(blocked, new int2(50, 50)).Should().BeFalse();
        }
    }
}
