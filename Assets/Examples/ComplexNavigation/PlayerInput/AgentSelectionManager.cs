using CustomNativeCollections;
using HCore.Shapes;
using Navigation;
using Unity.Collections;
using Unity.Entities;
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
            var navMeshSystem = world.GetExistingSystemManaged<NavMeshSystem>();
            if (!navMeshSystem.NavMesh.TryGetNodeIndex(target, out var targetNodeIndex))
            {
                Debug.LogWarning("Outside navmesh");
                return;
            }

            EntityManager entityManager = world.EntityManager;
            using EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
                                      .WithPresent<LocalTransform>()
                                      .WithPresent<FindPathRequest>()
                                      .WithAll<Selected>()
                                      .Build(entityManager);

            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            using NativeArray<LocalTransform> transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Find target places
            using var targetPlaces = new NativeList<float2>(entities.Length, Allocator.TempJob);
            PathFinding.FindSpaces(target,
                targetNodeIndex,
                entities.Length,
                .3f,
                navMeshSystem.NavMesh.Nodes,
                new SamplePathSeeker(),
                targetPlaces);

            // Find agents positions
            var agentPositions = new NativeArray<float2>(entities.Length, Allocator.TempJob);
            for (var index = 0; index < entities.Length; index++)
            {
                agentPositions[index] = transforms[index].Position.xy;
            }

            // Assign optimal targets to agents 
            using var assignedPositions = new NativeArray<float2>(entities.Length, Allocator.TempJob);
            PathFinding.AssignTargets(agentPositions, targetPlaces.AsArray(), assignedPositions);

            for (var index = 0; index < entities.Length; index++)
            {
                Entity entity = entities[index];
                entityManager.SetComponentData(entity, new FindPathRequest
                {
                    TargetPosition = assignedPositions[index],
                });
                entityManager.SetComponentEnabled<FindPathRequest>(entity, true);
                entityManager.SetComponentData<AgentPathState>(entity, new() { CurrentPathIndex = 0 });
                
                entityManager.SetComponentData(entity, new TargetData
                {
                    TargetPosition = assignedPositions[index],
                });
                entityManager.SetComponentEnabled<TargetData>(entity, true);
            }

            agentPositions.Dispose();
        }
    }
}