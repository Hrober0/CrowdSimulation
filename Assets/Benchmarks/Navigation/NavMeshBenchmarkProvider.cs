using Unity.Mathematics;
using UnityEngine;

namespace Benchmarks
{
    public abstract class NavMeshBenchmarkProvider : MonoBehaviour
    {
        public abstract void Initialize(float2 size);
        public abstract void UpdateNavMesh(float2 min, float2 max);
        public abstract void FindPath(float2 start, float2 end);
    }
}