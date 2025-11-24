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
            new DirectionCalculationJob()
                .ScheduleParallel();
        }

        [BurstCompile]
        public partial struct DirectionCalculationJob : IJobEntity
        {
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
                    Portal portal = pathBuffer[agentPathState.CurrentPathIndex].Portal;
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

                float2 nextPathPoint = agentPathState.CurrentPathIndex + 1 < pathBuffer.Length
                    ? pathBuffer[agentPathState.CurrentPathIndex + 1].Portal.Center
                    : targetData.TargetPosition;
                float2 direction = PathFinding.ComputeGuidanceVector(agentPosition,
                    pathBuffer[agentPathState.CurrentPathIndex].Portal,
                    nextPathPoint);

                coreData.PrefVelocity = direction;
            }
        }
    }
}