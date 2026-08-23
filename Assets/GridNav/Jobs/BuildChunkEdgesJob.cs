using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Works out what it costs to walk between the gates of one chunk, without leaving it (design §4.1).
    ///
    /// One search per gate over the chunk's 1024 cells, which is what makes the coarse graph honest: the
    /// cost of an edge is a real path, and a gate pair with no path inside the chunk gets no edge at all.
    /// The search follows exits, so P -> Q and Q -> P are computed separately and may differ.
    /// </summary>
    [BurstCompile]
    public struct BuildChunkEdgesJob : IJobParallelFor
    {
        public GridMap Map;

        [ReadOnly] public NativeArray<int> DirtyChunks;

        public ChunkGateGraph Graph;

        public void Execute(int index)
        {
            int chunkIndex = DirtyChunks[index];
            int2 chunkCoord = Graph.ChunkCoordOf(chunkIndex);

            int touching = CollectTouchingGates(chunkIndex, chunkCoord);
            Graph.SetTouchingCount(chunkIndex, touching);

            if (touching > 0)
            {
                BuildEdges(chunkIndex, chunkCoord, touching);
            }

            Graph.SetBuiltVersion(chunkIndex, Map.GetChunkVersions(chunkCoord).PassabilityVersion);
        }

        /// <summary>
        /// The gates on this chunk's own two borders and its own outgoing links, plus those the west and south
        /// neighbours own on the borders they share with it, plus the links that *land* here.
        ///
        /// The last group is the one that cannot be found by looking at a fixed set of neighbours, because a
        /// bridge may arrive from any chunk on the map, so the link table is walked instead. It has to be
        /// collected: a chunk that only ever sees the gates on its own borders would report no way from the far
        /// bank of a bridge to anywhere else, which is a route the coarse layer would then refuse to plan.
        /// </summary>
        private int CollectTouchingGates(int chunkIndex, int2 chunkCoord)
        {
            int count = AddGatesOf(chunkIndex, chunkIndex, GateBorder.East, 0);
            count = AddGatesOf(chunkIndex, chunkIndex, GateBorder.North, count);
            count = AddGatesOf(chunkIndex, chunkIndex, GateBorder.Link, count);

            int2 west = chunkCoord + new int2(-1, 0);
            if (Graph.ChunkInBounds(west))
            {
                count = AddGatesOf(chunkIndex, Graph.ChunkIndex(west), GateBorder.East, count);
            }

            int2 south = chunkCoord + new int2(0, -1);
            if (Graph.ChunkInBounds(south))
            {
                count = AddGatesOf(chunkIndex, Graph.ChunkIndex(south), GateBorder.North, count);
            }

            return AddLinksLandingHere(chunkIndex, chunkCoord, count);
        }

        /// <summary>
        /// The link gates owned elsewhere whose far bank is in this chunk. Matched on the pair of cells rather
        /// than on a slot number, because the owner numbers its own link slots and nothing outside it can know
        /// which one a given bridge got.
        /// </summary>
        private int AddLinksLandingHere(int chunkIndex, int2 chunkCoord, int count)
        {
            for (int i = 0; i < Map.LinkSlotCount; i++)
            {
                NavLink link = Map.GetLink(i);
                if (!link.IsValid || !Map.ChunkCoordOf(link.To).Equals(chunkCoord))
                {
                    continue;
                }

                int2 ownerCoord = Map.ChunkCoordOf(link.From);
                if (ownerCoord.Equals(chunkCoord))
                {
                    continue; // its own chunk; already collected, or priced by the chunk search
                }

                if (count >= ChunkGateGraph.MAX_GATES_TOUCHING_CHUNK)
                {
                    break;
                }

                int owner = Graph.ChunkIndex(ownerCoord);
                for (int slot = 0; slot < ChunkGateGraph.MAX_LINK_GATES_PER_CHUNK; slot++)
                {
                    int gateIndex = Graph.GateIndex(owner, GateBorder.Link, slot);
                    ChunkGate gate = Graph.GetGate(gateIndex);

                    if (!gate.IsValid || !gate.CellA.Equals(link.From) || !gate.CellB.Equals(link.To))
                    {
                        continue;
                    }

                    Graph.SetTouching(chunkIndex, count, gateIndex);
                    count++;
                    break;
                }
            }

            return count;
        }

        private int AddGatesOf(int chunkIndex, int ownerChunk, GateBorder border, int count)
        {
            int slots = ChunkGateGraph.SlotsOf(border);

            for (int slot = 0; slot < slots; slot++)
            {
                if (count >= ChunkGateGraph.MAX_GATES_TOUCHING_CHUNK)
                {
                    break;
                }

                int gateIndex = Graph.GateIndex(ownerChunk, border, slot);
                if (!Graph.GetGate(gateIndex).IsValid)
                {
                    continue;
                }

                Graph.SetTouching(chunkIndex, count, gateIndex);
                count++;
            }

            return count;
        }

        private void BuildEdges(int chunkIndex, int2 chunkCoord, int touching)
        {
            int2 chunkMin = Map.ChunkMinCell(chunkCoord);
            var distance = new NativeArray<int>(GridMap.CELLS_PER_CHUNK, Allocator.Temp);

            for (int from = 0; from < touching; from++)
            {
                int2 start = Graph.CellInChunk(Graph.TouchingGate(chunkIndex, from), chunkIndex);
                ChunkSearch.CostsFrom(Map, chunkMin, start, distance);

                for (int to = 0; to < touching; to++)
                {
                    if (to == from)
                    {
                        Graph.SetEdgeCost(chunkIndex, from, to, 0);
                        continue;
                    }

                    int2 target = Graph.CellInChunk(Graph.TouchingGate(chunkIndex, to), chunkIndex);
                    int cost = distance[ChunkSearch.LocalIndexOf(chunkMin, target)];

                    Graph.SetEdgeCost(chunkIndex, from, to, cost >= ChunkGateGraph.UNREACHABLE
                        ? ChunkGateGraph.UNREACHABLE
                        : (ushort)cost);
                }
            }

            distance.Dispose();
        }
    }
}
