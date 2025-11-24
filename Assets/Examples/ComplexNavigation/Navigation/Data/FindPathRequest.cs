using Unity.Entities;
using Unity.Mathematics;

namespace ComplexNavigation
{
    public struct FindPathRequest : IComponentData, IEnableableComponent
    {
        public float2 TargetPosition;
    }
}