using Unity.Entities;
using UnityEngine;

namespace ComplexNavigation
{
    public class NavMeshDebugManager : MonoBehaviour
    {
        [SerializeField] private bool _drawEdges;
        [SerializeField] private bool _drawLookup;
        [SerializeField] private bool _drawNodes;
        [SerializeField] private bool _drawConnections;
        
        private void OnDrawGizmos()
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

            if (_drawNodes)
            {
                navMeshSystem.NavMesh.DrawNodes(true);
            }
            
            if (_drawConnections)
            {
                navMeshSystem.NavMesh.DrawConnections();
            }
        }
    }
}