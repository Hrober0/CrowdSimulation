using HCore.Extensions;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ComplexNavigation
{
    public class PathDebugManager : MonoBehaviour
    {
        [SerializeField] private bool _drawPortals;

        private void OnDrawGizmos()
        {
            if (!_drawPortals)
            {
                return;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            EntityManager em = world.EntityManager;

            EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<Selected>(),
                ComponentType.ReadOnly<PathBuffer>()
            );

            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                DynamicBuffer<PathBuffer> path = em.GetBuffer<PathBuffer>(entity);
                for (int i = 0; i < path.Length; i++)
                {
                    var portal = path[i].Portal;

                    var left3 = portal.Left.To3D();
                    var right3 = portal.Right.To3D();

                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(left3, right3);

                    portal.Path.To3D().DrawPoint(Color.magenta, null, 0.1f);
                }
            }
        }
    }
}