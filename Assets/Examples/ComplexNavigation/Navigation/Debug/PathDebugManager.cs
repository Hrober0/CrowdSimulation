using HCore;
using HCore.Extensions;
using Navigation;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ComplexNavigation
{
    public class PathDebugManager : MonoBehaviour
    {
        [SerializeField] private bool _drawPortals;
        [SerializeField] private bool _drawPortalsRight;

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

            EntityManager entityManager = world.EntityManager;

            using EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
                                      .WithAll<Selected, PathBuffer>()
                                      .Build(entityManager);

            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
            {
                DynamicBuffer<PathBuffer> path = entityManager.GetBuffer<PathBuffer>(entity);
                for (int i = 0; i < path.Length; i++)
                {
                    var portal = path[i].Portal;

                    DebugUtils.Draw(portal.Left, portal.Right, Color.green);

                    portal.PathPoint.To3D().DrawPoint(ColorUtils.GetColor(i), null, 0.1f);

                    if (_drawPortalsRight)
                    {
                        portal.Right.To3D().DrawPoint(Color.red, null, 0.1f);
                    }
                }
            }
        }
    }
}