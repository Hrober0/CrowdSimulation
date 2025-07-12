using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HCore.Shapes;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public readonly struct Triangle : IEquatable<Triangle>, IOutline
    {
        public readonly float2 A;
        public readonly float2 B;
        public readonly float2 C;
   
        public Triangle(float2 a, float2 b, float2 c)
        {
            A = a;
            B = b;
            C = c;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Fits(Triangle t1, Triangle t2)
        {
            return PointsMatch(t1.A, t2.A, t2.B, t2.C) &&
                   PointsMatch(t1.B, t2.A, t2.B, t2.C) &&
                   PointsMatch(t1.C, t2.A, t2.B, t2.C);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool PointsMatch(float2 p, float2 x, float2 y, float2 z)
        {
            return p.Equals(x) || p.Equals(y) || p.Equals(z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Triangle other) => A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C);

        public override bool Equals(object obj) => obj is Triangle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(A, B, C);

        public override string ToString() => $"({A}, {B}, {C})";

        public IEnumerable<Vector2> GetBorderPoints()
        {
            yield return A;
            yield return B;
            yield return C;
        }
    }
}