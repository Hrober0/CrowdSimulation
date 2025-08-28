using Unity.Mathematics;

namespace Navigation
{
    public readonly struct Portal
    {
        public readonly float2 Left;
        public readonly float2 Right;
        public float2 Center => (Right + Left) * 0.5f;

        public Portal(float2 left, float2 right)
        {
            Left = left;
            Right = right;
        }
    }
}