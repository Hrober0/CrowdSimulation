using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Examples.Storage
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
    public partial struct HolderMovementSystem : ISystem
    {
        private const float ArrivalThreshold = 0.15f;
 
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<HolderComponent>();
        }
 
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI
                      .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                      .CreateCommandBuffer(state.WorldUnmanaged)
                      .AsParallelWriter();
 
            new MoveJob
            {
                DT  = SystemAPI.Time.DeltaTime,
                ECB = ecb,
            }.ScheduleParallel();
        }
 
        [BurstCompile]
        private partial struct MoveJob : IJobEntity
        {
            public float DT;
            public EntityCommandBuffer.ParallelWriter ECB;
 
            private void Execute(
                [ChunkIndexInQuery] int chunkIndex,
                Entity entity,
                ref LocalTransform transform,
                in HolderComponent holder)
            {
                if (holder.State is not (HolderState.MovingToSourceInput
                                      or HolderState.ExitingSource
                                      or HolderState.MovingToDestInput
                                      or HolderState.ExitingDest))
                    return;
 
                float3 dir  = holder.TargetPos - transform.Position;
                float  dist = math.length(dir);
 
                if (dist <= ArrivalThreshold)
                {
                    // Enable ArrivalTag — IEnableableComponent, no structural change.
                    // ResourceTransferSystem reads and disables it next frame (before movement).
                    ECB.SetComponentEnabled<ArrivalTag>(chunkIndex, entity, true);
                    return;
                }
 
                transform.Position += math.normalize(dir) * holder.MoveSpeed * DT;
 
                if (dist > 0.01f)
                {
                    transform.Rotation = quaternion.LookRotationSafe(
                        new float3(dir.x, 0f, dir.z), math.up());
                }
            }
        }
    }
}