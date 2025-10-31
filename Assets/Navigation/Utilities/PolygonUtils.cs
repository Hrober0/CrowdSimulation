using System.Runtime.CompilerServices;
using HCore.Extensions;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public static class PolygonUtils
    {
        public static bool IsPointInPolygon(float2 point, in NativeList<EdgeKey> polygon)
        {
            int crossings = 0;
        
            for (int i = 0; i < polygon.Length; i++)
            {
                float2 a = polygon[i].A;
                float2 b = polygon[i].B;
        
                // Check if point.x is between a.x and b.x (ray could intersect this edge)
                if (point.x > a.x && point.x <= b.x && point.y < Mathf.Max(a.y, b.y))
                {
                    // Compute y intersection of vertical ray at point.x with edge (a → b)
                    float yIntersection = (point.x - a.x) * (b.y - a.y) / (b.x - a.x + float.Epsilon) + a.y;
        
                    if (point.y < yIntersection)
                    {
                        crossings++;
                    }
                }
            }
        
            return (crossings % 2) == 1;
        }
        //
        // public static float2 PolygonCenter(List<EdgeKey> polygon)
        // {
        //     var sum = float2.zero;
        //     for (int i = 0; i < polygon.Count; i++)
        //     {
        //        sum += polygon[i].A;
        //        sum += polygon[i].B;
        //     }
        //     return sum / (polygon.Count * 2);
        // }
        
        /// <summary>
        /// Add to list border edges
        /// </summary>
        public static void GetEdgesUnordered(in NativeList<Triangle> triangles, NativeList<EdgeKey> borderEdges, float tolerance = GeometryUtils.EPSILON)
        {
            var points = new NativeList<float2>(triangles.Length * 3, Allocator.Temp);
            var edgeCounts = new NativeHashMap<EdgeKey, int>(triangles.Length * 3, Allocator.Temp);

            // Count all triangle edges
            foreach (Triangle tr in triangles)
            {
                AddEdge(tr.A, tr.B);
                AddEdge(tr.B, tr.C);
                AddEdge(tr.C, tr.A);
                // tr.GetCenter.To3D().DrawPoint(Color.cyan, size: 0.3f);
            }

            // Gather unique boundary points (appear only once)
            foreach (var kvp in edgeCounts)
            {
                if (kvp.Value == 1)
                {
                    borderEdges.Add(kvp.Key);
                }

                // var c = kvp.Value == 1 ? Color.green : Color.magenta;
                // Debug.DrawLine(math.float3(kvp.Key.A, 0), math.float3(kvp.Key.B, 0), c);
            }
            
            edgeCounts.Dispose();
            return;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void AddEdge(float2 a, float2 b)
            {
                AlignPoint(ref a);
                AlignPoint(ref b);
                var edge = new EdgeKey(a, b);
                if (edgeCounts.TryGetValue(edge, out int count))
                {
                    edgeCounts[edge] = count + 1;
                }
                else
                {
                    edgeCounts[edge] = 1;
                }
            }
            
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void AlignPoint(ref float2 p)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    if (math.lengthsq(p - points[i]) < tolerance)
                    {
                        p = points[i];
                        return;
                    }
                }
                points.Add(p);
            }
        }
        
        /// <summary>
        /// Convert border edge and add to points list in CCW ordered,
        /// returns true if loop is closed
        /// </summary>
        public static bool GetPointsCCW(in NativeList<EdgeKey> edges, NativeList<float2> points, bool ensureCCW = true)
        {
            // foreach (var p in edges)
            // {
            //     Debug.DrawLine(math.float3(p.A, 0), math.float3(p.B, 0), Color.magenta, 2);
            // }
            
            if (edges.Length == 0)
            {
                return false;
            }
            
            if (edges.Length == 1)
            {
                points.Add(edges[0].A);
                points.Add(edges[0].B);
                return true;
            }
            
            var pointsStartIndex = points.Length; // save start index
            
            using var edgeMap = new NativeParallelMultiHashMap<float2, EdgeKey>(edges.Length * 2, Allocator.Temp);
            foreach (EdgeKey edge in edges)
            {
                edgeMap.Add(edge.A, edge);
                edgeMap.Add(edge.B, edge);
            }

            // Debug.Log("Checking CCW");
            
            // Reconstruct ordered boundary loop
            bool isClosed = true;
            float2 startVert = edges[0].A;
            float2 currentVert = startVert;
            float2 nextVert = edges[0].B;
            points.Add(currentVert);
            // Debug.Log($"NextEdge {currentVert.x}, {currentVert.y}");
            for (int i = 0; i < edges.Length; i++)
            {
                // Debug.Log($"NextEdge {nextVert.x}, {nextVert.y}");
                points.Add(nextVert);
                
                EdgeKey? found = null;
                foreach (EdgeKey candidate in edgeMap.GetValuesForKey(nextVert))
                {
                    if (GeometryUtils.NearlyEqual(candidate.B, currentVert) || GeometryUtils.NearlyEqual(candidate.A, currentVert))
                    {
                        // candidate is the same edge as current
                        continue;
                    }
                    
                    found = candidate;
                    break;
                }

                if (found == null)
                {
                    isClosed = false;
                    break;
                }

                EdgeKey edge = found.Value;
                if (GeometryUtils.NearlyEqual(edge.A, nextVert))
                {
                    currentVert = edge.A;
                    nextVert = edge.B;
                }
                else
                {
                    currentVert = edge.B;
                    nextVert = edge.A;
                }
                
                if (GeometryUtils.NearlyEqual(nextVert, startVert))
                {
                    break;
                }
            }

            var addedPoints = points.Length - pointsStartIndex;
            if (addedPoints < edges.Length)
            {
                Debug.LogWarning($"Added points: {addedPoints} is less than edges: {edges.Length}.");
                return false;
            }
            
            // Make sure that points are sorted CCW
            if (ensureCCW && pointsStartIndex + 3 < points.Length && !Triangle.IsCCW(
                    points[pointsStartIndex], 
                    points[pointsStartIndex + 1], 
                    points[pointsStartIndex + 2])
                )
            {
                // Reverse
                int swapMidIndex = addedPoints / 2 + pointsStartIndex;
                for (int s = pointsStartIndex, e = points.Length - 1; s < swapMidIndex; s++, e--)
                {
                    (points[s], points[e]) = (points[e], points[s]);
                }
            }

            return isClosed;
        }

        /// <summary>
        /// Build polygon edges from ordered points, reducing redundant collinear vertices.
        /// </summary>
        /// <param name="orderedPoints">Points forming a closed loop (must be ordered CCW or CW).</param>
        /// <param name="edges">List where reduced edges will be added.</param>
        /// <param name="toleration"></param>
        public static void ReduceEdges(in NativeList<float2> orderedPoints, NativeList<Edge> edges, float toleration = GeometryUtils.EPSILON)
        {
            if (orderedPoints.Length < 3)
            {
                return;
            }

            using var reduced = new NativeList<float2>(orderedPoints.Length, Allocator.Temp);

            var l = orderedPoints.Length;
            for (int i = 0; i < l; i++)
            {
                float2 prev = orderedPoints[(i - 1 + l) % l];
                float2 curr = orderedPoints[i];
                float2 next = orderedPoints[(i + 1) % l];

                if (!GeometryUtils.Collinear(prev, curr, next, toleration))
                {
                    reduced.Add(curr);
                }
            }

            // build edges from reduced points
            for (int i = 0; i < reduced.Length; i++)
            {
                float2 a = reduced[i];
                float2 b = reduced[(i + 1) % reduced.Length];
                edges.Add(new Edge(a, b));
            }
            
            // foreach (var p in edges)
            // {
            //     Debug.DrawLine(math.float3(p.A, 0), math.float3(p.B, 0), Color.magenta, 2);
            //     p.Center.To3D().DrawPoint(Color.green,2, 0.3f);
            // }

            // foreach (var p in orderedPoints)
            // {
            //     p.To3D().DrawPoint(Color.blue, 2, 0.3f);
            // }
        }

        /// <summary>
        /// Replace intersection edges (ends included) by new edges to avoid intersection
        /// </summary>
        public static void CutIntersectingEdges(NativeList<Edge> edges, float tolerance = GeometryUtils.EPSILON)
        {
            for (int ai = 0; ai < edges.Length; ai++)
            {
                for (int bi = ai + 1; bi < edges.Length; bi++)
                {
                    Edge a = edges[ai];
                    Edge b = edges[bi];
                    if (!GeometryUtils.TryIntersectAndOverlap(a.A, a.B, b.A, b.B, out float2 intersection1, out float2 intersection2, tolerance))
                    {
                        continue;
                    }

                    // Debug.Log($"{a.A}, {a.B}, {b.A}, {b.B} -> {intersection1}, {intersection2} | {GeometryUtils.NearlyEqual(intersection1, intersection2, tolerance)} {GeometryUtils.SegmentsEqual(a.A, a.B, b.A, b.B, tolerance)}");
                    if (GeometryUtils.NearlyEqual(intersection1, intersection2, tolerance))
                    {
                        // Edges not overlap
                        SplitEdge(a, intersection1, ai);
                        SplitEdge(b, intersection1, bi);
                        continue;
                    }

                    if (GeometryUtils.SegmentsEqual(a.A, a.B, b.A, b.B, tolerance))
                    {
                        // Do not change edge a
                        // edges[ai] = a;
                        
                        // Remove duplicated edge b
                        if (bi < edges.Length - 1)
                        {
                            edges[bi] = edges[^1]; // replace duplicated edge by last edge
                            bi--; // make sure that new element will be checked
                        }
                        edges.Length--;
                        continue;
                    }
                    
                    // Overlaps partially
                    GeometryUtils.GetFarthestPointsOnLine(a.A, a.B, b.A, b.B, out float2 end1, out float2 end2);
                    
                    float2 pointsCloseToEnd1 = intersection1;
                    float2 pointsCloseToEnd2 = intersection2;
                    if (math.distancesq(end1, pointsCloseToEnd1) >
                        math.distancesq(end1, pointsCloseToEnd2))
                    {
                        (pointsCloseToEnd1, pointsCloseToEnd2) = (pointsCloseToEnd2, pointsCloseToEnd1);
                    }

                    // Debug.Log($"{end1}-{pointsCloseToEnd1} {end2}-{pointsCloseToEnd2}");    
                    if (GeometryUtils.NearlyEqual(end1, pointsCloseToEnd1, tolerance))
                    {
                        // Debug.Log("c1");
                        // edges[ai] = new Edge(end1, pointsCloseToEnd1);               // do not create empty edge
                        edges[ai] = new Edge(pointsCloseToEnd1, pointsCloseToEnd2);
                        edges[bi] = new Edge(end2, pointsCloseToEnd2);
                    }
                    else if (GeometryUtils.NearlyEqual(end2, pointsCloseToEnd2, tolerance))
                    {
                        // Debug.Log("c2");
                        // edges[ai] = new Edge(end2, pointsCloseToEnd2);               // do not create empty edge
                        edges[ai] = new Edge(pointsCloseToEnd1, pointsCloseToEnd2);
                        edges[bi] = new Edge(end1, pointsCloseToEnd1);
                    }
                    else
                    {
                        // Debug.Log("c3");
                        edges[ai] = new Edge(end1, pointsCloseToEnd1);
                        edges[bi] = new Edge(end2, pointsCloseToEnd2);
                        edges.Add(new Edge(pointsCloseToEnd1, pointsCloseToEnd2));
                    }
                }
            }

            return;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void SplitEdge(Edge e, float2 p, int index)
            {
                if (!GeometryUtils.NearlyEqual(e.A, p, tolerance))
                {
                    edges[index] = new(e.A, p);

                    if (!GeometryUtils.NearlyEqual(e.B, p, tolerance))
                    {
                        edges.Add(new(e.B, p));
                    }
                }
                else
                {
                    edges[index] = new(e.B, p);

                    if (!GeometryUtils.NearlyEqual(e.A, p, tolerance))
                    {
                        edges.Add(new(e.A, p));
                    }
                }
            }
        }

        /// <summary>
        /// Expands (offsets) a simple polygon outward by given radius.
        /// For clockwise verts and positive radius polygon will be expanded.
        /// Returns expanded polygon in the same order.
        /// </summary>
        public static void ExpandPolygon(NativeList<float2> polygon, float radius)
        {
            float2 start = polygon[0];
            float2 prev = polygon[^1];
            int count = polygon.Length - 1;
            for (int i = 0; i < count; i++)
            {
                float2 curr = polygon[i];
                float2 next = polygon[i + 1];

                ExpandVertex(curr, prev, next, radius, out float2 expanded);
                polygon[i] = expanded;
                
                prev = curr;
            }
            ExpandVertex(polygon[^1], prev, start, radius, out float2 lastExpanded);
            polygon[^1] = lastExpanded;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExpandVertex(float2 curr, float2 prev, float2 next, float radius, out float2 expanded)
        {
            // Edge directions
            float2 dirPrev = math.normalize(curr - prev);
            float2 dirNext = math.normalize(next - curr);

            // Outward normals
            float2 normalPrev = new(-dirPrev.y, dirPrev.x);
            float2 normalNext = new(-dirNext.y, dirNext.x);

            // Compute bisector
            float2 bisector = math.normalize(normalPrev + normalNext);

            // Angle between edges
            float cosTheta = math.clamp(math.dot(-dirPrev, dirNext), -1f, 1f);
            float theta = math.acos(cosTheta);

            // Distance along bisector
            float dist = radius / math.sin(theta * 0.5f);

            expanded = curr + bisector * dist;
        }

        // public static List<float2> PolygonIntersection(IReadOnlyList<float2> polyA, IReadOnlyList<float2> polyB)
        // {
        //     if (polyA.Count < 3 || polyB.Count < 3)
        //     {
        //         return new();
        //     }
        //
        //     // Start with all vertices from polyA
        //     var output = new List<float2>(polyA);
        //
        //     // Clip against each edge of polyB
        //     for (int i = 0; i < polyB.Count; i++)
        //     {
        //         float2 clipA = polyB[i];
        //         float2 clipB = polyB[(i + 1) % polyB.Count];
        //
        //         List<float2> input = output;
        //         output = new();
        //
        //         if (input.Count == 0)
        //         {
        //             break;
        //         }
        //
        //         float2 s = input[^1];
        //         for (int j = 0; j < input.Count; j++)
        //         {
        //             float2 e = input[j];
        //
        //             bool eInside = IsInside(clipA, clipB, e);
        //             bool sInside = IsInside(clipA, clipB, s);
        //
        //             if (eInside)
        //             {
        //                 if (!sInside)
        //                 {
        //                     output.Add(GeometryUtils.IntersectionPoint(s, e, clipA, clipB));
        //                 }
        //
        //                 output.Add(e);
        //             }
        //             else if (sInside)
        //             {
        //                 output.Add(GeometryUtils.IntersectionPoint(s, e, clipA, clipB));
        //             }
        //
        //             s = e;
        //         }
        //     }
        //     
        //     // RemoveDuplicates
        //     for (int i = 0; i < output.Count; i++)
        //     {
        //         float2 p = output[i];
        //         for (int j = i + 1; j < output.Count; j++)
        //         {
        //             if (math.lengthsq(p - output[j]) < .0001f)
        //             {
        //                 output.RemoveAt(j);
        //                 j--;
        //             }
        //         }
        //     }
        //         
        //     return output;
        //     
        //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
        //     static bool IsInside(float2 a, float2 b, float2 p) =>
        //         // Left-of test for AB -> point
        //         GeometryUtils.Cross(b - a, p - a) >= 0f;
        // }
    }
}