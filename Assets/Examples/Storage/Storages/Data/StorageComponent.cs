using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Storage
{
    public struct StorageComponent : IComponentData
    {
        public float3 WorldPosition;
        public float3 InputPoint;   // where holders enter (avoidance disabled on arrival)
        public float3 OutputPoint;  // where holders exit  (avoidance re-enabled on arrival)
    }
}
