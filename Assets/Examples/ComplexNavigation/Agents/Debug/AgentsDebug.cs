using HCore.Extensions;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ComplexNavigation
{
    public class AgentsDebug : MonoBehaviour
    {
        [SerializeField] private bool _drawVelocities;

        private void OnDrawGizmos()
        {
            if (!_drawVelocities)
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
            foreach (AgentCoreData agent in agents)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(agent.Position.To3D(), (agent.Position + agent.Velocity).To3D());
                Gizmos.color = Color.green;
                Gizmos.DrawLine(agent.Position.To3D(), (agent.Position + agent.PrefVelocity).To3D());
            }
        }
    }
}