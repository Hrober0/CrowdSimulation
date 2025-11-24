using CustomNativeCollections;
using HCore.Shapes;
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
    public class AgentSelectionManager : MonoBehaviour
    {
        private Vector2 _startPosition;

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _startPosition = MousePosition.GetMousePosition();
            }

            if (Input.GetMouseButtonUp(0))
            {
                SelectAgents(_startPosition, MousePosition.GetMousePosition());
            }

            if (Input.GetMouseButtonDown(1))
            {
                SetTarget(MousePosition.GetMousePosition());
            }
        }

        private static void SelectAgents(Vector2 from, Vector2 to)
        {
            const float MIN_SIZE = 0.2f;

            if ((from - to).sqrMagnitude < MIN_SIZE)
            {
                var center = (to + from) * 0.5f;
                from = center - Vector2.one * MIN_SIZE;
                to = center + Vector2.one * MIN_SIZE;
            }

            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            // Disable all
            using EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
                                      .WithAll<Selected>()
                                      .Build(entityManager);
            using NativeArray<Entity> selectedEntities = query.ToEntityArray(Allocator.Temp);
            foreach (Entity selectedEntity in selectedEntities)
            {
                entityManager.SetComponentEnabled<Selected>(selectedEntity, false);
            }

            // Enable selected
            NativeSpatialHash<AgentCoreData> agentLookup
                = entityManager.World.GetExistingSystemManaged<AgentSpatialHashSystem>().SpatialHash;
            using var resultAgents = new NativeList<AgentCoreData>(Allocator.Temp);
            agentLookup.QueryAABB(from, to, resultAgents);
            var range = Rectangle.CreateByMinMax(from, to);
            foreach (AgentCoreData agent in resultAgents)
            {
                if (!range.Contains(agent.Position))
                {
                    continue;
                }

                entityManager.SetComponentEnabled<Selected>(agent.Entity, true);
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

                    // Set FindPathRequest
                    var findPathRequest = new FindPathRequest { TargetPosition = assignedPositions[i] };
                    ECB.SetComponentEnabled<FindPathRequest>(entity, true);
                    ECB.SetComponent(entity, findPathRequest);

                    // Reset AgentPathState
                    ECB.SetComponent(entity, new AgentPathState { CurrentPathIndex = 0 });

                    // Set TargetData
                    var targetData = new TargetData { TargetPosition = assignedPositions[i] };
                    ECB.SetComponentEnabled<TargetData>(entity, true);
                    ECB.SetComponent(entity, targetData);
                }
                
                agentPositions.Dispose();
            }
        }
    }
}