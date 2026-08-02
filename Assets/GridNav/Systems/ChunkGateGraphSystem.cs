using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Keeps the chunk gate graph in step with the grid (design §13.3 #4).
    ///
    /// A chunk is rebuilt when its <c>PassabilityVersion</c> moved. Cost churn - a forest being harvested -
    /// does not touch that counter, so it never reaches here; and because a passability change on a border
    /// bumps the neighbouring chunk too, both sides of a changed border are always rebuilt together.
    /// </summary>
    [UpdateInGroup(typeof(PathfindingGroup), OrderFirst = true)]
    public partial struct ChunkGateGraphSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out ChunkGateGraph graph))
            {
                graph.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;

            if (!SystemAPI.HasSingleton<ChunkGateGraph>())
            {
                state.EntityManager.CreateSingleton(
                    new ChunkGateGraph(map.ChunkCount, Allocator.Persistent),
                    "ChunkGateGraph"
                );
            }

            ChunkGateGraph graph = SystemAPI.GetSingletonRW<ChunkGateGraph>().ValueRW;

            NativeList<int> dirtyChunks = CollectDirtyChunks(map, graph);
            if (dirtyChunks.IsEmpty)
            {
                dirtyChunks.Dispose();
                return;
            }

            NativeArray<int> chunks = dirtyChunks.AsArray();

            state.Dependency = new BuildChunkGatesJob
            {
                Map = map,
                DirtyChunks = chunks,
                Graph = graph,
            }.Schedule(chunks.Length, 1, state.Dependency);

            state.Dependency = new BuildChunkEdgesJob
            {
                Map = map,
                DirtyChunks = chunks,
                Graph = graph,
            }.Schedule(chunks.Length, 1, state.Dependency);

            // Completed before leaving the system, like the grid drain. Both passes carry the GridMap, and
            // ECS cannot sequence a later reader against them because what they touch lives inside a
            // singleton rather than in chunk data. The parallelism that matters - across dirty chunks -
            // is inside the two jobs and is unaffected.
            state.Dependency.Complete();

            dirtyChunks.Dispose();
        }

        private static NativeList<int> CollectDirtyChunks(in GridMap map, in ChunkGateGraph graph)
        {
            var dirty = new NativeList<int>(graph.ChunkTotal, Allocator.TempJob);

            int2 chunkCount = graph.ChunkCount;
            for (int y = 0; y < chunkCount.y; y++)
            {
                for (int x = 0; x < chunkCount.x; x++)
                {
                    int2 coord = new(x, y);
                    int chunkIndex = graph.ChunkIndex(coord);

                    if (graph.NeedsRebuild(chunkIndex, map.GetChunkVersions(coord).PassabilityVersion))
                    {
                        dirty.Add(chunkIndex);
                    }
                }
            }

            return dirty;
        }
    }
}
