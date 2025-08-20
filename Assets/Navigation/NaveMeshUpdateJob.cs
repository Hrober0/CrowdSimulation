using System.Runtime.CompilerServices;
using andywiecko.BurstTriangulator.LowLevel.Unsafe;
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
        private const float MIN_POINT_DISTANCE = .0001f;
        
        public float2 UpdateMin;
        public float2 UpdateMax;

        public NavMesh NavMesh;

        [ReadOnly]
        public NavObstacles NavObstacles;

        public void Execute()
        {
            // Remove nodes
            using var removedNodes = new NativeList<Triangle>(64, Allocator.Temp);
            NavMesh.RemoveNodes(UpdateMin, UpdateMax, removedNodes);

            // Get remove area border
            using var borderEdges = new NativeList<EdgeKey>(64, Allocator.Temp);
            PolygonUtils.GetEdgesUnordered(in removedNodes, borderEdges);
            
            // TODO: try merge edges

            // Get obstacle edges
            using var obstaclesParts = new NativeList<NavObstacles.IndexedTriangle>(64, Allocator.Temp);
            NavObstacles.ObstacleLookup.QueryAABB(UpdateMin, UpdateMax, obstaclesParts);

            using var obstacleIndexes = new NativeHashSet<int>(obstaclesParts.Length, Allocator.Temp);
            foreach (var indexedTriangle in obstaclesParts)
            {
                obstacleIndexes.Add(indexedTriangle.Index);
            }

            using var edges = new NativeList<Edge>(256, Allocator.Temp);
            foreach (var obstacleIndex in obstacleIndexes)
            {
                foreach (EdgeKey edge in NavObstacles.ObstacleEdges.GetValuesForKey(obstacleIndex))
                {
                    bool isAInside = IsPointInPolygon(in edge.A, in removedNodes);
                    bool isBInside = IsPointInPolygon(in edge.B, in removedNodes);

                    if (isAInside && isBInside)
                    {
                        edges.Add(new Edge(edge.A, edge.B));
                    }
                    else if (isAInside || isBInside)
                    {
                        // Get only part of edge that is inside
                        if (BorderIntersection(in edge.A, in edge.B, in borderEdges, out float2 intersectionPoint))
                        {
                            float2 insidePoint = isAInside ? edge.A : edge.B;
                            edges.Add(new Edge(intersectionPoint, insidePoint));
                        }
                        else
                        {
                            Debug.LogWarning($"One of edge point is inside border but intersection point between edge {edge} and border was not found");
                            edges.Add(new Edge(edge.A, edge.B));
                        }
                    }
                }
            }

            // Add border edges to obstacle edges
            foreach (var edge in borderEdges)
            {
                edges.Add(new Edge(edge.A, edge.B));
            }
            
            // Cut edges to avoid intersection
            PolygonUtils.CutIntersectingEdges(edges);
            
            // Prepare triangulation input
            using var positions = new NativeList<float2>(edges.Length * 2, Allocator.Temp);
            using var constraintEdges = new NativeList<int>(edges.Length * 4, Allocator.Temp);
            using var constrainCheck = new NativeHashSet<EdgeKey>(edges.Length, Allocator.Temp);
            foreach (var edge in edges)
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
                    Debug.LogWarning($"Detected duplicated edge {edge}");
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
            using var outputTriangles = new NativeList<int>(64, Allocator.Temp);
            new UnsafeTriangulator<float2>().Triangulate(
                input: new()
                {
                    Positions = positions.AsArray(),
                    ConstraintEdges = constraintEdges.AsArray(),
                },
                output: new() { Triangles = outputTriangles },
                args: Args.Default(restoreBoundary: true, validateInput: false), // there is an error with input validation https://github.com/andywiecko/BurstTriangulator/issues/384
                allocator: Allocator.Temp
            );
            
            // Create nodes
            for (int i = 0; i < outputTriangles.Length; i += 3)
            {
                var triangle = new Triangle(
                    positions[outputTriangles[i]],
                    positions[outputTriangles[i + 1]],
                    positions[outputTriangles[i + 2]]);
                
                // triangle.DrawBorder(Color.green, 5);
                // triangle.GetCenter.To3D().DrawPoint(Color.green, 5, .1f);
                
                // triangle.DrawBorder(Color.green);
                // triangle.GetCenter.To3D().DrawPoint(Color.green, null, .1f);
                
                NavMesh.AddNode(new NavMesh.AddNodeRequest
                {
                    Triangle = triangle,
                });
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
        
        [BurstCompile]
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

        [BurstCompile]
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