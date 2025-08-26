using System.Collections.Generic;
using System.Runtime.CompilerServices;
using andywiecko.BurstTriangulator;
using andywiecko.BurstTriangulator.LowLevel.Unsafe;
using CustomNativeCollections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    [BurstCompile]
    public struct NaveMeshUpdateJob<T> : IJob  where T : unmanaged, INodeAttributes<T>
    {
        private const float MIN_POINT_DISTANCE = .001f;
        private const int DEFAULT_CAPACITY = 128;
        private const int AVERAGE_OBSTACLE_VERTICES_CAPACITY = 16;
        
        public float2 UpdateMin;
        public float2 UpdateMax;

        public NavMesh<T> NavMesh;

        [ReadOnly]
        public NavObstacles<T> NavObstacles;
        
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
            using var unorderedBorderEdges = new NativeList<EdgeKey>(DEFAULT_CAPACITY, Allocator.Temp);
            PolygonUtils.GetEdgesUnordered(in removedNodes, unorderedBorderEdges, tolerance: MIN_POINT_DISTANCE);
            using var borderPointsCCW = new NativeList<float2>(DEFAULT_CAPACITY, Allocator.Temp);
            var isLoopClose = PolygonUtils.GetPointsCCW(in unorderedBorderEdges, borderPointsCCW);
            if (borderPointsCCW.Length < unorderedBorderEdges.Length - 1)
            {
                Debug.LogWarning($"Border points count is less than border points {borderPointsCCW.Length} < {unorderedBorderEdges.Length}");
                ReAddNodesOnError(NavMesh, in removedNodes, in NavObstacles);
                return;
            }
            if (!isLoopClose)
            {
                Debug.LogWarning("Loop is not closed");
                ReAddNodesOnError(NavMesh, in removedNodes, in NavObstacles);
                return;
            }
            
            using var borderEdgesCCW = new NativeList<Edge>(unorderedBorderEdges.Length, Allocator.Temp);
            PolygonUtils.ReduceEdges(in borderPointsCCW, borderEdgesCCW, toleration: MIN_POINT_DISTANCE);
            
            // fixedBorderEdges.Clear();
            // foreach (var edge in borderEdges)
            // {
            //     fixedBorderEdges.Add(new(edge.A, edge.B));
            // }
            
            // Get obstacle inside border
            using var obstaclesParts = new NativeList<NavObstacles<T>.IndexedTriangle>(DEFAULT_CAPACITY, Allocator.Temp);
            NavObstacles.ObstacleLookup.QueryAABB(UpdateMin, UpdateMax, obstaclesParts);
            using var obstacleIndexes = new NativeHashSet<int>(obstaclesParts.Length, Allocator.Temp);
            foreach (var indexedTriangle in obstaclesParts)
            {
                obstacleIndexes.Add(indexedTriangle.Index);
            }
            
            // Inside edges should reflect obstacle and border at removed area
            var expectedInsideEdgesCapacity = obstacleIndexes.Count * AVERAGE_OBSTACLE_VERTICES_CAPACITY + borderEdgesCCW.Length;
            using var insideEdges = new NativeList<Edge>(expectedInsideEdgesCapacity, Allocator.Temp);
            
            // Add borderEdges to insideEdges
            insideEdges.CopyFrom(in borderEdgesCCW);
            
            // Add obstacle edges to insideEdges
            using var intersectionBuffer = new NativeList<float2>(borderEdgesCCW.Length, Allocator.Temp);
            foreach (var obstacleIndex in obstacleIndexes)
            {
                foreach (Edge obstacleEdge in NavObstacles.ObstacleEdges.GetValuesForKey(obstacleIndex))
                {
                    bool isAInside = IsPointInPolygon(in obstacleEdge.A, in removedNodes);
                    bool isBInside = IsPointInPolygon(in obstacleEdge.B, in removedNodes);

                    if (isAInside && isBInside)
                    {
                        // Inside polygon
                        insideEdges.Add(new Edge(obstacleEdge.A, obstacleEdge.B));
                        continue;
                    }

                    if (!isAInside && !isBInside)
                    {
                        // Outside polygon
                        continue;
                    }
                    
                    intersectionBuffer.Clear();
                    foreach (Edge borderEdge in borderEdgesCCW)
                    {
                        if (GeometryUtils.TryIntersect(borderEdge.A, borderEdge.B, obstacleEdge.A, obstacleEdge.B, out float2 intersectionPoint))
                        {
                            intersectionBuffer.Add(intersectionPoint);
                        }
                    }

                    if (intersectionBuffer.Length == 0)
                    {
                        Debug.LogWarning($"{obstacleEdge} {isAInside} {isBInside} should intersect border!");
                        continue;
                    }

                    var insidePoint = isAInside ? obstacleEdge.A : obstacleEdge.B;
                    if (intersectionBuffer.Length == 1)
                    {
                        insideEdges.Add(new Edge(intersectionBuffer[0], insidePoint));
                        continue;
                    }
                    
                    intersectionBuffer.Add(insidePoint);
                    intersectionBuffer.Sort(new PointComparer{Reference = insidePoint});
                    for (int i = 0; i < intersectionBuffer.Length - 1; i++)
                    {
                        insideEdges.Add(new Edge(intersectionBuffer[i], intersectionBuffer[i + 1]));
                    }
                }
            }
            
            // Cut edges to avoid intersection
            PolygonUtils.CutIntersectingEdges(insideEdges, MIN_POINT_DISTANCE * 0.5f);
            
            // Prepare triangulation input
            using var positions = new NativeList<float2>(insideEdges.Length * 2, Allocator.Temp);
            using var constraintEdges = new NativeList<int>(insideEdges.Length * 4, Allocator.Temp);
            using var constrainCheck = new NativeHashSet<EdgeKey>(insideEdges.Length, Allocator.Temp);
            foreach (var edge in borderEdgesCCW)
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

            // for (var index = 0; index < constraintEdges.Length; index+=2)
            // {
            //     var p1 = positions[constraintEdges[index]];
            //     var p2 = positions[constraintEdges[index + 1]];
            //     Debug.DrawLine(p1.To3D(), p2.To3D(), Color.green);
            // }
            // foreach (var p in positions)
            // {
            //     p.To3D().DrawPoint(Color.blue, 5, .1f);
            // }

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
                    validateInput: false    // there is an error with input validation https://github.com/andywiecko/BurstTriangulator/issues/384
                    ), 
                allocator: Allocator.Temp
            );

            if (triangulationStatus.Value != Status.OK)
            {
                Debug.LogWarning($"Triangulation status returned {triangulationStatus.Value}");
                
                // string pos = "";
                // foreach (var position in positions.AsArray())
                // {
                //     var x = $"{position.x:g9}f".Replace(",", ".");
                //     var y = $"{position.y:g9}f".Replace(",", ".");
                //     pos += $"new({x}, {y}),\n";
                // }
                // Debug.Log($"Positions: \n{pos}");
                //
                // var cons = "";
                // foreach (var edge in constraintEdges)
                // {
                //     cons += edge + ", ";
                // }
                // Debug.Log($"Constrains: \n{cons}");

                ReAddNodesOnError(NavMesh, in removedNodes, in NavObstacles);
                return;
            }

            if (outputTriangles.Length == 0)
            {
                Debug.LogWarning($"Triangulation returned no triangles");
                ReAddNodesOnError(NavMesh, in removedNodes, in NavObstacles);
                return;
            }
            
            // Create nodes
            using var obstaclesAtNode = new NativeList<NavObstacles<T>.IndexedTriangle>(16,Allocator.Temp);
            var nodeConstructor = new T();
            for (int i = 0; i < outputTriangles.Length; i += 3)
            {
                var triangle = new Triangle(
                    positions[outputTriangles[i]],
                    positions[outputTriangles[i + 1]],
                    positions[outputTriangles[i + 2]]);
                
                // triangle.DrawBorder(Color.green, 5);
                // triangle.GetCenter.To3D().DrawPoint(Color.green, 5, .1f);

                // Do not add triangles outside border
                if (!PolygonUtils.IsPointInPolygon(triangle.GetCenter, in unorderedBorderEdges))
                {
                    // triangle.GetCenter.To3D().DrawPoint(Color.red, 2, .5f);
                    continue;
                }

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
            
            // Debug
            // foreach (var e in borderEdges)
            // {
            //     Debug.DrawLine(math.float3(e.A, 0), math.float3(e.B, 0), Color.green);
            // }

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
        
        private static bool IsPointInPolygon(in float2 point, in NativeList<Triangle> triangles)
        {
            foreach (Triangle areaTriangle in triangles)
            {
                if (Triangle.PointIn(point, areaTriangle.A, areaTriangle.B, areaTriangle.C))
                {
                    return true;
                }
            }

            return false;
        }
        
        private static void ReAddNodesOnError(NavMesh<T> navMesh, in NativeList<Triangle> removedNodes, in NavObstacles<T> navObstacles)
        {
            using var obstaclesAtNode = new NativeList<NavObstacles<T>.IndexedTriangle>(16,Allocator.Temp);
            var nodeConstructor = new T();
            foreach (var triangle in removedNodes)
            {
                T attributes = nodeConstructor.Empty();
                obstaclesAtNode.Clear();
                float2 center = triangle.GetCenter;
                navObstacles.ObstacleLookup.QueryPoint(center, obstaclesAtNode);
                foreach (var indexed in obstaclesAtNode)
                {
                    if (Triangle.PointIn(center, indexed.Triangle.A, indexed.Triangle.B, indexed.Triangle.C))
                    {
                        attributes.Merge(navObstacles.Obstacles[indexed.Index].Attributes);
                    }
                }
                
                navMesh.AddNode(new(triangle, attributes));
            }
        }
        
        private struct PointComparer : IComparer<float2>
        {
            public float2 Reference;

            public int Compare(float2 a, float2 b)
            {
                float da = math.lengthsq(a - Reference);
                float db = math.lengthsq(b - Reference);
                return da.CompareTo(db); // ascending order
            }
        }
    }
}