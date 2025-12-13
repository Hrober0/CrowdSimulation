using Navigation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace ComplexNavigation
{
    public class AgentTargetManager : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetMouseButtonDown(1))
            {
                SetTarget(MousePosition.GetMousePosition());
            }
        }

        private static void SetTarget(Vector2 target)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var entityManager = world.EntityManager;
            var navMeshSystem = world.GetExistingSystemManaged<NavMeshSystem>();

            // Query selected agents
            using EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
                                      .WithAll<LocalTransform, Selected>()
                                      .WithAny<FindPathRequest, AgentPathState, TargetData>()
                                      .Build(entityManager);
            using var entities = query.ToEntityArray(Allocator.TempJob);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

            if (entities.Length == 0)
            {
                return;
            }

            // ECB for parallel writing
            var ecbSystem = world.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();
            var ecb = ecbSystem.CreateCommandBuffer();

            var job = new SetTargetJob
            {
                Entities = entities,
                AgentTransforms = transforms,
                TargetPosition = target,
                NavMesh = navMeshSystem.NavMesh,
                Seeker = new SamplePathSeeker(),
                ECB = ecb
            };

            job.Schedule().Complete();
        }

        [BurstCompile]
        public struct SetTargetJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<LocalTransform> AgentTransforms;
            [ReadOnly] public float2 TargetPosition;
            [ReadOnly] public NavMesh<IdAttribute> NavMesh;
            [ReadOnly] public SamplePathSeeker Seeker;

            public EntityCommandBuffer ECB;

            public void Execute()
            {
                // Get target node index
                if (!NavMesh.TryGetNodeIndex(TargetPosition, out int targetNodeIndex))
                {
                    // outside navmesh; skip
                    return;
                }

                int agentNumber = Entities.Length;

                // Agent positions
                var agentPositions = new NativeArray<float2>(agentNumber, Allocator.Temp);
                for (int i = 0; i < agentNumber; i++)
                {
                    agentPositions[i] = AgentTransforms[i].Position.xy;
                }

                using var targetPlaces = new NativeList<float2>(agentNumber, Allocator.Temp);
                PathFinding.FindSpaces(
                    TargetPosition,
                    targetNodeIndex,
                    agentNumber,
                    0.3f,
                    NavMesh.Nodes,
                    Seeker,
                    targetPlaces
                );

                using var assignedPositions = new NativeArray<float2>(agentNumber, Allocator.Temp);
                PathFinding.AssignTargets(agentPositions, targetPlaces.AsArray(), assignedPositions);

                for (int i = 0; i < agentNumber; i++)
                {
                    Entity entity = Entities[i];

                    // Reset AgentPathState
                    ECB.SetComponent(entity, new AgentPathState { CurrentPathIndex = 0 });

                    // Set TargetData
                    ECB.SetComponentEnabled<TargetData>(entity, true);
                    ECB.SetComponent<TargetData>(entity, new() { TargetPosition = assignedPositions[i] });
                }

                agentPositions.Dispose();
            }
        }
    }
}