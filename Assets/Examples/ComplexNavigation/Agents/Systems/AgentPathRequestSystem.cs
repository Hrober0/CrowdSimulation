using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace ComplexNavigation
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(PathFindingSystem))]
    public partial struct AgentPathRequestSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new AgentPathRequestUpdateJob
            {
                CurrentTime = SystemAPI.Time.ElapsedTime
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
        public partial struct AgentPathRequestUpdateJob : IJobEntity
        {
            private const double UPDATE_INTERVAL = 1;
            private const float JITTER_RANGE = .2f;
            
            public double CurrentTime;

            public void Execute(
                [EntityIndexInQuery] int entityIndex,
                ref TargetData targetData,
                EnabledRefRW<TargetData> targetDataEnabled,
                ref FindPathRequest pathRequest,
                EnabledRefRW<FindPathRequest> pathRequestEnabled)
            {
                if (targetDataEnabled.ValueRO && CurrentTime - targetData.LastTargetUpdateTime > UPDATE_INTERVAL)
                {
                    var rng = Random.CreateFromIndex((uint)(entityIndex + 1) * 0x9F6ABC1u);
                    float jitter = rng.NextFloat(-JITTER_RANGE, JITTER_RANGE);
                    targetData.LastTargetUpdateTime = CurrentTime + jitter;
                    
                    pathRequestEnabled.ValueRW = true;
                    pathRequest.TargetPosition = targetData.TargetPosition;
                }
            }
        }
    }
}