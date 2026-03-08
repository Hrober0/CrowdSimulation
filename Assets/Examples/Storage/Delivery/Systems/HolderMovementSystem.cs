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
                DT = SystemAPI.Time.DeltaTime,
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
                // Idle holders and those already at target don't move
                if (holder.State == HolderState.Idle) return;

                float3 dir = holder.TargetPos - transform.Position;
                float dist = math.length(dir);

                if (dist <= ArrivalThreshold)
                {
                    // Signal arrival — ResourceTransferSystem handles the state change
                    ECB.AddComponent<ArrivalTag>(chunkIndex, entity);
                    return;
                }

                transform.Position += math.normalize(dir) * holder.MoveSpeed * DT;

                // Face movement direction (Y-axis rotation only — flat world)
                if (dist > 0.01f)
                {
                    transform.Rotation = quaternion.LookRotationSafe(
                        new float3(dir.x, 0f, dir.z), math.up());
                }
            }
        }
    }
}