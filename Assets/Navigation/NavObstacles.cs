using System;
using System.Collections.Generic;
using CustomNativeCollections;
using HCore;
using HCore.Extensions;
using HCore.Shapes;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public struct NavObstacles : IDisposable
    {
        public NativeFixedList<Obstacle> Obstacles;
        public NativeSpatialHash<IndexedTriangle> ObstacleLookup;
        public NativeParallelMultiHashMap<int, EdgeKey> ObstacleEdges;

        public NavObstacles(float cellSize, int obstacleInitialCapacity = 512, int averageVerticesPerObstacle = 4)
        {
            Obstacles = new(obstacleInitialCapacity, Allocator.Persistent);
            ObstacleLookup = new(obstacleInitialCapacity, cellSize, Allocator.Persistent);
            ObstacleEdges = new(obstacleInitialCapacity * averageVerticesPerObstacle, Allocator.Persistent);
        }

        public void Dispose()
        {
            Obstacles.Dispose();
            ObstacleLookup.Dispose();
            ObstacleEdges.Dispose();
        }
        
        public int AddObstacle(in NativeList<Triangle> parts)
        {
            // Add obstacle
            var worldMin = new float2(float.MaxValue, float.MaxValue);
            var worldMax = new float2(float.MinValue, float.MinValue);
            foreach (var part in parts)
            {
                worldMin = math.min(math.min(part.A, part.B), math.min(part.C, worldMin));
                worldMax = math.max(math.max(part.A, part.B), math.max(part.C, worldMax));
            }
            var obstacle = new Obstacle
            {
                Min = worldMin,
                Max = worldMax,
            };
            int newId = Obstacles.Add(obstacle);
            
            // Add triangle spatial hash
            foreach (var triangle in parts)
            {
                ObstacleLookup.AddAABB(triangle.Min, triangle.Max, new IndexedTriangle(triangle, newId));
            }
            
            // Add edges
            using var edges = new NativeList<EdgeKey>(parts.Length, Allocator.Temp);
            PolygonUtils.GetEdgesUnordered(in parts, edges);
            foreach (EdgeKey edge in edges)
            {
                ObstacleEdges.Add(newId, edge);
            }
            
            return newId;
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
            public float2 Min;
            public float2 Max;
        }

        public struct IndexedTriangle : IEquatable<IndexedTriangle>, IOutline
        {
            public Triangle Triangle;
            public int Index;

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
    }

    public static class NavObstaclesExtension
    {
        public static int AddObstacle(this NavObstacles navObstacles, List<Triangle> parts)
        {
            using var nativeList = new NativeList<Triangle>(parts.Count, Allocator.Temp);
            for (int i = 0; i < parts.Count; i++)
            {
                nativeList.Add(parts[i]);
            }
            return navObstacles.AddObstacle(in nativeList);
        }
    }
}