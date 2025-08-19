using Unity.Mathematics;

namespace Navigation
{
    public readonly struct Edge
    {
        public readonly float2 A;
        public readonly float2 B;

        public Edge(float2 a, float2 b)
        {
            A = a;
            B = b;
        }
        
        public float2 Center => (A + B) / 2f;
    }
}