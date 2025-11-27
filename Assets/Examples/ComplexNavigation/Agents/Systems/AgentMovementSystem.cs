using Navigation;
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
            new DirectionCalculationJob
                {
                    DeltaTime = SystemAPI.Time.DeltaTime,
                }
                .ScheduleParallel();
        }

        [BurstCompile]
        public partial struct DirectionCalculationJob : IJobEntity
        {
            public float DeltaTime;
            
            public void Execute(
                in LocalTransform localTransform,
                ref AgentCoreData coreData,
                in TargetData targetData,
                in DynamicBuffer<PathBuffer> pathBuffer,
                ref AgentPathState agentPathState)
            {
                float2 agentPosition = localTransform.Position.xy;

                if (agentPathState.CurrentPathIndex < pathBuffer.Length)
                {
                    PathPortal portal = pathBuffer[agentPathState.CurrentPathIndex].Portal;
                    // DebugUtils.Draw(agentPosition, portal.Center, Color.black);

                    if (GeometryUtils.Sign(agentPosition, portal.Left, portal.Right) > 0)
                    {
                        agentPathState.CurrentPathIndex++;
                    }
                }

                if (agentPathState.CurrentPathIndex > pathBuffer.Length)
                {
                    // Target reached
                    coreData.PrefVelocity = float2.zero;
                    coreData.MaxSpeed = 1;
                    return;
                }

                if (agentPathState.CurrentPathIndex == pathBuffer.Length)
                {
                    // Last path
                    float2 moveDirection = targetData.TargetPosition - localTransform.Position.xy;
                    var distanceSq = math.lengthsq(moveDirection);
                    if (distanceSq < 0.1f)
                    {
                        // Target reached
                        agentPathState.CurrentPathIndex++;
                        coreData.PrefVelocity = float2.zero;
                        return;
                    }

                    coreData.PrefVelocity = moveDirection / math.sqrt(distanceSq);
                    return;
                }

                PathPortal targetPortal = pathBuffer[agentPathState.CurrentPathIndex].Portal;
                float2 focusDirection = math.normalizesafe(targetPortal.Path - agentPosition);
                float2 direction = math.normalizesafe(focusDirection + targetPortal.Direction);
                // coreData.MaxSpeed = math.min(coreData.MaxSpeed + DeltaTime * 10, 10);
                coreData.PrefVelocity = direction * coreData.MaxSpeed;
            }
        }
    }
}