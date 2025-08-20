using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public static class PolygonUtils
    {
        private const float EPSILON = 1e-6f;
        
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
        public static void GetEdgesUnordered(in NativeList<Triangle> triangles, NativeList<EdgeKey> borderEdges)
        {
            var edgeCounts = new NativeHashMap<EdgeKey, int>(triangles.Length * 3, Allocator.Temp);

            // Count all triangle edges
            foreach (Triangle tr in triangles)
            {
                {
                    var edge = new EdgeKey(tr.A, tr.B);
                    if (edgeCounts.TryGetValue(edge, out int count))
                    {
                        edgeCounts[edge] = count + 1;
                    }
                    else
                    {
                        edgeCounts[edge] = 1;
                    }
                }
                {
                    var edge = new EdgeKey(tr.B, tr.C);
                    if (edgeCounts.TryGetValue(edge, out int count))
                    {
                        edgeCounts[edge] = count + 1;
                    }
                    else
                    {
                        edgeCounts[edge] = 1;
                    }
                }
                {
                    var edge = new EdgeKey(tr.C, tr.A);
                    if (edgeCounts.TryGetValue(edge, out int count))
                    {
                        edgeCounts[edge] = count + 1;
                    }
                    else
                    {
                        edgeCounts[edge] = 1;
                    }
                }
            }

            // Gather unique boundary points (appear only once)
            foreach (var kvp in edgeCounts)
            {
                if (kvp.Value == 1)
                {
                    borderEdges.Add(kvp.Key);
                }
            }
            
            edgeCounts.Dispose();
        }
        
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
                            output.Add(GeometryUtils.IntersectionPoint(s, e, clipA, clipB));
                        }

                        output.Add(e);
                    }
                    else if (sInside)
                    {
                        output.Add(GeometryUtils.IntersectionPoint(s, e, clipA, clipB));
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
                GeometryUtils.Cross(b - a, p - a) >= 0f;
        }
        
        public static void CutIntersectingEdges(NativeList<Edge> edges)
        {
            for (int ai = 0; ai < edges.Length; ai++)
            {
                for (int bi = ai + 1; bi < edges.Length; bi++)
                {
                    Edge a = edges[ai];
                    Edge b = edges[bi];
                    if (GeometryUtils.TryIntersect(a.A, a.B, b.A, b.B, out float2 p))
                    {
                        SplitEdge(a, p, ai);
                        SplitEdge(b, p, bi);
                    }
                }
            }

            return;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void SplitEdge(Edge e, float2 p, int index)
            {
                if (math.distancesq(e.A, p) > EPSILON)
                {
                    edges[index] = new(e.A, p);

                    if (math.distancesq(e.B, p) > EPSILON)
                    {
                        edges.Add(new(e.B, p));
                    }
                }
                else
                {
                    edges[index] = new(e.B, p);

                    if (math.distancesq(e.A, p) > EPSILON)
                    {
                        edges.Add(new(e.A, p));
                    }
                }
            }
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