using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ComplexNavigation
{
    [BurstCompile]
    [UpdateAfter(typeof(AgentVelocityCalculationSystem))]
    partial struct AgentMovementSystem : ISystem
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
    }

    [BurstCompile]
    public partial struct PositionUpdateJob : IJobEntity
    {
        public float DeltaTime;

        public void Execute(ref LocalTransform localTransform,
                            ref AgentCoreData coreData,
                            in AgentMovementData movementData,
                            in TargetData targetData)
        {
            float2 moveDirection = targetData.TargetPosition - localTransform.Position.xy;
            coreData.PrefVelocity = math.normalize(moveDirection);

            float2 velocity = coreData.Velocity;
            float magnitude = math.length(velocity);
            float moveThisFrame = movementData.MovementSpeed * DeltaTime;

            // Prevent overshooting the target
            if (magnitude < moveThisFrame)
            {
                return;
            }

            float3 fixedDirection = math.float3(velocity / magnitude, 0);
            localTransform.Position += fixedDirection * moveThisFrame;

            localTransform.Rotation = math.slerp(
                localTransform.Rotation,
                quaternion.RotateZ(math.atan2(fixedDirection.y, fixedDirection.x)),
                movementData.RotationSpeed * DeltaTime
            );
        }
    }
}