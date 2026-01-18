using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using Unity.Mathematics;

namespace Benchmarks
{
    public class UnityNavMeshAdapter : NavMeshBenchmarkProvider
    {
        [SerializeField] private NavMeshSurface _surface;

        private NavMeshPath _path;

        public override void Initialize(float2 size)
        {
        }

        public override void UpdateNavMesh(float2 min, float2 max)
        {
            _surface.BuildNavMesh();
        }

        public override void FindPath(float2 start, float2 end)
        {
            if (_path == null)
            {
                _path = new NavMeshPath();
            }

            NavMesh.CalculatePath(
                new Vector3(start.x, 0, start.y),
                new Vector3(end.x, 0, end.y),
                NavMesh.AllAreas,
                _path);
        }
    }
}