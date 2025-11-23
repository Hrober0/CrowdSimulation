using Unity.Entities;
using Unity.Mathematics;

namespace ComplexNavigation
{
    public struct ObstacleVertexBuffer : IBufferElementData
    {
        public float2 Vertex;
    }
}