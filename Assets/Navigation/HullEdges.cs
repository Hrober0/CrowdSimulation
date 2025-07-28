using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public static class HullEdges
    {
        public static HashSet<Vector2> GetPointsUnordered(List<NavNode> triangles)
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
            var borderPoints = new HashSet<Vector2>(edgeCounts.Count);
            foreach (var kvp in edgeCounts)
            {
                if (kvp.Value == 1)
                {
                    borderPoints.Add(kvp.Key.A);
                    borderPoints.Add(kvp.Key.B);
                }
            }

            return borderPoints;
        }
        
        public static List<Vector2> GetPointsCCW(List<Triangle> triangles)
        {
            var edgeCounts = new Dictionary<EdgeKey, int>(triangles.Count * 3);

            // Count all triangle edges
            foreach (var tr in triangles)
            {
                AddEdge(edgeCounts, new(tr.A, tr.B));
                AddEdge(edgeCounts, new(tr.A, tr.C));
                AddEdge(edgeCounts, new(tr.B, tr.C));
            }
            
            // Filter outer boundary edges (appear only once)
            using var edgeMap = new NativeParallelMultiHashMap<float2, EdgeKey>(edgeCounts.Count * 2, Allocator.Temp);
            EdgeKey? startEdge = null;
            foreach ((EdgeKey edge, int edgeCount) in edgeCounts)
            {
                if (edgeCount == 1)
                {
                    startEdge ??= edge;
                    edgeMap.Add(edge.A, edge);
                    edgeMap.Add(edge.B, edge);
                }
            }
            
            var loop = new List<Vector2>();
            if (startEdge == null)
            {
                return loop;
            }

            // Reconstruct ordered boundary loop
            float2 startVert = startEdge.Value.A;
            float2 currentVert = startVert;
            float2 nextVert = startEdge.Value.B;
            loop.Add(currentVert);
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