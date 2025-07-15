using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public static class HullEdges
    {
        public static HashSet<Vector2> GetHullEdgesPointsUnordered(List<NavNode> triangles)
        {
            var edgeCount = new Dictionary<EdgeKey, int>();

            foreach (NavNode tr in triangles)
            {
                AddEdge(edgeCount, new(tr.CornerA, tr.CornerB));
                AddEdge(edgeCount, new(tr.CornerA, tr.CornerC));
                AddEdge(edgeCount, new(tr.CornerB, tr.CornerC));
            }

            var borderPoints = new HashSet<Vector2>();
            foreach (var kvp in edgeCount)
            {
                // if edge appears only one time it means that is not border edge
                if (kvp.Value == 1)
                {
                    borderPoints.Add(kvp.Key.A);
                    borderPoints.Add(kvp.Key.B);
                }
            }

            return borderPoints;
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