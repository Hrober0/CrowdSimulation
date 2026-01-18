using Unity.Mathematics;
using UnityEngine;

namespace Benchmarks
{
    public abstract class NavMeshBenchmarkProvider : MonoBehaviour
    {
        public abstract void Update(float2 min, float2 max);
        public abstract void FindPath(float2 start, float2 end);
    }
}