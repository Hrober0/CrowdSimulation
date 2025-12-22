using System.Runtime.CompilerServices;
using andywiecko.BurstTriangulator;
using andywiecko.BurstTriangulator.LowLevel.Unsafe;
using HCore.Extensions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    [BurstCompile]
    public struct NavMeshUpdateJob<T> : IJob  where T : unmanaged, INodeAttributes<T>
    {
        private const float MIN_POINT_DISTANCE = .001f;
        private const int DEFAULT_CAPACITY = 128;
        private const int AVERAGE_OBSTACLE_VERTICES_CAPACITY = 16;
        
        public NavMesh<T> NavMesh;
        
        [ReadOnly] public NavObstacles<T> NavObstacles;
        [ReadOnly] public float2 UpdateMin;
        [ReadOnly] public float2 UpdateMax;
        
        public void Execute()
        {
            // Remove nodes
            using var removedNodes = new NativeList<Triangle>(DEFAULT_CAPACITY, Allocator.Temp);
            NavMesh.RemoveNodes(UpdateMin, UpdateMax, removedNodes);
            
            if (removedNodes.Length == 0)
            {
                Debug.LogWarning("Update outside NavMesh");
                return;
            }

            // Get removed area border
            using var borderEdges = new NativeList<EdgeKey>(DEFAULT_CAPACITY, Allocator.Temp);
            PolygonUtils.GetEdgesUnordered(in removedNodes, borderEdges, tolerance: MIN_POINT_DISTANCE);
            
            // Create CCS border points
            using var borderPointsCCW = new NativeList<float2>(DEFAULT_CAPACITY, Allocator.Temp);
            var isLoopClose = PolygonUtils.GetPointsCCW(in borderEdges, borderPointsCCW, false);

            // DebugBorder(borderEdges);
            // DebugBorder(borderEdges);
            
            // Validate border creation
            if (borderPointsCCW.Length < borderEdges.Length - 1)
            {
                Debug.LogWarning($"Border points count is less than border points {borderPointsCCW.Length} < {borderEdges.Length}");
                AddNodes(in removedNodes);
                return;
            }
            if (!isLoopClose)
            {
                Debug.LogWarning("Loop is not closed");
                AddNodes(in removedNodes);
                return;
            }
            
            // Create CCS border points
            borderEdges.Clear();
            PolygonUtils.ReduceEdges(in borderPointsCCW, borderEdges, UpdateMin, UpdateMax, toleration: MIN_POINT_DISTANCE);
            
            // Fix min max
            float2 newUpdateMin = math.min(borderEdges[0].A, borderEdges[0].B);
            float2 newUpdateMax = math.max(borderEdges[0].A, borderEdges[0].B);
            for (var i = 1; i < borderEdges.Length; i++)
            {
                EdgeKey b = borderEdges[i];
                newUpdateMin = math.min(newUpdateMin, math.min(b.A, b.B));
                newUpdateMax = math.max(newUpdateMax, math.max(b.A, b.B));
            }
            
            // DebugBorder(borderEdges);
            
            // Get obstacle inside border
            using var obstaclesParts = new NativeList<NavObstacles<T>.IndexedTriangle>(DEFAULT_CAPACITY, Allocator.Temp);
            NavObstacles.ObstacleLookup.QueryAABB(newUpdateMin, newUpdateMax, obstaclesParts);
            using var obstacleIndexes = new NativeHashSet<int>(obstaclesParts.Length, Allocator.Temp);
            foreach (var indexedTriangle in obstaclesParts)
            {
                obstacleIndexes.Add(indexedTriangle.Index);
            }
            
            // Inside edges should reflect obstacle and border at removed area
            var expectedInsideEdgesCapacity = obstacleIndexes.Count * AVERAGE_OBSTACLE_VERTICES_CAPACITY + borderEdges.Length;
            using var insideEdges = new NativeList<Edge>(expectedInsideEdgesCapacity, Allocator.Temp);
            
            // Add borderEdges to insideEdges
            foreach (var edge in borderEdges)
            {
                insideEdges.Add(new(edge.A, edge.B));    
            }
            
            // Add obstacle edges to insideEdges
            using var intersectionBuffer = new NativeList<float2>(borderEdges.Length, Allocator.Temp);
            foreach (var obstacleIndex in obstacleIndexes)
            {
                foreach (Edge obstacleEdge in NavObstacles.ObstacleEdges.GetValuesForKey(obstacleIndex))
                {
                    PolygonUtils.CutEdgeToArea(obstacleEdge, borderEdges, insideEdges, intersectionBuffer);
                }
            }

            // DebugInsideEdges(insideEdges);
            
            // Cut edges to avoid intersection
            PolygonUtils.CutIntersectingEdges(insideEdges, MIN_POINT_DISTANCE * 0.5f);
            
            // DebugInsideEdges(insideEdges);
            
            // Prepare triangulation input
            using var positions = new NativeList<float2>(insideEdges.Length * 2, Allocator.Temp);
            using var constraintEdges = new NativeList<int>(insideEdges.Length * 4, Allocator.Temp);
            using var constrainCheck = new NativeHashSet<EdgeKey>(insideEdges.Length, Allocator.Temp);
            foreach (var edge in borderEdges)
            {
                // First add border points to make sure that they will not be aligned to other points
                AddPosition(edge.A);
                AddPosition(edge.B);
            }
            foreach (var edge in insideEdges)
            {
                var ai = AddPosition(edge.A);
                var bi = AddPosition(edge.B);
                
                if (ai == bi)
                {
                    // IDK why but it can happen, but it doesn't break anything
                    // Debug.LogWarning($"Detected edge build with the same points {edge}");
                    continue;
                }
                
                var key = new EdgeKey(ai, bi);
                if (!constrainCheck.Add(key))
                {
                    // IDK why but it can happen, but it doesn't break anything
                    // Debug.LogWarning($"Detected duplicated edge {edge}");
                    continue;
                }
                
                constraintEdges.Add(ai);
                constraintEdges.Add(bi);
            }
            
            // DebugConstraints(constraintEdges, positions);

            // Triangulate
            using var outputTriangles = new NativeList<int>(positions.Length * 3, Allocator.Temp);
            using var triangulationStatus = new NativeReference<Status>(Allocator.Temp);
            new UnsafeTriangulator<float2>().Triangulate(
                input: new()
                {
                    Positions = positions.AsArray(),
                    ConstraintEdges = constraintEdges.AsArray(),
                },
                output: new()
                {
                    Triangles = outputTriangles,
                    Status = triangulationStatus,
                },
                args: Args.Default(
                    verbose: true,         // false to not throw errors, because unity can stop on error inside job, what's lead to unity memory corruption
                    validateInput: true
                    ), 
                allocator: Allocator.Temp
            );

            if (triangulationStatus.Value != Status.OK)
            {
                Debug.LogWarning($"Triangulation status returned {triangulationStatus.Value}");
                AddNodes(in removedNodes);
                return;
            }

            if (outputTriangles.Length == 0)
            {
                Debug.LogWarning($"Triangulation returned no triangles");
                AddNodes(in removedNodes);
                return;
            }
            
            // Create nodes
            using var newNodes = new NativeList<Triangle>(outputTriangles.Length / 3,Allocator.Temp);
            for (int i = 0; i < outputTriangles.Length; i += 3)
            {
                var triangle = new Triangle(
                    positions[outputTriangles[i]],
                    positions[outputTriangles[i + 1]],
                    positions[outputTriangles[i + 2]]);
                
                // triangle.DrawBorder(Color.green, 5);
                // triangle.GetCenter.To3D().DrawPoint(Color.green, 5, .1f);

                // Do not add triangles outside border
                if (!PolygonUtils.IsPointInPolygon(triangle.GetCenter, in borderEdges))
                {
                    // triangle.GetCenter.To3D().DrawPoint(Color.red, 2, .5f);
                    continue;
                }

                newNodes.Add(triangle);
            }
            AddNodes(in newNodes);

            return;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int AddPosition(float2 p)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    if (math.lengthsq(p - positions[i]) < MIN_POINT_DISTANCE)
                    {
                        return i;
                    }
                }
            
                positions.Add(p);
                return positions.Length - 1;
            }
        }
        
        private void AddNodes(in NativeList<Triangle> newNodes)
        {
            using var obstaclesAtNode = new NativeList<NavObstacles<T>.IndexedTriangle>(16,Allocator.Temp);
            var nodeConstructor = new T();
            foreach (var triangle in newNodes)
            {
                T attributes = nodeConstructor.Empty();
                obstaclesAtNode.Clear();
                float2 center = triangle.GetCenter;
                NavObstacles.ObstacleLookup.QueryPoint(center, obstaclesAtNode);
                foreach (var indexed in obstaclesAtNode)
                {
                    if (Triangle.PointIn(center, indexed.Triangle.A, indexed.Triangle.B, indexed.Triangle.C))
                    {
                        attributes.Merge(NavObstacles.Obstacles[indexed.Index].Attributes);
                    }
                }
                
                NavMesh.AddNode(new(triangle, attributes));
            }
        }

        private static void DebugBorder(NativeList<EdgeKey> borderEdges)
        {
            var center = float2.zero;
            foreach (EdgeKey edge in borderEdges)
            {
                center += edge.A;
                center += edge.B;
            }
            center /= math.max(borderEdges.Length * 2, 1);
            center.To3D().DrawPoint(Color.cyan, 15, .1f);
            Debug.Log($"Update Center: {center}");
            foreach (EdgeKey edge in borderEdges)
            {
                DebugUtils.DrawWithOffset(edge.A, edge.B, center, Color.cyan, 15);
                Debug.Log($"Edge {edge.A} - {edge.B}");
            }
        }
        
        private static void DebugInsideEdges(NativeList<Edge> edges)
        {
            foreach (Edge edge in edges)
            {
                DebugUtils.Draw(edge.A, edge.B, Color.magenta, 15);
                Debug.Log($"Edge {edge.A} - {edge.B}");
            }
        }
        
        private static void DebugBorder(NativeList<float2> borderPoints)
        {
            borderPoints.AsArray().DrawLoop(Color.green, 5);
            foreach (var p in borderPoints)
            {
                Debug.Log($"P {p}");
            }
        }

        private static void DebugConstraints(NativeList<int> constraintEdges, NativeList<float2> positions)
        {
            for (var index = 0; index < constraintEdges.Length; index+=2)
            {
                var p1 = positions[constraintEdges[index]];
                var p2 = positions[constraintEdges[index + 1]];
                Debug.DrawLine(p1.To3D(), p2.To3D(), Color.red, 5);
            }
            foreach (var p in positions)
            {
                p.To3D().DrawPoint(Color.blue, 5, .1f);
            }
        }
    }
}