using System;
using System.Collections.Generic;
using andywiecko.BurstTriangulator;
using andywiecko.BurstTriangulator.LowLevel.Unsafe;
using CustomNativeCollections;
using HCore;
using HCore.Extensions;
using HCore.Shapes;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public struct NavObstacles<T> : IDisposable where T : unmanaged, INodeAttributes<T>
    {
        public NativeFixedList<Obstacle> Obstacles;
        public NativeSpatialHash<IndexedTriangle> ObstacleLookup;
        public NativeParallelMultiHashMap<int, Edge> ObstacleEdges;

        public NavObstacles(float chunkSize, int capacity = 512, int averageVerticesPerObstacle = 4)
        {
            Obstacles = new(capacity, Allocator.Persistent);
            ObstacleLookup = new(capacity, chunkSize, Allocator.Persistent);
            ObstacleEdges = new(capacity * averageVerticesPerObstacle, Allocator.Persistent);
        }

        public void Dispose()
        {
            Obstacles.Dispose();
            ObstacleLookup.Dispose();
            ObstacleEdges.Dispose();
        }
        
        public int AddObstacle(in NativeList<float2> border, T attributes)
        {
            if (border.Length < 2)
            {
                Debug.LogWarning("Attempted to obstacle with border containing less then one edge!");
                return -1;
            }
            
            // Add obstacle
            var worldMin = new float2(float.MaxValue, float.MaxValue);
            var worldMax = new float2(float.MinValue, float.MinValue);
            foreach (var p in border)
            {
                worldMin = math.min(p, worldMin);
                worldMax = math.max(p, worldMax);
            }

            var obstacle = new Obstacle(worldMin, worldMax, attributes);
            int newId = Obstacles.Add(obstacle);

            // Add edges
            var constraintEdges = new NativeArray<int>(border.Length * 2, Allocator.Temp);
            ObstacleEdges.Add(newId, new Edge(border[^1], border[0]));
            constraintEdges[0] = border.Length - 1;
            constraintEdges[1] = 0;
            for (int i = 1; i < border.Length; i++)
            {
                ObstacleEdges.Add(newId, new Edge(border[i - 1], border[i]));
                constraintEdges[i * 2] = i - 1;
                constraintEdges[i * 2 + 1] = i;
            }

            // Add triangle spatial hash
            using var outputTriangles = new NativeList<int>(border.Length * 3, Allocator.Temp);
            using var status = new NativeReference<andywiecko.BurstTriangulator.Status>(Allocator.Temp);
            new UnsafeTriangulator<float2>().Triangulate(
                input: new()
                {
                    Positions = border.AsArray(),
                    ConstraintEdges = constraintEdges,
                },
                output: new()
                {
                    Triangles = outputTriangles,
                    Status = status,
                },
                args: Args.Default(
                    restoreBoundary: true
                ), 
                allocator: Allocator.Temp
            );

            if (status.Value != Status.OK)
            {
                constraintEdges.Dispose();
                return -1;
            }

            for (var index = 0; index < outputTriangles.Length; index += 3)
            {
                var triangle = new Triangle(
                    border[outputTriangles[index]],
                    border[outputTriangles[index + 1]],
                    border[outputTriangles[index + 2]]
                    );
                ObstacleLookup.AddAABB(triangle.Min, triangle.Max, new IndexedTriangle(triangle, newId));
            }
            
            constraintEdges.Dispose();

            return newId;
        }

        public int RunAddObstacle(in NativeList<float2> border, T attributes)
        {
            using var obstacleId = new NativeReference<int>(Allocator.TempJob);
            new AddObstacleJob
            {
                Border = border,
                Attributes = attributes,
                NavObstacles = this,
                Id = obstacleId,
            }.Run();
            return obstacleId.Value;
        }
        
        public void RemoveObstacle(int id)
        {
            // Remove obstacle
            Obstacle obstacle = Obstacles[id];
            Obstacles.RemoveAt(id);
            
            // Remove triangle spatial hash
            var dummyTriangleToRemoveById = new IndexedTriangle(default, id);
            ObstacleLookup.RemoveAABB(obstacle.Min, obstacle.Max, dummyTriangleToRemoveById);

            // Remove edges
            ObstacleEdges.Remove(id);
        }

        public void RunRemoveObstacle(int id)
        {
            new RemoveObstacleJob
            {
                ObstacleId = id,
            }.Run();
        }
        
        public void UpdateAttributes(int id, T attributes)
        {
            Obstacle obstacle = Obstacles[id];
            obstacle.Attributes = attributes;
            Obstacles[id] = obstacle;
        }

        public void Clear()
        {
            Obstacles.Clear();
            ObstacleLookup.Clear();
            ObstacleEdges.Clear();
        }
        
        #region Debug

        public void DrawEdges()
        {
            using NativeArray<int> keys = ObstacleEdges.GetKeyArray(Allocator.Temp);
            foreach (var key in keys)
            {
                float2 center = float2.zero;
                int number = 0;
                var color = ColorUtils.GetColor(key);
                foreach (var edge in ObstacleEdges.GetValuesForKey(key))
                {
                    edge.DrawBorder(color);

                    center += edge.A;
                    center += edge.B;
                    number++;
                }
                
                (center / (number * 2)).To3D().DrawPoint(color, null, 0.1f);
            }
        }

        public void DrawLookup()
        {
            ObstacleLookup.Map.Draw(Color.green);
        }

        public string GetCapacityStats()
        {
            return $"obstacle: {Obstacles.Length}\nedges: {ObstacleEdges.Count()} \nobstacleLookup: {ObstacleLookup.Count}";
        }
        
        #endregion

        public struct Obstacle
        {
            public readonly float2 Min;
            public readonly float2 Max;
            public T Attributes;

            public Obstacle(float2 min, float2 max, T attributes)
            {
                Min = min;
                Max = max;
                Attributes = attributes;
            }
        }

        public readonly struct IndexedTriangle : IEquatable<IndexedTriangle>, IOutline
        {
            public readonly Triangle Triangle;
            public readonly int Index;

            public IndexedTriangle(Triangle triangle, int index)
            {
                Triangle = triangle;
                Index = index;
            }

            public bool Equals(IndexedTriangle other) => Index == other.Index;

            public override bool Equals(object obj) => obj is IndexedTriangle other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Triangle, Index);
            public IEnumerable<Vector2> GetBorderPoints() => Triangle.GetBorderPoints();
        }
        
        [BurstCompile]
        private struct AddObstacleJob : IJob
        {
            [ReadOnly] public NativeList<float2> Border;
            [ReadOnly] public T Attributes;
            public NativeReference<int> Id;
            public NavObstacles<T> NavObstacles;
            
            public void Execute()
            {
                Id.Value = NavObstacles.AddObstacle(Border, Attributes);
            }
        }
    
        [BurstCompile]
        private struct RemoveObstacleJob : IJob
        {
            [ReadOnly] public int ObstacleId;
            public NavObstacles<T> NavObstacles;
            
            public void Execute()
            {
                NavObstacles.RemoveObstacle(ObstacleId);
            }
        }
    }

    public static class NavObstaclesExtension
    {
        public static int AddObstacle<T>(this NavObstacles<T> navObstacles, T attributes, List<float2> border) where T : unmanaged, INodeAttributes<T>
        {
            using var nativeList = new NativeList<float2>(border.Count, Allocator.Temp);
            for (int i = 0; i < border.Count; i++)
            {
                nativeList.Add(border[i]);
            }
            return navObstacles.RunAddObstacle(in nativeList, attributes);
        }
        public static int AddObstacle<T>(this NavObstacles<T> navObstacles, T attributes, params float2[] border) where T : unmanaged, INodeAttributes<T>
        {
            using var nativeList = new NativeList<float2>(border.Length, Allocator.Temp);
            for (int i = 0; i < border.Length; i++)
            {
                nativeList.Add(border[i]);
            }
            return navObstacles.RunAddObstacle(in nativeList, attributes);
        }
    }
}