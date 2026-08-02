using GridNav;
using Unity.Burst;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Asks for the flow fields the walking agents need (design §13.3 #14).
    ///
    /// Asking is all it does. A field requested now is built during the next frame's navigation phase and
    /// read the frame after, which is the one-frame latency of §13.2 invariant 3 - invisible in play, and it
    /// removes every sync point between field generation and steering. An agent whose field is not ready
    /// simply stands still for a frame.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(PathRouteSystem))]
    public partial struct PathRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FlowFieldCache>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            FlowFieldCache cache = SystemAPI.GetSingletonRW<FlowFieldCache>().ValueRW;

            foreach (RefRO<PathFollow> path in SystemAPI.Query<RefRO<PathFollow>>())
            {
                cache.Request(path.ValueRO.WaypointCell);
            }
        }
    }
}
