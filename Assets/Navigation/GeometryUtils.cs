using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Navigation
{
    public static class GeometryUtils
    {
        private const float EPSILON = 1e-6f;
        
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
            float t = Cross(delta, s) / rxs;
            float u = Cross(delta, r) / rxs;

            return t is > 0f and < 1f && u is > 0f and < 1f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EdgesIntersectIncludeEnds(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float2 r = a2 - a1;
            float2 s = b2 - b1;
            float rxs = Cross(r, s);
            // float q_pxr = Cross(b1 - a1, r);

            if (math.abs(rxs) < math.E)
            {
                return false; // Parallel or collinear
            }

            float2 delta = b1 - a1;
            float t = Cross(delta, s) / rxs;
            float u = Cross(delta, r) / rxs;

            return t is >= 0f and <= 1f && u is >= 0f and <= 1f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 IntersectionPoint(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float2 r = a2 - a1;
            float2 s = b2 - b1;
            float rxs = Cross(r, s);
            if (math.abs(rxs) < EPSILON)
            {
                return (a1 + a2) * 0.5f; // Lines nearly parallel
            }

            float t = Cross(b1 - a1, s) / rxs;
            return a1 + t * r;
        }
        
        /// <summary>
        /// Ends are included
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryIntersect(float2 a1, float2 a2, float2 b1, float2 b2, out float2 intersection)
        {
            float2 r = a2 - a1;
            float2 s = b2 - b1;

            float rxs = Cross(r, s);
            float qpxr = Cross(b1 - a1, r);

            // Default
            intersection = default;

            // Collinear
            if (math.abs(rxs) < EPSILON && math.abs(qpxr) < EPSILON)
            {
                // Overlapping collinear segments → skip (not a single intersection point)
                return false;
            }

            // Parallel, non-intersecting
            if (math.abs(rxs) < EPSILON)
            {
                return false;
            }

            float2 delta = b1 - a1;
            float t = Cross(delta, s) / rxs;
            float u = Cross(delta, r) / rxs;

            if (t is < -EPSILON or > 1 + EPSILON ||
                u is < -EPSILON or > 1 + EPSILON)
            {
                return false;
            }

            // Proper intersection
            intersection = a1 + t * r;
            return true;
        }
        
        /// <summary>
        /// Checks if points p1 and p2 lie on the same side of the line segment AB.
        /// </summary>
        /// <returns>
        /// <list type="bullet">
        ///   <item>
        ///     <description>Positive — points are on the same side</description>
        ///   </item>
        ///   <item>
        ///     <description>Negative — points are on opposite sides</description>
        ///   </item>
        ///   <item>
        ///     <description>Zero — any of the points lies on the line</description>
        ///   </item>
        /// </list>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Side(float2 lineA, float2 lineB, float2 p1, float2 p2)
        {
            // Direction vector of the segment AB
            float2 ab = lineB - lineA;

            // Cross products of AB with AP1 and AP2
            float cross1 = Cross(ab, p1 - lineA);
            float cross2 = Cross(ab, p2 - lineA);
            
            return cross1 * cross2;
        }

        /// <summary>
        /// 2D cross product (returns scalar z-component)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Cross(float2 u, float2 v) => u.x * v.y - u.y * v.x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Collinear(float2 a, float2 b, float2 c, float eps = 1e-6f) => math.abs(Cross(b - a, c - a)) < eps;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AabbOverlap(float2 minA, float2 maxA, float2 minB, float2 maxB)
        {
            return !(maxA.x < minB.x || minA.x > maxB.x ||
                     maxA.y < minB.y || minA.y > maxB.y);
        }
    }

}