using Navigation;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

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
                EnabledRefRW<TargetData> enabledTarget,
                ref AgentCoreData coreData,
                in DynamicBuffer<PathBuffer> pathBuffer,
                ref PathIndex pathIndex)
            {
                if (pathIndex.Index >= pathBuffer.Length)
                {
                    // Target reached
                    coreData.PrefVelocity = float2.zero;
                    enabledTarget.ValueRW = false;
                    return;
                }

                float2 agentPosition = coreData.Position;
                PathPortal currentPortal = pathBuffer[pathIndex.Index].Portal;
                // DebugUtils.Draw(agentPosition, portal.Center, Color.black);

                if (GeometryUtils.Sign(agentPosition, currentPortal.Left, currentPortal.Right) > 0)
                {
                    pathIndex.Index++;
                    if (pathIndex.Index == pathBuffer.Length)
                    {
                        return;
                    }
                }
                else if (pathIndex.Index + 1 < pathBuffer.Length)
                {
                    var closestPortalPoint = GeometryUtils.ClosestPointOnSegment(agentPosition, currentPortal.Left, currentPortal.Right);
                    if (math.distancesq(closestPortalPoint, agentPosition) < DeltaTime * coreData.MaxSpeed)
                    {
                        pathIndex.Index++;
                        if (pathIndex.Index == pathBuffer.Length)
                        {
                            return;
                        }
                    }
                }

                var index = pathIndex.Index;
                GetLookAheadPoint(agentPosition, pathBuffer, ref index, .2f, out float2 lookTarget);

                float2 currentTargetPosition = index + 1 < pathBuffer.Length
                    ? agentPosition + math.normalize(lookTarget - agentPosition)
                    : pathBuffer[^1].Portal.PathPoint;

                float2 preferredVelocity =
                    PathMovement.ComputePreferredVelocity(
                        agentPosition,
                        coreData.Velocity,
                        currentTargetPosition,
                        coreData.MaxSpeed,
                        3,
                        .5f,
                        DeltaTime
                    );

                coreData.PrefVelocity = preferredVelocity;
            }

            private static void GetLookAheadPoint(
                float2 position,
                in DynamicBuffer<PathBuffer> pathBuffer,
                ref int index,
                float lookAhead,
                out float2 lookAheadTarget)
            {
                float2 current = position;
                float remaining = lookAhead;

                // Walk forward through path segments
                for (; index < pathBuffer.Length; index++)
                {
                    float2 next = pathBuffer[index].Portal.PathPoint;
                    float2 segment = next - current;
                    float segLen = math.length(segment);

                    if (segLen >= remaining)
                    {
                        // We can place the lookahead point inside this segment
                        float2 direction = segment / math.max(segLen, math.EPSILON);
                        lookAheadTarget = current + direction * remaining;
                        return;
                    }

                    remaining -= segLen;
                    current = next;
                }

                // End of path
                lookAheadTarget = pathBuffer[^1].Portal.PathPoint;
            }
        }
    }
}