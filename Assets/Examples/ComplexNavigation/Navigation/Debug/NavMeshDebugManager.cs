using Unity.Entities;
using UnityEngine;

namespace ComplexNavigation
{
    public class NavMeshDebugManager : MonoBehaviour
    {
        [SerializeField] private bool _drawEdges;
        [SerializeField] private bool _drawLookup;
        
        private void Update()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world is not { IsCreated: true })
            {
                return;
            }

            var navMeshSystem = world.GetExistingSystemManaged<NavMeshSystem>();
            if (navMeshSystem == null)
            {
                return;
            }

            if (_drawEdges)
            {
                navMeshSystem.NavObstacles.DrawEdges();
            }

            if (_drawLookup)
            {
                navMeshSystem.NavObstacles.DrawLookup();
            }
        }
    }
}