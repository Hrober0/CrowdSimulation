using System;
using CustomNativeCollections;
using Unity.Collections;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// The long-range tier (design §4): A* over the chunk gate graph. It answers "which gates do I go
    /// through", not "which cells do I step on" - the crowded last stretch is the flow field's job, and the
    /// step-by-step walking is the local tier's.
    ///
    /// A search state is a gate *plus the side you came out on*, because crossing a gate from chunk A leaves
    /// you somewhere different than crossing it from chunk B, and the two have different onward costs. With
    /// one-way exits they can also differ in whether they exist at all.
    /// </summary>
    public static class GatePathFinder
    {
        /// <summary>
        /// Fills <paramref name="gatePath"/> with the gates to cross, in order. An empty path with
        /// <c>true</c> means start and goal share a chunk and no gate is involved.
        /// </summary>
        public static bool TryFindGatePath(in GridMap map, in ChunkGateGraph graph,
                                           int2 startCell, int2 goalCell, NativeList<int> gatePath)
        {
            gatePath.Clear();

            if (!map.IsPassable(startCell) || !map.IsPassable(goalCell) || !graph.IsCreated)
            {
                return false;
            }

            int2 startChunkCoord = map.ChunkCoordOf(startCell);
            int2 goalChunkCoord = map.ChunkCoordOf(goalCell);
            int startChunk = graph.ChunkIndex(startChunkCoord);
            int goalChunk = graph.ChunkIndex(goalChunkCoord);

            int2 goalChunkMin = map.ChunkMinCell(goalChunkCoord);
            var goalCosts = new NativeArray<int>(GridMap.CELLS_PER_CHUNK, Allocator.Temp);
            ChunkSearch.CostsTo(map, goalChunkMin, goalCell, goalCosts);

            // Both ends in one chunk with a path between them: the flow field can take it from here.
            if (startChunk == goalChunk
                && goalCosts[ChunkSearch.LocalIndexOf(goalChunkMin, startCell)] != ChunkSearch.UNREACHED)
            {
                goalCosts.Dispose();
                return true;
            }

            int2 startChunkMin = map.ChunkMinCell(startChunkCoord);
            var startCosts = new NativeArray<int>(GridMap.CELLS_PER_CHUNK, Allocator.Temp);
            ChunkSearch.CostsFrom(map, startChunkMin, startCell, startCosts);

            var search = new Search(map, graph, goalCell, goalChunk, goalChunkMin, goalCosts);
            search.Seed(startChunk, startChunkMin, startCosts);
            bool found = search.Run(gatePath);

            search.Dispose();
            startCosts.Dispose();
            goalCosts.Dispose();

            return found;
        }

        private struct Search : IDisposable
        {
            private GridMap _map;
            private ChunkGateGraph _graph;

            private readonly int _goalChunk;
            private readonly int2 _goalChunkMin;
            private readonly float2 _goalPoint;
            private NativeArray<int> _goalCosts;

            private NativeArray<int> _gCost;
            private NativeArray<int> _cameFrom;
            private NativeArray<bool> _closed;
            private NativePriorityQueue<GateNode> _queue;
            private readonly int _queueCapacity;

            public Search(in GridMap map, in ChunkGateGraph graph, int2 goalCell, int goalChunk,
                          int2 goalChunkMin, NativeArray<int> goalCosts)
            {
                _map = map;
                _graph = graph;
                _goalChunk = goalChunk;
                _goalChunkMin = goalChunkMin;
                _goalPoint = GridCoords.CellCenter(goalCell);
                _goalCosts = goalCosts;

                int states = graph.GateSlotTotal * 2;
                _gCost = new NativeArray<int>(states, Allocator.Temp);
                _cameFrom = new NativeArray<int>(states, Allocator.Temp);
                _closed = new NativeArray<bool>(states, Allocator.Temp);

                for (int i = 0; i < states; i++)
                {
                    _gCost[i] = int.MaxValue;
                    _cameFrom[i] = -1;
                }

                _queueCapacity = states * 2 + 64;
                _queue = new NativePriorityQueue<GateNode>(_queueCapacity, Allocator.Temp);
            }

            public void Seed(int startChunk, int2 startChunkMin, NativeArray<int> startCosts)
            {
                int touching = _graph.TouchingCount(startChunk);
                for (int i = 0; i < touching; i++)
                {
                    int gate = _graph.TouchingGate(startChunk, i);
                    if (!_graph.CanCrossFrom(gate, startChunk))
                    {
                        continue;
                    }

                    int other = _graph.OtherChunkOf(gate, startChunk);
                    if (other < 0)
                    {
                        continue;
                    }

                    int toGate = startCosts[
                        ChunkSearch.LocalIndexOf(startChunkMin, _graph.CellInChunk(gate, startChunk))
                    ];

                    if (toGate == ChunkSearch.UNREACHED)
                    {
                        continue;
                    }

                    Relax(StateOf(gate, other), toGate + CostOfEntering(gate, other), -1);
                }
            }

            public bool Run(NativeList<int> gatePath)
            {
                int best = int.MaxValue;
                int bestState = -1;

                while (_queue.Count > 0)
                {
                    GateNode node = _queue.Dequeue();
                    if (node.F >= best)
                    {
                        break; // nothing left that could beat the route already found
                    }

                    if (_closed[node.State])
                    {
                        continue;
                    }

                    _closed[node.State] = true;

                    int gate = GateOf(node.State);
                    int chunk = ChunkOf(node.State);
                    int g = _gCost[node.State];

                    if (chunk == _goalChunk)
                    {
                        int finish = _goalCosts[
                            ChunkSearch.LocalIndexOf(_goalChunkMin, _graph.CellInChunk(gate, chunk))
                        ];

                        if (finish != ChunkSearch.UNREACHED && g + finish < best)
                        {
                            best = g + finish;
                            bestState = node.State;
                        }
                    }

                    Expand(node.State, gate, chunk, g);
                }

                if (bestState < 0)
                {
                    return false;
                }

                Reconstruct(bestState, gatePath);
                return true;
            }

            private void Expand(int state, int gate, int chunk, int g)
            {
                int fromLocal = _graph.LocalIndexOf(chunk, gate);
                if (fromLocal < 0)
                {
                    return;
                }

                int touching = _graph.TouchingCount(chunk);
                for (int t = 0; t < touching; t++)
                {
                    int next = _graph.TouchingGate(chunk, t);
                    if (next == gate || !_graph.CanCrossFrom(next, chunk))
                    {
                        continue;
                    }

                    ushort edge = _graph.EdgeCost(chunk, fromLocal, t);
                    if (edge == ChunkGateGraph.UNREACHABLE)
                    {
                        continue;
                    }

                    int other = _graph.OtherChunkOf(next, chunk);
                    if (other < 0)
                    {
                        continue;
                    }

                    Relax(StateOf(next, other), g + edge + CostOfEntering(next, other), state);
                }
            }

            private void Relax(int state, int cost, int from)
            {
                if (cost >= _gCost[state])
                {
                    return;
                }

                _gCost[state] = cost;
                _cameFrom[state] = from;

                if (_queue.Count >= _queueCapacity - 1)
                {
                    // Only reachable if a state is improved far more often than its in-degree allows.
                    // Dropping the entry costs optimality on a graph that shape; throwing would cost the frame.
                    return;
                }

                _queue.Enqueue(new GateNode { State = state, F = cost + Heuristic(state) });
            }

            private void Reconstruct(int bestState, NativeList<int> gatePath)
            {
                for (int state = bestState; state >= 0; state = _cameFrom[state])
                {
                    gatePath.Add(GateOf(state));
                }

                for (int head = 0, tail = gatePath.Length - 1; head < tail; head++, tail--)
                {
                    (gatePath[head], gatePath[tail]) = (gatePath[tail], gatePath[head]);
                }
            }

            /// <summary>Straight-line distance left to walk, at the cheapest a step can possibly be.</summary>
            private int Heuristic(int state)
            {
                float2 point = GridCoords.CellCenter(_graph.CellInChunk(GateOf(state), ChunkOf(state)));
                return (int)(math.distance(point, _goalPoint) * NavCost.STEP);
            }

            private int CostOfEntering(int gate, int chunk) =>
                NavCost.OfCell(_map.GetCost(_graph.CellInChunk(gate, chunk)));

            private int StateOf(int gate, int chunk) =>
                gate * 2 + (chunk == _graph.GateOwnerChunk(gate) ? 0 : 1);

            private static int GateOf(int state) => state >> 1;

            private int ChunkOf(int state)
            {
                int gate = GateOf(state);
                return (state & 1) == 0 ? _graph.GateOwnerChunk(gate) : _graph.GateNeighbourChunk(gate);
            }

            public void Dispose()
            {
                _gCost.Dispose();
                _cameFrom.Dispose();
                _closed.Dispose();
                _queue.Dispose();
            }
        }

        private struct GateNode : IComparable<GateNode>
        {
            public int State;
            public int F;

            public int CompareTo(GateNode other) => F.CompareTo(other.F);
        }
    }
}
