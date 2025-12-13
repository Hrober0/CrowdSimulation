using Unity.Burst;
using Unity.Entities;

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
            private const double UPDATE_INTERVAL = 0.5;
            public double CurrentTime;

            public void Execute(ref TargetData targetData,
                                EnabledRefRW<TargetData> targetDataEnabled,
                                ref FindPathRequest pathRequest,
                                EnabledRefRW<FindPathRequest> pathRequestEnabled)
            {
                if (targetDataEnabled.ValueRO && CurrentTime - targetData.LastTargetUpdateTime > UPDATE_INTERVAL)
                {
                    pathRequestEnabled.ValueRW = true;
                    pathRequest.TargetPosition = targetData.TargetPosition;
                    targetData.LastTargetUpdateTime = CurrentTime;
                }
            }
        }
    }
}