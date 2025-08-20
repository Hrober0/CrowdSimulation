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

        public float2[] Vertices => new[] { A, B, C };

        public Edge[] Edges => new[]
        {
            new Edge(A, B),
            new Edge(B, C),
            new Edge(C, A)
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Triangle other) => A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C);

        public override bool Equals(object obj) => obj is Triangle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(A, B, C);

        public override string ToString() => $"Tri({A}, {B}, {C})";

        public IEnumerable<Vector2> GetBorderPoints()
        {
            yield return A;
            yield return B;
            yield return C;
        }

        public float2 Min => math.min(math.min(A, B), C);
        public float2 Max => math.max(math.max(A, B), C);
        
        public (float2 min, float2 max) GetBounds()
        {
            return (Min, Max);
        }
        
        public void Deconstruct(out float2 a, out float2 b, out float2 c)
        {
            a = A;
            b = B;
            c = C;
        }
        
        public float2 GetCenter => Center(A, B, C);

        #region Simple static methods

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
        public static float2 Center(float2 a, float2 b, float2 c) => (a + b + c) * 0.3333333f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sign(float2 a, float2 b, float2 c) => (a.x - c.x) * (b.y - c.y) - (b.x - c.x) * (a.y - c.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PointIn(float2 p, float2 a, float2 b, float2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);

            bool hasNeg = (d1 < -GeometryUtils.EPSILON) || (d2 < -GeometryUtils.EPSILON) || (d3 < -GeometryUtils.EPSILON);
            bool hasPos = (d1 > GeometryUtils.EPSILON)  || (d2 > GeometryUtils.EPSILON)  || (d3 > GeometryUtils.EPSILON);

            return !(hasNeg && hasPos);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PointInExcludingEdges(float2 p, float2 a, float2 b, float2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);

            bool hasNeg = (d1 < -GeometryUtils.EPSILON) || (d2 < -GeometryUtils.EPSILON) || (d3 < -GeometryUtils.EPSILON);
            bool hasPos = (d1 > GeometryUtils.EPSILON)  || (d2 > GeometryUtils.EPSILON)  || (d3 > GeometryUtils.EPSILON);

            // Exclude edge cases (d1, d2, d3 == 0)
            return !(hasNeg && hasPos) && d1 != 0f && d2 != 0f && d3 != 0f;
        }

        /// <summary>
        /// positive if CCW, negative if CW
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SignedArea(float2 a, float2 b, float2 c) => GeometryUtils.Cross(b - a, c - a);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Area(float2 a, float2 b, float2 c) => math.abs(SignedArea(a, b, c)) * 0.5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCCW(float2 a, float2 b, float2 c) => SignedArea(a, b, c) > 0;
        
        #endregion
        
        #region Static methods
        
        public static bool Intersect(Triangle t1, Triangle t2)
        {
            var bounds1 = t1.GetBounds();
            var bounds2 = t2.GetBounds();
            if (!GeometryUtils.AabbOverlap(bounds1.min, bounds1.max, bounds2.min, bounds2.max))
            {
                return false;
            }

            // Interior vertex inside other triangle → definite overlap
            foreach (var v in t1.Vertices)
            {
                if (PointInExcludingEdges(v, t2.A, t2.B, t2.C))
                {
                    return true;
                }
            }

            foreach (var v in t2.Vertices)
            {
                if (PointInExcludingEdges(v, t1.A, t1.B, t1.C))
                {
                    return true;
                }
            }

            // Interior edge crossing (exclude touching endpoints)
            foreach (var e1 in t1.Edges)
            {
                foreach (var e2 in t2.Edges)
                {
                    if (GeometryUtils.EdgesIntersect(e1.A, e1.B, e2.A, e2.B))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool AnyTrianglesIntersect(List<Triangle> triangles)
        {
            int count = triangles.Count;

            for (int i = 0; i < count; i++)
            {
                Triangle t1 = triangles[i];
                for (int j = i + 1; j < count; j++)
                {
                    var t2 = triangles[j];
                    if (Intersect(t1, t2))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        
        #endregion
    }
}