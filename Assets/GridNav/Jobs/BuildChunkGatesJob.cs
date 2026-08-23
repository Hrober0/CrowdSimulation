using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GridNav
{
    /// <summary>
    /// Re-scans the two borders a dirty chunk owns, and the links that leave it, and rewrites its gates
    /// (design §4.1).
    ///
    /// Runs to completion for every dirty chunk before any edges are built: a chunk's edges depend on its
    /// neighbours' gates, and when a border cell changes both chunks are dirty, so the two passes cannot be
    /// interleaved without reading half-rebuilt gates. A link bumps the passability of both of its mouths'
    /// chunks for the same reason a border cell bumps both sides of the border.
    /// </summary>
    [BurstCompile]
    public struct BuildChunkGatesJob : IJobParallelFor
    {
        public GridMap Map;

        [ReadOnly] public NativeArray<int> DirtyChunks;

        public ChunkGateGraph Graph;

        public void Execute(int index)
        {
            int chunkIndex = DirtyChunks[index];

            Graph.ClearGates(chunkIndex);
            ScanBorder(chunkIndex, GateBorder.East);
            ScanBorder(chunkIndex, GateBorder.North);
            ScanLinks(chunkIndex);
        }

        /// <summary>
        /// Turns every link that leaves this chunk into a gate.
        ///
        /// **A link that stays inside its chunk gets no gate**, and that is not an omission. The coarse graph
        /// only ever asks a chunk "what does it cost to walk between your gates", and
        /// <see cref="ChunkSearch"/> already answers that with the crossing taken into account - so an
        /// intra-chunk bridge is priced into the edges of the gates that already exist. Giving it a gate would
        /// make a node whose two sides are the same chunk, which the A* over gate-plus-side you-came-out-on
        /// cannot say anything useful about.
        ///
        /// One way, always: <see cref="GateCrossing.AToB"/> alone, so <c>CanCrossFrom</c> refuses the crossing
        /// from the far bank without anything here having to say so.
        /// </summary>
        private void ScanLinks(int chunkIndex)
        {
            int2 chunkCoord = Graph.ChunkCoordOf(chunkIndex);
            int slot = 0;

            for (int i = 0; i < Map.LinkSlotCount; i++)
            {
                NavLink link = Map.GetLink(i);
                if (!link.IsValid || !Map.ChunkCoordOf(link.From).Equals(chunkCoord))
                {
                    continue;
                }

                int2 farCoord = Map.ChunkCoordOf(link.To);
                if (farCoord.Equals(chunkCoord))
                {
                    continue; // stays home; ChunkSearch prices it into the ordinary edges
                }

                // Both banks have to be stood on. A blocked mouth means the crossing is not available, and
                // leaving the slot empty is how the graph says so.
                if (!Map.IsPassable(link.From) || !Map.IsPassable(link.To))
                {
                    continue;
                }

                if (slot >= ChunkGateGraph.MAX_LINK_GATES_PER_CHUNK)
                {
                    Debug.LogWarning("[GridNav] A chunk has more links leaving it than the gate graph can hold; the extra ones are ignored.");
                    return;
                }

                Graph.SetGate(Graph.GateIndex(chunkIndex, GateBorder.Link, slot), new ChunkGate
                {
                    CellA = link.From,
                    CellB = link.To,
                    Length = 1,
                    Crossing = GateCrossing.AToB,
                    FarChunk = Graph.ChunkIndex(farCoord),
                    CrossCost = link.Cost,
                });

                slot++;
            }
        }

        private void ScanBorder(int chunkIndex, GateBorder border)
        {
            int2 chunkCoord = Graph.ChunkCoordOf(chunkIndex);
            bool east = border == GateBorder.East;

            int2 across = east ? new int2(1, 0) : new int2(0, 1);
            if (!Graph.ChunkInBounds(chunkCoord + across))
            {
                return; // the map ends here, so there is nothing to cross into
            }

            int2 along = east ? new int2(0, 1) : new int2(1, 0);
            int2 first = Map.ChunkMinCell(chunkCoord) + (east
                ? new int2(GridMap.CHUNK_SIZE - 1, 0)
                : new int2(0, GridMap.CHUNK_SIZE - 1));

            Direction outward = east ? Direction.East : Direction.North;
            Direction inward = DirectionUtils.Opposite(outward);

            int slot = 0;
            int runStart = -1;
            GateCrossing runCrossing = GateCrossing.None;

            for (int i = 0; i < GridMap.CHUNK_SIZE; i++)
            {
                int2 a = first + along * i;
                int2 b = a + across;

                GateCrossing crossing = GateCrossing.None;
                if (Map.CanTraverse(a, outward))
                {
                    crossing |= GateCrossing.AToB;
                }

                if (Map.CanTraverse(b, inward))
                {
                    crossing |= GateCrossing.BToA;
                }

                if (crossing == runCrossing)
                {
                    continue;
                }

                // A run ends at a closed pair and also where the crossing direction changes, so that no gate
                // is passable one way along part of its span and the other way along the rest.
                slot = CloseRun(chunkIndex, border, slot, first, along, across, runStart, i, runCrossing);

                runCrossing = crossing;
                runStart = crossing == GateCrossing.None ? -1 : i;
            }

            CloseRun(chunkIndex, border, slot, first, along, across, runStart, GridMap.CHUNK_SIZE, runCrossing);
        }

        private int CloseRun(int chunkIndex, GateBorder border, int slot, int2 first, int2 along, int2 across,
                             int runStart, int runEnd, GateCrossing crossing)
        {
            if (crossing == GateCrossing.None || runStart < 0)
            {
                return slot;
            }

            if (slot >= ChunkGateGraph.MAX_GATES_PER_BORDER)
            {
                Debug.LogWarning("[GridNav] A chunk border has more openings than the gate graph can hold; the extra ones are ignored.");
                return slot;
            }

            int middle = (runStart + runEnd - 1) / 2;
            int2 cellA = first + along * middle;

            Graph.SetGate(Graph.GateIndex(chunkIndex, border, slot), new ChunkGate
            {
                CellA = cellA,
                CellB = cellA + across,
                Length = (byte)(runEnd - runStart),
                Crossing = crossing,
            });

            return slot + 1;
        }
    }
}
