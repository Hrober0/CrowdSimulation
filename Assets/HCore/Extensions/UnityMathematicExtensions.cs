using Unity.Mathematics;
using UnityEngine;

namespace HCore.Extensions
{
    public static class UnityMathematicsExtensions
    {
        public static Vector3 To3D(this float2 vector2D) => new Vector3(vector2D.x, VectorExtensions.Y_2D_POSITION, vector2D.y);
        public static Vector3 To3D(this float2 vector2D, float y) => new Vector3(vector2D.x, y, vector2D.y);
    }
}