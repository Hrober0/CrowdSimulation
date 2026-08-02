using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;

namespace GridNav
{
    /// <summary>
    /// The single writer of the grid (design §13.2, invariant 1). Ordered last in
    /// <see cref="GridUpdateGroup"/>, so every producer of the frame has already enqueued.
    ///
    /// It takes <see cref="GridWorld"/> read-write and every reader takes it read-only, which is what makes
    /// the ECS dependency system order the readers behind this write for free.
    /// </summary>
    [UpdateInGroup(typeof(GridUpdateGroup), OrderLast = true)]
    [BurstCompile]
    public partial struct GridApplySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RefRW<GridWorld> gridWorld = SystemAPI.GetSingletonRW<GridWorld>();
            if (gridWorld.ValueRO.Edits.Count == 0)
            {
                return;
            }

            state.Dependency = new ApplyGridEditsJob
            {
                Map = gridWorld.ValueRW.Map,
                Edits = gridWorld.ValueRW.Edits,
            }.Schedule(state.Dependency);

            // Completed here, not left in flight. What the job writes lives inside the singleton rather than
            // in chunk data, so ECS does not sequence later main-thread readers - the gate graph checking
            // chunk versions, a gizmo drawing cells - against it. Completing is what makes "after this group
            // the grid is immutable for the rest of the frame" literally true, and the drain is a handful of
            // edits; there is nothing to gain by deferring it.
            state.Dependency.Complete();
        }
    }
}
