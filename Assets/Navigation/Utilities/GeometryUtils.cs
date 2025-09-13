using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Navigation
{
    public static class GeometryUtils
    {
        public const float EPSILON = 1e-5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EdgesIntersect(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float2 r = a2 - a1;
            float2 s = b2 - b1;
            float rxs = Cross(r, s);

            if (math.abs(rxs) < EPSILON)
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

            if (math.abs(rxs) < EPSILON)
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
        /// Check if two segments intersects, ends are included.
        /// </summary>
        /// <remarks>
        /// In case of overlapping segments there can be two intersection points,
        /// but only one will be returned. To check full overlap use
        /// <see cref="TryIntersectAndOverlap"/>.
        /// </remarks>
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryIntersectAndOverlap(float2 a1, float2 a2, float2 b1, float2 b2, out float2 intersection1, out float2 intersection2, float tolerance = EPSILON)
        {
            float2 r = a2 - a1;
            float2 s = b2 - b1;

            float rxs = Cross(r, s);
            float qpxr = Cross(b1 - a1, r);
            
            // Collinear
            if (math.abs(rxs) < tolerance && math.abs(qpxr) < tolerance)
            {
                if (Overlaps1D(a1.x, a2.x, b1.x, b2.x) && Overlaps1D(a1.y, a2.y, b1.y, b2.y))
                {
                    // Find overlap segment
                    float2 minA = math.min(a1, a2);
                    float2 maxA = math.max(a1, a2);
                    float2 minB = math.min(b1, b2);
                    float2 maxB = math.max(b1, b2);

                    intersection1 = math.max(minA, minB); // start of overlap
                    intersection2 = math.min(maxA, maxB); // end of overlap
                    return true;
                }
            }
            
            // Parallel, non-intersecting
            if (math.abs(rxs) < tolerance)
            {
                intersection1 = default;
                intersection2 = default;
                return false;
            }

            float2 delta = b1 - a1;
            float t = Cross(delta, s) / rxs;
            float u = Cross(delta, r) / rxs;

            if (t < -tolerance || t > 1 + tolerance ||
                u < -tolerance || u > 1 + tolerance)
            {
                intersection1 = default;
                intersection2 = default;
                return false;
            }

            // Proper intersection
            intersection1 = intersection2 = a1 + t * r;
            return true;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetFarthestPointsOnLine(float2 a, float2 b, float2 c, float2 d, out float2 p1, out float2 p2)
        {
            // Pick a direction vector from first two points
            float2 dir = math.normalize(b - a);

            // Project points onto line
            float pa = math.dot(a, dir);

            // Find min and max projection
            float minProj = pa;
            float maxProj = pa;
            float2 minP = a;
            float2 maxP = a;

            Check(b);
            Check(c);
            Check(d);
            
            p1 = minP;
            p2 = maxP;
            return;
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void Check(float2 point)
            {
                float proj = math.dot(point, dir);
                if (proj < minProj) { minProj = proj; minP = point; }
                if (proj > maxProj) { maxProj = proj; maxP = point; }
            }
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
        public static bool Collinear(float2 a, float2 b, float2 c, float eps = EPSILON) => math.abs(Cross(b - a, c - a)) < eps;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AabbOverlap(float2 minA, float2 maxA, float2 minB, float2 maxB)
        {
            return !(maxA.x < minB.x || minA.x > maxB.x ||
                     maxA.y < minB.y || minA.y > maxB.y);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Overlaps1D(float a1, float a2, float b1, float b2)
        {
            if (a1 > a2) (a1, a2) = (a2, a1);
            if (b1 > b2) (b1, b2) = (b2, b1);
            return a1 <= b2 && b1 <= a2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SegmentsEqual(float2 a1, float2 a2, float2 b1, float2 b2, float tolerance = EPSILON)
        {
            return (NearlyEqual(a1, b1, tolerance) && NearlyEqual(a2, b2, tolerance))
                   || (NearlyEqual(a1, b2, tolerance) && NearlyEqual(a2, b1, tolerance));
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NearlyEqual(float2 u, float2 v, float eps = EPSILON) => math.distancesq(u, v) < eps;
        
        /// <summary>
        /// Returns the closest point on a line segment AB to point P.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 ClosestPointOnEdge(float2 a, float2 b, float2 p)
        {
            float2 ab = b - a;
            float2 ap = p - a;

            float t = math.dot(ap, ab) / math.dot(ab, ab);

            t = math.clamp(t, 0f, 1f);

            return a + t * ab;
        }
        
        /// <summary>
        /// Converts a 2D direction vector to an angle in degrees [0, 360).
        /// </summary>
        /// <list type="">
        ///  <item>Top    ->   0</item>
        ///  <item>Right  ->  90</item>
        ///  <item>Bottom -> 180</item>
        ///  <item>Left   -> 270</item>
        /// </list>
        /// <param name="dir">The 2D direction vector (float2).</param>
        /// <returns>Angle in degrees, normalized to [0, 360).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToAngleT0(this float2 dir)
        {
            // atan2 returns radians in range [-π, π]
            float angleRad = math.atan2(dir.y, dir.x);

            // Convert to degrees
            float angleDeg = angleRad * (-180f / math.PI);

            return (angleDeg + 810) % 360f;
        }
    }

}