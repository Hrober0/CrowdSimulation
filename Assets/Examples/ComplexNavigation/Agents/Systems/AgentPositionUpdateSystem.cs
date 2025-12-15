using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ComplexNavigation
{
    [BurstCompile]
    [UpdateAfter(typeof(AgentVelocityCalculationSystem))]
    partial struct AgentPositionUpdateSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new PositionUpdateJob
                {
                    DeltaTime = SystemAPI.Time.DeltaTime,
                }
                .ScheduleParallel();
        }

        [BurstCompile]
        public partial struct PositionUpdateJob : IJobEntity
        {
            public float DeltaTime;

            public void Execute(
                ref LocalTransform localTransform,
                ref AgentCoreData coreData,
                in AgentMovementData movementData)
            {
                localTransform.Position += math.float3(movementData.MovementSpeed * DeltaTime * coreData.Velocity, 0);

                localTransform.Rotation = math.slerp(
                    localTransform.Rotation,
                    quaternion.RotateZ(math.atan2(coreData.Velocity.y, coreData.Velocity.x)),
                    movementData.RotationSpeed * DeltaTime
                );
            }
        }
    }
}