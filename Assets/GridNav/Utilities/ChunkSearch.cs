using System;
using CustomNativeCollections;
using Unity.Collections;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Dijkstra over the 1024 cells of one chunk, never leaving it. Used to price the gate graph's
    /// intra-chunk edges and to join a start or goal cell to the gates of its own chunk (design §4.1).
    /// </summary>
    public static class ChunkSearch
    {
        public const int UNREACHED = int.MaxValue;

        /// <summary>A cell can only be improved once per incoming direction, so pushes cannot overflow this.</summary>
        private const int QUEUE_CAPACITY = GridMap.CELLS_PER_CHUNK * DirectionUtils.DIRECTION_COUNT + 1;

        /// <summary>
        /// Cost of walking from <paramref name="source"/> to every cell of the chunk.
        /// </summary>
        public static void CostsFrom(in GridMap map, int2 chunkMin, int2 source, NativeArray<int> distance) =>
            Search(map, chunkMin, source, distance, false);

        /// <summary>
        /// Cost of walking to <paramref name="target"/> from every cell of the chunk - the same search with
        /// every edge reversed, which is why it tests the *neighbour's* exit bit. Getting that backwards
        /// produces routes that run the wrong way down a one-way road, and nothing complains (§3).
        /// </summary>
        public static void CostsTo(in GridMap map, int2 chunkMin, int2 target, NativeArray<int> distance) =>
            Search(map, chunkMin, target, distance, true);

        public static int LocalIndexOf(int2 chunkMin, int2 cell)
        {
            int2 local = cell - chunkMin;
            return local.y * GridMap.CHUNK_SIZE + local.x;
        }

        public static bool IsInside(int2 chunkMin, int2 cell)
        {
            int2 local = cell - chunkMin;
            return math.all(local >= 0) && math.all(local < GridMap.CHUNK_SIZE);
        }

        private static void Search(in GridMap map, int2 chunkMin, int2 origin, NativeArray<int> distance, bool reverse)
        {
            for (int i = 0; i < distance.Length; i++)
            {
                distance[i] = UNREACHED;
            }

            if (!IsInside(chunkMin, origin) || !map.IsPassable(origin))
            {
                return;
            }

            var queue = new NativePriorityQueue<CellNode>(QUEUE_CAPACITY, Allocator.Temp);

            int originLocal = LocalIndexOf(chunkMin, origin);
            distance[originLocal] = 0;
            queue.Enqueue(new CellNode { LocalIndex = originLocal, Cost = 0 });

            while (queue.Count > 0)
            {
                CellNode node = queue.Dequeue();
                if (node.Cost > distance[node.LocalIndex])
                {
                    continue; // a better route to this cell turned up after it was queued
                }

                int2 cell = chunkMin + new int2(
                    node.LocalIndex % GridMap.CHUNK_SIZE,
                    node.LocalIndex / GridMap.CHUNK_SIZE
                );

                for (int d = 0; d < DirectionUtils.DIRECTION_COUNT; d++)
                {
                    var direction = (Direction)d;
                    int2 next = cell + DirectionUtils.Offset(direction);

                    if (!IsInside(chunkMin, next))
                    {
                        continue;
                    }

                    // Forwards, the step is cell -> next and costs what it costs to enter next. Backwards,
                    // the real step is next -> cell, so the neighbour's exit bit decides and the cost is
                    // that of entering cell.
                    bool walkable = reverse
                        ? map.CanTraverseFromNeighbour(cell, direction)
                        : map.CanTraverse(cell, direction);

                    if (!walkable)
                    {
                        continue;
                    }

                    int cost = node.Cost + NavCost.OfCell(map.GetCost(reverse ? cell : next));
                    int nextLocal = LocalIndexOf(chunkMin, next);
                    if (cost >= distance[nextLocal])
                    {
                        continue;
                    }

                    distance[nextLocal] = cost;
                    queue.Enqueue(new CellNode { LocalIndex = nextLocal, Cost = cost });
                }
            }

            queue.Dispose();
        }

        private struct CellNode : IComparable<CellNode>
        {
            public int LocalIndex;
            public int Cost;

            public int CompareTo(CellNode other) => Cost.CompareTo(other.Cost);
        }
    }
}
