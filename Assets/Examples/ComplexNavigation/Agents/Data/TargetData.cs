using Unity.Entities;
using Unity.Mathematics;

namespace ComplexNavigation
{
    public struct TargetData : IComponentData
    {
        public float2 TargetPosition;
    }
}