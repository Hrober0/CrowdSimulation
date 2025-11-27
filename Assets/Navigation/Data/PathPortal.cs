using Unity.Mathematics;

namespace Navigation
{
    public struct PathPortal
    {
        public float2 Left;
        public float2 Right;
        public float2 Path;
        public float2 Direction;

        public PathPortal(float2 left, float2 right, float2 path, float2 direction)
        {
            Left = left;
            Right = right;
            Path = path;
            Direction = direction;
        }

        public override string ToString() => $"PP({Left} {Right} {Path})";
    }
}