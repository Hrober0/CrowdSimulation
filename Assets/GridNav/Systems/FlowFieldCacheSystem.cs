using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Serves the flow field requests posted since the last frame, building at most a handful of fields
    /// (design §13.3 #5).
    ///
    /// The cap is the point: a burst of new destinations spreads over several frames instead of spiking one.
    /// A request that does not make the cut is simply not served this frame - agents ask again next frame,
    /// and asking is free.
    /// </summary>
    [UpdateInGroup(typeof(PathfindingGroup))]
    [UpdateAfter(typeof(ChunkGateGraphSystem))]
    public partial struct FlowFieldCacheSystem : ISystem
    {
        private const int MAX_BUILDS_PER_FRAME = 4;

        public void OnCreate(ref SystemState state)
        {
            // Created up front rather than on first update: the cache needs nothing from the map, and
            // anything wanting to post a request has to find it there from the very first frame.
            state.EntityManager.CreateSingleton(new FlowFieldCache(Allocator.Persistent), "FlowFieldCache");

            state.RequireForUpdate<GridWorld>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out FlowFieldCache cache))
            {
                cache.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;
            FlowFieldCache cache = SystemAPI.GetSingletonRW<FlowFieldCache>().ValueRW;
            cache.Tick();

            NativeList<int> toBuild = TakeRequests(ref cache, map);
            cache.Requests.Clear();

            if (toBuild.IsEmpty)
            {
                toBuild.Dispose();
                return;
            }

            NativeArray<int> slots = toBuild.AsArray();

            state.Dependency = new BuildFlowFieldJob
            {
                Map = map,
                Slots = slots,
                Storage = cache.Storage,
            }.Schedule(slots.Length, 1, state.Dependency);

            // Completed here for the same reason the grid drain is: what the job writes lives inside the
            // singleton, so ECS does not sequence later readers against it. The one-frame latency of
            // §13.2 invariant 3 comes from requests being served the frame *after* they are posted, not
            // from leaving the build in flight.
            state.Dependency.Complete();

            toBuild.Dispose();
        }

        private static NativeList<int> TakeRequests(ref FlowFieldCache cache, in GridMap map)
        {
            var toBuild = new NativeList<int>(MAX_BUILDS_PER_FRAME, Allocator.TempJob);

            foreach (int2 goalCell in cache.Requests)
            {
                if (cache.TryGetSlot(goalCell, out int slot) && cache.IsFresh(slot, map))
                {
                    cache.MarkUsed(slot); // asking for a field is what keeps it from being evicted
                    continue;
                }

                if (toBuild.Length >= MAX_BUILDS_PER_FRAME)
                {
                    continue;
                }

                // No slot free means every field in the cache is in use this frame. The request is simply not
                // served - the same outcome as missing the build cap above, and agents ask again next frame.
                if (!cache.TryAcquireSlot(goalCell, map, out int acquired))
                {
                    continue;
                }

                cache.MarkUsed(acquired);
                toBuild.Add(acquired);
            }

            return toBuild;
        }
    }
}
