using Unity.Entities;
using Unity.Mathematics;

namespace ComplexNavigation
{
    public struct TargetData : IComponentData, IEnableableComponent
    {
        public float2 TargetPosition;
        public double LastTargetUpdateTime;
    }
}