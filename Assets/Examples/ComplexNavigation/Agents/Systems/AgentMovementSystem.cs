using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ComplexNavigation
{
    partial struct AgentMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach ((
                         RefRW<LocalTransform> localTransform,
                         RefRO<AgentMovementData> movementData,
                         RefRO<TargetData> targetData)
                     in SystemAPI.Query<
                         RefRW<LocalTransform>,
                         RefRO<AgentMovementData>,
                         RefRO<TargetData>>())
            {
                float2 moveDirection = targetData.ValueRO.TargetPosition - localTransform.ValueRO.Position.xy;
                float magnitude = math.length(moveDirection);
                float moveThisFrame = movementData.ValueRO.MovementSpeed * SystemAPI.Time.DeltaTime;
                if (magnitude < moveThisFrame)
                {
                    return;
                }

                float3 fixedDirection = math.float3(moveDirection / magnitude, 0);
                localTransform.ValueRW.Position += fixedDirection * moveThisFrame;
                localTransform.ValueRW.Rotation = math.slerp(
                    localTransform.ValueRO.Rotation,
                    quaternion.RotateZ(math.atan2(moveDirection.y, moveDirection.x)),
                    movementData.ValueRO.RotationSpeed * SystemAPI.Time.DeltaTime);
            }
        }
    }
}