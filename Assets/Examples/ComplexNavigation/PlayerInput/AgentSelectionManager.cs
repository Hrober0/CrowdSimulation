using System;
using CustomNativeCollections;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace ComplexNavigation
{
    public class AgentSelectionManager : MonoBehaviour
    {
        public static Action<Vector2> OnSelectStart;
        public static Action<Vector2> OnSelectPerforming;
        public static Action<Vector2> OnSelectionEnd;

        private static readonly float4 _defaultColor = new(1, 1, 1, 1);
        private static readonly float4 _selectedColor = new(.5f, 1, .5f, 1);

        private Vector2 _startPosition;

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                StatSelection();
            }

            if (Input.GetMouseButton(0))
            {
                OnSelectPerforming?.Invoke(MousePosition.GetMousePosition());
            }

            if (Input.GetMouseButtonUp(0))
            {
                EndSelection();
            }
        }

        private void StatSelection()
        {
            _startPosition = MousePosition.GetMousePosition();
            OnSelectStart?.Invoke(_startPosition);
        }

        private void EndSelection()
        {
            var endPosition = MousePosition.GetMousePosition();
            SelectAgents(_startPosition, endPosition);
            OnSelectionEnd?.Invoke(endPosition);
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
                                      .WithAll<Selected, URPMaterialPropertyBaseColor>()
                                      .Build(entityManager);
            using NativeArray<Entity> selectedEntities = query.ToEntityArray(Allocator.Temp);
            foreach (Entity selectedEntity in selectedEntities)
            {
                entityManager.SetComponentEnabled<Selected>(selectedEntity, false);
                entityManager.SetComponentData<URPMaterialPropertyBaseColor>(selectedEntity, new() { Value = _defaultColor });
            }

            // Enable selected
            float2 selectMin = math.min(from, to);
            float2 selectMax = math.max(from, to);
            NativeSpatialHash<AgentCoreData> agentLookup
                = entityManager.World.GetExistingSystemManaged<AgentSpatialHashSystem>().SpatialHash;
            using var resultAgents = new NativeList<AgentCoreData>(Allocator.Temp);
            agentLookup.QueryAABB(selectMin, selectMax, resultAgents);

            foreach (AgentCoreData agent in resultAgents)
            {
                float2 aMin = agent.Position - agent.Radius * .5f;
                float2 aMax = agent.Position + agent.Radius * .5f;
                if (aMin.x < selectMin.x || aMin.y < selectMin.y || aMax.x > selectMax.x || aMax.y > selectMax.y)
                {
                    continue;
                }

                entityManager.SetComponentEnabled<Selected>(agent.Entity, true);
                entityManager.SetComponentData<URPMaterialPropertyBaseColor>(agent.Entity, new() { Value = _selectedColor });
            }
        }
    }
}