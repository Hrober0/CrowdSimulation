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
    public struct NaveMeshUpdateJob : IJob
    {
        private const float MIN_POINT_DISTANCE = .001f;
        private const int DEFAULT_CAPACITY = 128;
        private const int AVERAGE_OBSTACLE_VERTICES_CAPACITY = 16;
        
        public float2 UpdateMin;
        public float2 UpdateMax;

        public NavMesh NavMesh;

        [ReadOnly]
        public NavObstacles NavObstacles;

        // TODO: find why sometimes "Sloan max iterations exceeded"
        
        public void Execute()
        {
            // Remove nodes
            using var removedNodes = new NativeList<Triangle>(DEFAULT_CAPACITY, Allocator.Temp);
            NavMesh.RemoveNodes(UpdateMin, UpdateMax, removedNodes);

            // Get removed area border
            using var borderEdges = new NativeList<EdgeKey>(DEFAULT_CAPACITY, Allocator.Temp);
            PolygonUtils.GetEdgesUnordered(in removedNodes, borderEdges);
            using var borderPoints = new NativeList<float2>(DEFAULT_CAPACITY, Allocator.Temp);
            var isLoopClose = PolygonUtils.GetPointsCCW(in borderEdges, borderPoints);
            if (!isLoopClose)
            {
                Debug.LogWarning($"Loop is not closed");
            }

            // Get obstacle inside border
            using var obstaclesParts = new NativeList<NavObstacles.IndexedTriangle>(DEFAULT_CAPACITY, Allocator.Temp);
            NavObstacles.ObstacleLookup.QueryAABB(UpdateMin, UpdateMax, obstaclesParts);
            using var obstacleIndexes = new NativeHashSet<int>(obstaclesParts.Length, Allocator.Temp);
            foreach (var indexedTriangle in obstaclesParts)
            {
                obstacleIndexes.Add(indexedTriangle.Index);
            }
            
            // Inside edges should reflect obstacle and border at removed area
            var expectedInsideEdgesCapacity = obstacleIndexes.Count * AVERAGE_OBSTACLE_VERTICES_CAPACITY + borderEdges.Length;
            using var insideEdges = new NativeList<Edge>(expectedInsideEdgesCapacity, Allocator.Temp);
            
            // Add borderEdges to insideEdges
            PolygonUtils.ReduceEdges(in borderPoints, insideEdges, toleration: MIN_POINT_DISTANCE);
            
            // Add obstacle edges to insideEdges
            foreach (var obstacleIndex in obstacleIndexes)
            {
                foreach (EdgeKey edge in NavObstacles.ObstacleEdges.GetValuesForKey(obstacleIndex))
                {
                    bool isAInside = IsPointInPolygon(in edge.A, in removedNodes);
                    bool isBInside = IsPointInPolygon(in edge.B, in removedNodes);

                    if (isAInside && isBInside)
                    {
                        insideEdges.Add(new Edge(edge.A, edge.B));
                    }
                    else if (isAInside || isBInside)
                    {
                        // Get only part of edge that is inside
                        if (BorderIntersection(in edge.A, in edge.B, in borderEdges, out float2 intersectionPoint))
                        {
                            float2 insidePoint = isAInside ? edge.A : edge.B;
                            insideEdges.Add(new Edge(intersectionPoint, insidePoint));
                        }
                        else
                        {
                            Debug.LogWarning($"One of edge point is inside border but intersection point between edge {edge} and border was not found");
                            insideEdges.Add(new Edge(edge.A, edge.B));
                        }
                    }
                }
            }
            
            // Cut edges to avoid intersection
            PolygonUtils.CutIntersectingEdges(insideEdges);
            
            // Prepare triangulation input
            using var positions = new NativeList<float2>(insideEdges.Length * 2, Allocator.Temp);
            using var constraintEdges = new NativeList<int>(insideEdges.Length * 4, Allocator.Temp);
            using var constrainCheck = new NativeHashSet<EdgeKey>(insideEdges.Length, Allocator.Temp);
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
            using var triangulationStatus = new NativeReference<andywiecko.BurstTriangulator.Status>(Allocator.Temp);
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
                    restoreBoundary: true,  // to not create connection outside area
                    verbose: false,         // false to not throw errors, because unity can stop on error inside job, what's lead to unity memory corruption
                    validateInput: false    // there is an error with input validation https://github.com/andywiecko/BurstTriangulator/issues/384
                    ), 
                allocator: Allocator.Temp
            );

            if (triangulationStatus.Value != Status.OK)
            {
                Debug.LogWarning($"Triangulation status returned {triangulationStatus.Value}");
                
                // Re add removed nodes
                foreach (var triangle in removedNodes)
                {
                    NavMesh.AddNode(new NavMesh.AddNodeRequest
                    {
                        Triangle = triangle,
                    });
                }
                return;
            }
            
            // Create nodes
            for (int i = 0; i < outputTriangles.Length; i += 3)
            {
                var triangle = new Triangle(
                    positions[outputTriangles[i]],
                    positions[outputTriangles[i + 1]],
                    positions[outputTriangles[i + 2]]);
                
                // triangle.DrawBorder(Color.green, 5);
                // triangle.GetCenter.To3D().DrawPoint(Color.green, 5, .1f);
                
                NavMesh.AddNode(new NavMesh.AddNodeRequest
                {
                    Triangle = triangle,
                });
            }
            
            // Debug
            foreach (var e in borderEdges)
            {
                Debug.DrawLine(e.A.To3D(), e.B.To3D(), Color.green);
            }

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
        
        private static bool BorderIntersection(in float2 a, in float2 b, in NativeList<EdgeKey> edges, out float2 intersectionPoint)
        {
            foreach (EdgeKey edge in edges)
            {
                if (GeometryUtils.TryIntersect(a, b, edge.A, edge.B, out intersectionPoint))
                {
                    return true;
                }
            }
            intersectionPoint = default;
            return false;
        }
    }
}