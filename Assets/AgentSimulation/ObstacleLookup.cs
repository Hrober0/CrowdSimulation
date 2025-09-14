﻿using System;
using System.Collections.Generic;
using HCore.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace AgentSimulation
{
    public struct ObstacleLookup : IDisposable
    {
        public NativeList<ObstacleVertex> ObstacleVertices;
        public NativeParallelMultiHashMap<int, int> ObstacleVerticesLookup;     // position hash index to vertex index

        private readonly float _chunkSize;
        
        public bool IsCreated => ObstacleVertices.IsCreated;

        public ObstacleLookup(float chunkSize)
        {
            ObstacleVertices = new(Allocator.Persistent);
            ObstacleVerticesLookup = new(100, Allocator.Persistent);
            
            _chunkSize = chunkSize;
        }
        
        /// <summary>
        /// Adds a new obstacle to the simulation
        /// </summary>
        /// <param name="vertices">>List of the vertices of the polygonal obstacle in counterclockwise order.</param>
        /// <param name="objectId">Obstacle id.</param>
        /// <param name="updateTree">To add obstacle to simulation tree must be updated, it can be done automatically on manually</param>
        public void AddObstacle(List<float2> vertices, int objectId, bool updateTree = true)
        {
            if (vertices.Count < 2)
            {
                return;
            }

            int firstVertexIndex = ObstacleVertices.Length;

            for (int i = 0; i < vertices.Count; i++)
            {
                var obstacleVertex = new ObstacleVertex()
                {
                    ObjectId = objectId,
                    VertexIndex = ObstacleVertices.Length,
                };

                obstacleVertex.Next = i < vertices.Count - 1 ? obstacleVertex.VertexIndex + 1 : firstVertexIndex;
                obstacleVertex.Previous = i > 0 ? obstacleVertex.VertexIndex - 1 : firstVertexIndex + vertices.Count - 1;

                obstacleVertex.Point = vertices[i];
                obstacleVertex.Direction = math.normalize(vertices[(i == vertices.Count - 1 ? 0 : i + 1)] - vertices[i]);

                if (vertices.Count == 2)
                {
                    obstacleVertex.Convex = true;
                }
                else
                {
                    float t = RVOMath.LeftOf(
                        vertices[i == 0 ? vertices.Count - 1 : i - 1],
                        vertices[i],
                        vertices[i == vertices.Count - 1 ? 0 : i + 1]);
                    obstacleVertex.Convex = (t >= 0f);
                }

                ObstacleVertices.Add(obstacleVertex);
            }

            if (updateTree)
            {
                UpdateObstacleVeritiesLookup();
            }
        }

        /// <summary>
        /// Remove obstacle from the simulation
        /// </summary>
        /// <param name="objectId">Obstacle id.</param>
        /// <param name="vertexIndex">First of obstacle vertices, If known can speed up search.</param>
        /// <param name="updateTree">To remove obstacle from simulation tree must be updated, it can be done automatically or manually later</param>
        public void RemoveObstacle(int objectId, int vertexIndex = 0, bool updateTree = true)
        {
            int del = 0;
            for (int i = vertexIndex; i < ObstacleVertices.Length; i++)
            {
                var vert = ObstacleVertices[i];
                if (vert.ObjectId == objectId)
                {
                    ObstacleVertices.RemoveAt(i);
                    i--;
                    del++;
                }
                else
                {
                    vert.VertexIndex -= del;
                    vert.Next -= del;
                    vert.Previous -= del;
                    ObstacleVertices[i] = vert;
                }
            }

            if (updateTree)
            {
                UpdateObstacleVeritiesLookup();
            }
        }
        
        public void UpdateObstacleVeritiesLookup()
        {
            ObstacleVerticesLookup.Clear();
            var requiredCapacity = math.max(ObstacleVerticesLookup.Capacity, ObstacleVertices.Length * 3);
            if (requiredCapacity > ObstacleVerticesLookup.Capacity)
            {
                Debug.LogWarning($"Map capacity ({ObstacleVerticesLookup.Capacity}) exceeded, map was relocated, it will cause memory leak!");
                ObstacleVerticesLookup.Capacity = requiredCapacity + 2048;
            }
            new BuildObstacleVerticesLookupJob
                {
                    Vertices = ObstacleVertices,
                    Lookup = ObstacleVerticesLookup.AsParallelWriter(),
                    ChunkSizeMultiplier = 1f / _chunkSize,
                }
                .Schedule(ObstacleVertices.Length, 4).Complete();
        }

        public void Clear()
        {
            ObstacleVertices.Clear();
            ObstacleVerticesLookup.Clear();
        }
        
        public void Dispose()
        {
            ObstacleVertices.Dispose();
            ObstacleVerticesLookup.Dispose();
        }
        
        public void DrawObstacle()
        {
            if (!ObstacleVerticesLookup.IsCreated)
            {
                return;
            }
            
            Gizmos.color = Color.red;
            foreach (var vert in ObstacleVertices)
            {
                Gizmos.DrawLine(vert.Point.To3D(), ObstacleVertices[vert.Next].Point.To3D());
            }
        }
    }
}