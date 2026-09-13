using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Keeps the chunk gate graphs in step with the grid (design §13.3 #4, §14.4).
    ///
    /// A chunk is rebuilt when the version its gates were built against moved. Cost churn - a forest being
    /// harvested - does not touch passability, so it never reaches here; and because a passability change on
    /// a border bumps the neighbouring chunk too, both sides of a changed border are always rebuilt together.
    ///
    /// **There is a graph per traversal, not one graph.** A gate is an opening between chunks, and whether an
    /// opening exists is a passability question - so a seeker that can knock a wall down sees gates where the
    /// civilian view sees none. One entity per traversal rather than one array of graphs, because a graph
    /// carries native containers and those cannot be nested inside another container.
    ///
    /// Only the views somebody has asked for get built, and the civilian one always exists because nearly
    /// everything walks on it. A graph that breaches also watches the structure counter, so a wall coming
    /// down opens its gates - and leaves every civilian graph untouched (§3.1).
    /// </summary>
    [UpdateInGroup(typeof(PathfindingGroup), OrderFirst = true)]
    public partial struct ChunkGateGraphSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton(new GateGraphRequests(Allocator.Persistent),
                                                "GateGraphRequests");

            state.RequireForUpdate<GridWorld>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out GateGraphRequests requests))
            {
                requests.Dispose();
            }

            foreach (RefRW<ChunkGateGraph> graph in SystemAPI.Query<RefRW<ChunkGateGraph>>())
            {
                graph.ValueRW.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;

            EnsureGraphs(ref state, map);

            foreach (RefRW<ChunkGateGraph> graph in SystemAPI.Query<RefRW<ChunkGateGraph>>())
            {
                Rebuild(ref state, map, graph.ValueRW);
            }
        }

        /// <summary>
        /// Makes sure a graph exists for the civilian view and for everything asked for since the last frame.
        ///
        /// Creating an entity is a structural change, so the requests are read into a list first - and it
        /// happens at most a handful of times in a game, the first time a class of soldier takes the field.
        /// </summary>
        private void EnsureGraphs(ref SystemState state, in GridMap map)
        {
            GateGraphRequests requests = SystemAPI.GetSingleton<GateGraphRequests>();

            var wanted = new NativeList<Traversal>(Traversal.MAX_CLASSES, Allocator.Temp);
            wanted.Add(Traversal.Civilian);

            foreach (Traversal traversal in requests.Wanted)
            {
                wanted.Add(traversal);
            }

            requests.Wanted.Clear();

            foreach (Traversal traversal in wanted)
            {
                if (!HasGraphFor(ref state, traversal))
                {
                    Entity entity = state.EntityManager.CreateEntity();
                    state.EntityManager.AddComponentData(
                        entity, new ChunkGateGraph(map.ChunkCount, Allocator.Persistent, traversal));
                }
            }

            wanted.Dispose();
        }

        private bool HasGraphFor(ref SystemState state, Traversal traversal)
        {
            foreach (RefRO<ChunkGateGraph> graph in SystemAPI.Query<RefRO<ChunkGateGraph>>())
            {
                if (graph.ValueRO.Traversal.Equals(traversal))
                {
                    return true;
                }
            }

            return false;
        }

        private void Rebuild(ref SystemState state, in GridMap map, in ChunkGateGraph graph)
        {
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
            // component's native containers rather than in chunk data. The parallelism that matters - across
            // dirty chunks - is inside the two jobs and is unaffected.
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

                    if (graph.NeedsRebuild(chunkIndex, ChunkGateGraph.VersionOf(map, coord, graph.Traversal)))
                    {
                        dirty.Add(chunkIndex);
                    }
                }
            }

            return dirty;
        }
    }
}
