using CustomNativeCollections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace ComplexNavigation
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class AgentSpatialHashSystem : SystemBase
    {
        public NativeSpatialHash<AgentCoreData> SpatialHash;
        public JobHandle OutputHandle;

        protected override void OnCreate()
        {
            base.OnCreate();

            SpatialHash = new(
                capacity: 4096,
                cellSize: 1f,
                Allocator.Persistent
            );
        }

        protected override void OnDestroy()
        {
            if (SpatialHash.IsCreated)
                SpatialHash.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AgentSpatialHashSystem))]
    [BurstCompile]
    public partial struct AgentSpatialHashUpdateSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var hashSystem = state.World.GetExistingSystemManaged<AgentSpatialHashSystem>();

            var agentPositions = new NativeList<AgentData>(Allocator.TempJob);
            foreach (var (localTransform, coreData)
                     in SystemAPI.Query<RefRO<LocalTransform>, RefRW<AgentCoreData>>())
            {
                coreData.ValueRW.Position = localTransform.ValueRO.Position.xy;
                agentPositions.Add(new()
                {
                    Pos = localTransform.ValueRO.Position.xy,
                    Radius = coreData.ValueRO.Radius,
                    CoreData = coreData.ValueRO
                });
            }

            new UpdateSpatialHashJob
            {
                Hash = hashSystem.SpatialHash,
                Agents = agentPositions
            }.Run();

            agentPositions.Dispose();
        }
    }

    struct AgentData
    {
        public float2 Pos;
        public float Radius;
        public AgentCoreData CoreData;
    }

    [BurstCompile]
    struct UpdateSpatialHashJob : IJob
    {
        public NativeSpatialHash<AgentCoreData> Hash;
        [ReadOnly] public NativeList<AgentData> Agents;

        public void Execute()
        {
            Hash.Clear();
            foreach (var a in Agents)
            {
                Hash.AddAABB(a.Pos - new float2(a.Radius, a.Radius), a.Pos + new float2(a.Radius, a.Radius), a.CoreData);
            }
        }
    }
}