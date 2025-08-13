using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public static class PolygonUtils
    {
        public static bool IsPointInPolygon(float2 point, List<EdgeKey> polygon)
        {
            int crossings = 0;
        
            for (int i = 0; i < polygon.Count; i++)
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

        public static float2 PolygonCenter(List<EdgeKey> polygon)
        {
            var sum = float2.zero;
            for (int i = 0; i < polygon.Count; i++)
            {
               sum += polygon[i].A;
               sum += polygon[i].B;
            }
            return sum / (polygon.Count * 2);
        }
        
        public static List<EdgeKey> GetEdgesUnordered(List<NavNode> triangles)
        {
            var edgeCounts = new Dictionary<EdgeKey, int>(triangles.Count * 3);

            // Count all triangle edges
            foreach (NavNode tr in triangles)
            {
                AddEdge(edgeCounts, new(tr.CornerA, tr.CornerB));
                AddEdge(edgeCounts, new(tr.CornerA, tr.CornerC));
                AddEdge(edgeCounts, new(tr.CornerB, tr.CornerC));
            }

            // Gather unique boundary points (appear only once)
            var borderEdges = new List<EdgeKey>(edgeCounts.Count);
            foreach (var kvp in edgeCounts)
            {
                if (kvp.Value == 1)
                {
                    borderEdges.Add(kvp.Key);
                }
            }

            return borderEdges;
        }
        public static List<EdgeKey> GetEdgesUnordered(List<Triangle> triangles)
        {
            var edgeCounts = new Dictionary<EdgeKey, int>(triangles.Count * 3);

            // Count all triangle edges
            foreach (Triangle tr in triangles)
            {
                AddEdge(edgeCounts, new(tr.A, tr.B));
                AddEdge(edgeCounts, new(tr.A, tr.C));
                AddEdge(edgeCounts, new(tr.B, tr.C));
            }

            // Gather unique boundary points (appear only once)
            var borderEdges = new List<EdgeKey>(edgeCounts.Count);
            foreach (var kvp in edgeCounts)
            {
                if (kvp.Value == 1)
                {
                    borderEdges.Add(kvp.Key);
                }
            }

            return borderEdges;
        }
        
        // public static List<float2> GetPointsUnordered(List<EdgeKey> edges)
        // {
        //     var points = new List<float2>();
        //     foreach (EdgeKey edge in edges)
        //     {
        //         AddPoint(points, edge.A);
        //         AddPoint(points, edge.B);
        //     }
        //
        //     return points;
        // }
        
        public static List<float2> GetPointsCCW(List<Triangle> triangles) => GetPointsCCW(GetEdgesUnordered(triangles));
        public static List<float2> GetPointsCCW(List<EdgeKey> edges)
        {
            if (edges.Count == 0)
            {
                return new();
            }
            
            if (edges.Count == 1)
            {
                return new()
                {
                    edges[0].A,
                    edges[0].B,
                };
            }
            
            // Filter outer boundary edges (appear only once)
            using var edgeMap = new NativeParallelMultiHashMap<float2, EdgeKey>(edges.Count * 2, Allocator.Temp);
            foreach (EdgeKey edge in edges)
            {
                edgeMap.Add(edge.A, edge);
                edgeMap.Add(edge.B, edge);
            }
            

            // Reconstruct ordered boundary loop
            float2 startVert = edges[0].A;
            float2 currentVert = startVert;
            float2 nextVert = edges[0].B;
            var loop = new List<float2>()
            {
                currentVert
            };
            while (!nextVert.Equals(startVert)) // until we close the loop
            {
                loop.Add(nextVert);
                
                EdgeKey? found = null;
                foreach (EdgeKey candidate in edgeMap.GetValuesForKey(nextVert))
                {
                    if (candidate.B.Equals(currentVert) || candidate.A.Equals(currentVert))
                    {
                        // candidate is the same edge as current
                        continue;
                    }
                    
                    found = candidate;
                    break;
                }

                if (found == null)
                {
                    Debug.LogWarning($"{nameof(GetPointsCCW)}: Edge not found");
                    break;
                }

                EdgeKey edge = found.Value;
                if (edge.A.Equals(nextVert))
                {
                    currentVert = edge.A;
                    nextVert = edge.B;
                }
                else
                {
                    currentVert = edge.B;
                    nextVert = edge.A;
                }
            }
            
            // Make sure that points are sorted CCW
            if (!Triangle.IsCCW(loop[0], loop[1], loop[2]))
            {
                loop.Reverse();
            }

            return loop;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddEdge(Dictionary<EdgeKey, int> dict, EdgeKey edge)
        {
            if (dict.TryGetValue(edge, out int count))
            {
                dict[edge] = count + 1;
            }
            else
            {
                dict[edge] = 1;
            }
        }
    }
}