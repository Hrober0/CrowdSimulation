using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Avoidance
{
    public struct RVOMath
    {
        public const float EPSILON = 0.00001f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Det(float2 vector1, float2 vector2) => vector1.x * vector2.y - vector1.y * vector2.x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistSqPointLineSegment(float2 vector1, float2 vector2, float2 vector3)
        {
            float r = math.dot(vector3 - vector1, vector2 - vector1) / math.lengthsq(vector2 - vector1);

            return r switch
            {
                < 0.0f => math.lengthsq(vector3 - vector1),
                > 1.0f => math.lengthsq(vector3 - vector2),
                _ => math.lengthsq(vector3 - (vector1 + r * (vector2 - vector1)))
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float LeftOf(float2 a, float2 b, float2 c) => Det(a - c, b - a);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ChunkHash(int x, int y) => x + (y << 16);
    }
}