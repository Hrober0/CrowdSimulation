using System;
using System.Collections.Generic;
using AgentSimulation;
using HCore.Shapes;
using HCore.Systems;
using Objects.GenericSystems;
using Unity.Mathematics;
using UnityEngine;

namespace Objects.Obstacles
{
    public class ObstacleSystem : MonoBehaviour, ISystem
    {
        [SerializeField] private bool _drawObstacles;
        
        private ObjectsSystem _objectsSystem;
        
        // queries data are used to reduce checking the same objects while query objects
        private int _queryId = 0;
        private int[] _queries;
        private int[] _queriesConvex;
        
        public ObstacleLookup ObstacleLookup { get; private set; }
        
        void IInitializable.Initialize(ISystemManager systems)
        {
            _objectsSystem = systems.Get<ObjectsSystem>();

            _objectsSystem.OnObjectRegisteredInit += RegisterObstacle;
            _objectsSystem.OnObjectUnregisteredInit += UnregisterObstacle;
            _objectsSystem.OnObjectsCapacityChanged += UpdateQueriesCapacity;
            
            _queries = new int[_objectsSystem.DefaultCapacity];
            _queriesConvex = new int[_objectsSystem.DefaultCapacity];
            
            ObstacleLookup = new ObstacleLookup(_objectsSystem.ChunkSize, 100, 3);
        }
        void IInitializable.Deinitialize()
        {
            _objectsSystem.OnObjectRegisteredInit -= RegisterObstacle;
            _objectsSystem.OnObjectUnregisteredInit -= UnregisterObstacle;
            _objectsSystem.OnObjectsCapacityChanged -= UpdateQueriesCapacity;
            
            ObstacleLookup.Dispose();
        }
        
        public void FindObstacleInRangeRect(SimpleRect range, List<IObject> results)
        {
            var center = new float2
            (
                (range.MaxX + range.MinX) * 0.5f,
                (range.MaxY + range.MinY) * 0.5f
            );

            _queryId++;
            var chunkSizeMultiplier = 1f / _objectsSystem.ChunkSize;
            var minX = (int)(range.MinX * chunkSizeMultiplier);
            var maxX = (int)(range.MaxX * chunkSizeMultiplier);
            var minY = (int)(range.MinY * chunkSizeMultiplier);
            var maxY = (int)(range.MaxY * chunkSizeMultiplier);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var chunkIndex = RVOMath.ChunkHash(x, y);
                    foreach (var vertexIndex in ObstacleLookup.ObstacleVerticesLookup.GetValuesForKey(chunkIndex))
                    {
                        ObstacleVertex vertex = ObstacleLookup.ObstacleVertices[vertexIndex];
                        float2 startPosition = vertex.Point;
                        ObstacleVertex nextVertex = ObstacleLookup.ObstacleVertices[vertex.Next];
                        float2 endPosition = nextVertex.Point;
                        float2 min = math.min(startPosition, endPosition);
                        float2 max = math.max(startPosition, endPosition);
                        if (_queries[vertex.ObjectId] == _queryId)
                        {
                            continue;
                        }
                        
                        if (min.x <= range.MaxX
                            && max.x >= range.MinX
                            && min.y <= range.MaxY
                            && max.y >= range.MinY)
                        {
                            results.Add(_objectsSystem.Objects[vertex.ObjectId]);
                            _queries[vertex.ObjectId] = _queryId;
                            continue;
                        }

                        if (!vertex.Convex || _queriesConvex[vertex.ObjectId] == _queryId)
                        {
                            continue;
                        }

                        _queriesConvex[vertex.ObjectId] = _queryId;
                        bool isIn = true;
                        var p = startPosition;
                        while (nextVertex != vertex)
                        {
                            if (RVOMath.LeftOf(p, nextVertex.Point, center) < 0)
                            {
                                isIn = false;
                                break;
                            }
                            p = nextVertex.Point;
                            nextVertex = ObstacleLookup.ObstacleVertices[nextVertex.Next];
                        }
                        if (isIn)
                        {
                            results.Add(_objectsSystem.Objects[vertex.ObjectId]);
                            _queries[vertex.ObjectId] = _queryId;
                        }
                    }
                }
            }
        }

        private void RegisterObstacle(IObject obj, int objectId)
        {
            if (!obj.TryGetModule(out IObstacle _))
            {
                return;
            }

            var vertices = new List<float2>();
            foreach (var point in obj.Bounds.GetBorderPoints())
            {
                vertices.Add(point);
            }
            ObstacleLookup.AddObstacle(vertices, objectId);
        }
        private void UnregisterObstacle(IObject obj, int objectId)
        {
            if (!obj.TryGetModule(out IObstacle _))
            {
                return;
            }
            
            ObstacleLookup.RemoveObstacle(objectId);
        }

        private void UpdateQueriesCapacity(int newSize)
        {
            Array.Resize(ref _queries, newSize);
            Array.Resize(ref _queriesConvex, newSize);
        }

        private void OnDrawGizmos()
        {
            if (ObstacleLookup.IsCreated)
            {
                if (_drawObstacles)
                {
                    ObstacleLookup.DrawObstacle();
                }
            }
        }
    }
}