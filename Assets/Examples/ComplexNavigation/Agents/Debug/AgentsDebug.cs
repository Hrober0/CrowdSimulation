using HCore.Extensions;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ComplexNavigation
{
    public class AgentsDebug : MonoBehaviour
    {
        [SerializeField] private bool _drawVelocities;
        [SerializeField] private bool _drawPreferredVelocities;

        private void OnDrawGizmos()
        {
            if (!_drawVelocities && !_drawPreferredVelocities)
            {
                return;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            EntityManager entityManager = world.EntityManager;

            using EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
                                      .WithAll<AgentCoreData>()
                                      .Build(entityManager);

            using var agents = query.ToComponentDataArray<AgentCoreData>(Allocator.Temp);
            if (_drawVelocities)
            {
                foreach (AgentCoreData agent in agents)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(agent.Position.To3D(), (agent.Position + agent.Velocity).To3D());
                }
            }
            if (_drawPreferredVelocities)
            {
                foreach (AgentCoreData agent in agents)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(agent.Position.To3D(), (agent.Position + agent.PrefVelocity).To3D());
                }
            }
        }
    }
}