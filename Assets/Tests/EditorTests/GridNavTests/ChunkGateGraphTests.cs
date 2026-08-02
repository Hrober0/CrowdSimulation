using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.GridNavTests
{
    /// <summary>
    /// A 64x64 map, so 2x2 chunks. Chunk (0,0) covers cells (-32,-32) to (-1,-1); its east border is the
    /// column x = -1 and its north border the row y = -1.
    /// </summary>
    public class ChunkGateGraphTests
    {
        private const int MAP_SIZE = 64;
        private static readonly int2 LowerLeftChunk = new(0, 0);

        private GridNavTestWorld _world;

        [SetUp]
        public void Setup() => _world = new GridNavTestWorld(MAP_SIZE);

        [TearDown]
        public void Teardown() => _world.Dispose();

        private ChunkGateGraph Graph => _world.Graph;

        private void Tick() => _world.Tick();

        private void Enqueue(GridEdit edit) => _world.Enqueue(edit);

        private int GateCountOn(int chunkIndex, GateBorder border)
        {
            int count = 0;
            for (int slot = 0; slot < ChunkGateGraph.MAX_GATES_PER_BORDER; slot++)
            {
                if (Graph.GetGate(Graph.GateIndex(chunkIndex, border, slot)).IsValid)
                {
                    count++;
                }
            }

            return count;
        }

        private int LocalOf(int chunkIndex, GateBorder border, int slot) =>
            Graph.LocalIndexOf(chunkIndex, Graph.GateIndex(chunkIndex, border, slot));

        [Test]
        public void AnOpenBorder_IsOneGateSpanningAllOfIt()
        {
            Tick();

            int chunk = Graph.ChunkIndex(LowerLeftChunk);
            GateCountOn(chunk, GateBorder.East).Should().Be(1);

            ChunkGate gate = Graph.GetGate(Graph.GateIndex(chunk, GateBorder.East, 0));
            gate.Length.Should().Be(GridMap.CHUNK_SIZE);
            gate.Crossing.Should().Be(GateCrossing.Both);
            gate.CellA.x.Should().Be(-1, "the representative cell sits on the owning chunk's side");
            gate.CellB.x.Should().Be(0);
            gate.CellB.y.Should().Be(gate.CellA.y);
        }

        [Test]
        public void WhereTheMapEnds_ThereAreNoGates()
        {
            Tick();

            int lastColumn = Graph.ChunkIndex(new int2(1, 0));
            GateCountOn(lastColumn, GateBorder.East).Should().Be(0);

            int topRow = Graph.ChunkIndex(new int2(0, 1));
            GateCountOn(topRow, GateBorder.North).Should().Be(0);
        }

        [Test]
        public void AWallAcrossABorder_SplitsItIntoTwoGates()
        {
            // Block the middle of the east border of chunk (0,0).
            for (int y = -20; y <= -12; y++)
            {
                Enqueue(GridEdit.CostDelta(new int2(-1, y), CellData.BLOCKED));
            }

            Tick();

            int chunk = Graph.ChunkIndex(LowerLeftChunk);
            GateCountOn(chunk, GateBorder.East).Should().Be(2);

            ChunkGate below = Graph.GetGate(Graph.GateIndex(chunk, GateBorder.East, 0));
            ChunkGate above = Graph.GetGate(Graph.GateIndex(chunk, GateBorder.East, 1));

            below.Length.Should().Be(12, "cells -32..-21 stay open");
            above.Length.Should().Be(11, "cells -11..-1 stay open");
        }

        [Test]
        public void ABorderBlockedEndToEnd_HasNoGate()
        {
            for (int y = -32; y <= -1; y++)
            {
                Enqueue(GridEdit.CostDelta(new int2(-1, y), CellData.BLOCKED));
            }

            Tick();

            GateCountOn(Graph.ChunkIndex(LowerLeftChunk), GateBorder.East).Should().Be(0);
        }

        [Test]
        public void AOneWayBorder_ProducesAGateThatOnlyCrossesThatWay()
        {
            // Every cell on the east border may leave east, but nothing may come back west.
            for (int y = -32; y <= -1; y++)
            {
                Enqueue(GridEdit.SetExits(new int2(0, y), DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, Direction.West)));
            }

            Tick();

            int owner = Graph.ChunkIndex(LowerLeftChunk);
            int neighbour = Graph.ChunkIndex(new int2(1, 0));
            int gateIndex = Graph.GateIndex(owner, GateBorder.East, 0);

            Graph.GetGate(gateIndex).Crossing.Should().Be(GateCrossing.AToB);
            Graph.CanCrossFrom(gateIndex, owner).Should().BeTrue();
            Graph.CanCrossFrom(gateIndex, neighbour).Should().BeFalse("the coarse graph must not offer a route the agent cannot walk");
        }

        [Test]
        public void ABorderThatChangesDirectionHalfway_BecomesTwoGates()
        {
            for (int y = -32; y <= -17; y++)
            {
                Enqueue(GridEdit.SetExits(new int2(0, y), DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, Direction.West)));
            }

            Tick();

            int chunk = Graph.ChunkIndex(LowerLeftChunk);
            GateCountOn(chunk, GateBorder.East).Should().Be(2);

            Graph.GetGate(Graph.GateIndex(chunk, GateBorder.East, 0)).Crossing.Should().Be(GateCrossing.AToB);
            Graph.GetGate(Graph.GateIndex(chunk, GateBorder.East, 1)).Crossing.Should().Be(GateCrossing.Both);
        }

        [Test]
        public void AChunksGates_AreConnectedByEdgesWorthTheirPathLength()
        {
            Tick();

            int chunk = Graph.ChunkIndex(LowerLeftChunk);
            Graph.TouchingCount(chunk).Should().Be(2, "the lower-left chunk owns an east and a north gate, and has no lower neighbours");

            int east = LocalOf(chunk, GateBorder.East, 0);
            int north = LocalOf(chunk, GateBorder.North, 0);

            // (-1,-17) to (-17,-1) is 16 steps west and 16 north over empty ground.
            Graph.EdgeCost(chunk, east, north).Should().Be(32 * NavCost.STEP);
            Graph.EdgeCost(chunk, north, east).Should().Be(32 * NavCost.STEP);
            Graph.EdgeCost(chunk, east, east).Should().Be(0);
        }

        [Test]
        public void ExpensiveGround_ShowsUpInTheEdgeCost()
        {
            // A band of costly ground across the whole chunk, so every route between its gates crosses it.
            for (int x = -32; x <= -1; x++)
            {
                Enqueue(GridEdit.CostDelta(new int2(x, -9), 5));
            }

            Tick();

            int chunk = Graph.ChunkIndex(LowerLeftChunk);
            int east = LocalOf(chunk, GateBorder.East, 0);
            int north = LocalOf(chunk, GateBorder.North, 0);

            Graph.EdgeCost(chunk, east, north).Should().Be(32 * NavCost.STEP + 5);
        }

        [Test]
        public void GatesWithNoPathBetweenThemInsideTheChunk_HaveNoEdge()
        {
            // A full column just inside the east border cuts that border's gate off from the rest of the chunk.
            for (int y = -32; y <= -1; y++)
            {
                Enqueue(GridEdit.CostDelta(new int2(-2, y), CellData.BLOCKED));
            }

            Tick();

            int chunk = Graph.ChunkIndex(LowerLeftChunk);
            int east = LocalOf(chunk, GateBorder.East, 0);

            // The wall also splits the north border, leaving a one-cell gate at x = -1 that shares the strip
            // with the east gate. Everything on the far side of the wall is what must be cut off.
            int touching = Graph.TouchingCount(chunk);
            int checkedGates = 0;

            for (int other = 0; other < touching; other++)
            {
                int2 otherCell = Graph.CellInChunk(Graph.TouchingGate(chunk, other), chunk);
                if (other == east || otherCell.x >= -1)
                {
                    continue;
                }

                Graph.EdgeCost(chunk, east, other).Should().Be(ChunkGateGraph.UNREACHABLE);
                Graph.EdgeCost(chunk, other, east).Should().Be(ChunkGateGraph.UNREACHABLE);
                checkedGates++;
            }

            checkedGates.Should().BeGreaterThan(0, "the wall has to leave at least one gate on its far side");
        }

        [Test]
        public void AOneWayChunk_ProducesEdgesThatOnlyGoOneWay()
        {
            // Nothing inside the chunk may step west. Reaching the east gate from the north one is then
            // possible - it only needs east and south steps - while the way back is not.
            for (int y = -32; y <= -1; y++)
            {
                for (int x = -32; x <= -1; x++)
                {
                    Enqueue(GridEdit.SetExits(new int2(x, y), DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, Direction.West)));
                }
            }

            Tick();

            int chunk = Graph.ChunkIndex(LowerLeftChunk);
            int east = LocalOf(chunk, GateBorder.East, 0);
            int north = LocalOf(chunk, GateBorder.North, 0);

            Graph.EdgeCost(chunk, north, east).Should().Be(32 * NavCost.STEP);
            Graph.EdgeCost(chunk, east, north).Should().Be(ChunkGateGraph.UNREACHABLE,
                "a symmetric graph here would hand the agent a route it cannot walk");
        }

        [Test]
        public void ChangingTheGridRebuildsTheAffectedChunk()
        {
            Tick();
            GateCountOn(Graph.ChunkIndex(LowerLeftChunk), GateBorder.East).Should().Be(1);

            for (int y = -20; y <= -12; y++)
            {
                Enqueue(GridEdit.CostDelta(new int2(-1, y), CellData.BLOCKED));
            }

            Tick();

            GateCountOn(Graph.ChunkIndex(LowerLeftChunk), GateBorder.East).Should().Be(2);
        }

        [Test]
        public void CostChurnAlone_DoesNotDisturbTheGraph()
        {
            Tick();

            int chunk = Graph.ChunkIndex(LowerLeftChunk);
            int east = LocalOf(chunk, GateBorder.East, 0);
            int north = LocalOf(chunk, GateBorder.North, 0);
            ushort before = Graph.EdgeCost(chunk, east, north);

            // Cost that never crosses the blocked threshold: the gates cannot have moved.
            Enqueue(GridEdit.CostDelta(new int2(-20, -20), 30));
            Tick();

            GateCountOn(chunk, GateBorder.East).Should().Be(1);
            Graph.EdgeCost(chunk, east, north).Should().Be(before,
                "the cell that changed is not on the cheapest route between these gates");
        }

        [Test]
        public void NeighbouringChunks_ShareTheGateBetweenThem()
        {
            Tick();

            int left = Graph.ChunkIndex(LowerLeftChunk);
            int right = Graph.ChunkIndex(new int2(1, 0));
            int gateIndex = Graph.GateIndex(left, GateBorder.East, 0);

            Graph.LocalIndexOf(left, gateIndex).Should().BeGreaterThanOrEqualTo(0);
            Graph.LocalIndexOf(right, gateIndex).Should().BeGreaterThanOrEqualTo(0,
                "the chunk on the far side has to see the gate as one of its own exits");

            Graph.OtherChunkOf(gateIndex, left).Should().Be(right);
            Graph.OtherChunkOf(gateIndex, right).Should().Be(left);

            Graph.CellInChunk(gateIndex, left).x.Should().Be(-1);
            Graph.CellInChunk(gateIndex, right).x.Should().Be(0);
        }
    }
}
