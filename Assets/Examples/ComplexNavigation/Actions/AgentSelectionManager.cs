using CustomNativeCollections;
using HCore.Shapes;
using Unity.Collections;
using Unity.Entities;
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
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
                                      .WithPresent<TargetData>()
                                      .WithAll<Selected>()
                                      .Build(entityManager);

            using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                entityManager.SetComponentData(entity, new TargetData
                {
                    TargetPosition = target
                });
                entityManager.SetComponentEnabled<TargetData>(entity, true);
            }
        }
    }
}