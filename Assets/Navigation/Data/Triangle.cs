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
        
        public (float2 min, float2 max) GetBounds()
        {
            float2 min = math.min(math.min(A, B), C);
            float2 max = math.max(math.max(A, B), C);
            return (min, max);
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

            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(hasNeg && hasPos);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PointInExcludingEdges(float2 p, float2 a, float2 b, float2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);

            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            // Exclude edge cases (d1, d2, d3 == 0)
            return !(hasNeg && hasPos) && d1 != 0f && d2 != 0f && d3 != 0f;
        }
        
        /// <summary>
        /// positive if CCW, negative if CW
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SignedArea(float2 a, float2 b, float2 c) => Cross(b - a, c - a);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Area(float2 a, float2 b, float2 c) => math.abs(SignedArea(a, b, c)) * 0.5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCCW(float2 a, float2 b, float2 c) => SignedArea(a, b, c) > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool OnSegment(float2 a, float2 b, float2 c)
        {
            return math.min(a.x, c.x) <= b.x && b.x <= math.max(a.x, c.x) &&
                   math.min(a.y, c.y) <= b.y && b.y <= math.max(a.y, c.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EdgesIntersect(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float2 r = a2 - a1;
            float2 s = b2 - b1;
            float rxs = Cross(r, s);

            if (math.abs(rxs) < math.E)
            {
                return false; // Parallel or collinear
            }

            float2 delta = b1 - a1;
            float t = (delta.x * s.y - delta.y * s.x) / rxs;
            float u = (delta.x * r.y - delta.y * r.x) / rxs;

            return t is > 0f and < 1f && u is > 0f and < 1f;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float2 LineIntersection(float2 p1, float2 p2, float2 p3, float2 p4)
        {
            float2 r = p2 - p1;
            float2 s = p4 - p3;
            float rxs = Cross(r, s);
            if (math.abs(rxs) < 1e-8f)
            {
                return (p1 + p2) * 0.5f; // Lines nearly parallel
            }

            float t = Cross(p3 - p1, s) / rxs;
            return p1 + t * r;
        }
        
        #endregion
        
        #region Static methods
        
        public static bool Intersect(Triangle t1, Triangle t2)
        {
            var bounds1 = t1.GetBounds();
            var bounds2 = t2.GetBounds();
            if (!AabbOverlap(bounds1.min, bounds1.max, bounds2.min, bounds2.max))
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
                    if (EdgesIntersect(e1.A, e1.B, e2.A, e2.B))
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
        
        public static List<float2> PolygonIntersection(IReadOnlyList<float2> polyA, IReadOnlyList<float2> polyB)
        {
            if (polyA.Count < 3 || polyB.Count < 3)
            {
                return new();
            }

            // Start with all vertices from polyA
            var output = new List<float2>(polyA);
    
            // Clip against each edge of polyB
            for (int i = 0; i < polyB.Count; i++)
            {
                float2 clipA = polyB[i];
                float2 clipB = polyB[(i + 1) % polyB.Count];
    
                List<float2> input = output;
                output = new();
    
                if (input.Count == 0)
                {
                    break;
                }

                float2 s = input[^1];
                for (int j = 0; j < input.Count; j++)
                {
                    float2 e = input[j];
    
                    bool eInside = IsInside(clipA, clipB, e);
                    bool sInside = IsInside(clipA, clipB, s);
    
                    if (eInside)
                    {
                        if (!sInside)
                        {
                            output.Add(LineIntersection(s, e, clipA, clipB));
                        }

                        output.Add(e);
                    }
                    else if (sInside)
                    {
                        output.Add(LineIntersection(s, e, clipA, clipB));
                    }
    
                    s = e;
                }
            }
            
            // RemoveDuplicates
            for (int i = 0; i < output.Count; i++)
            {
                float2 p = output[i];
                for (int j = i + 1; j < output.Count; j++)
                {
                    if (math.lengthsq(p - output[j]) < .0001f)
                    {
                        output.RemoveAt(j);
                        j--;
                    }
                }
            }
                
            return output;
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool IsInside(float2 a, float2 b, float2 p) =>
                // Left-of test for AB -> point
                Cross(b - a, p - a) >= 0f;
        }
        
        #endregion

        #region Static utilities

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AabbOverlap(float2 minA, float2 maxA, float2 minB, float2 maxB)
        {
            return !(maxA.x < minB.x || minA.x > maxB.x ||
                     maxA.y < minB.y || minA.y > maxB.y);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
        
        #endregion
    }
}