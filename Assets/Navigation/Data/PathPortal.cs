using Unity.Mathematics;

namespace Navigation
{
    public struct PathPortal
    {
        public float2 Left;
        public float2 Right;
        public float2 PathPoint;

        public override string ToString() => $"PP({Left} {Right} {PathPoint})";
    }
}