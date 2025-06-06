using System;
using System.Collections;
using System.Collections.Generic;
using AgentSimulation;
using HCore.Shapes;
using HCore.Systems;
using Objects.GenericSystems;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Objects
{
    public class AgentsSystem : MonoBehaviour, ISystem
    {
        [SerializeField] private float _timeStamp = 0.05f;
        
        [Space]
        [SerializeField] private bool _drawPositions;
        [SerializeField] private bool _drawVelocities;
        [SerializeField] private bool _drawPreferredVelocities;

        private ObjectsSystem _objectsSystem;
        private ObstacleSystem _obstacleSystem;
        
        private YieldInstruction _simulationInterval;
        
        // queries data are used to reduce checking the same objects while query objects
        private int _queryId = 0;
        private int[] _queries;

        public AgentLookup AgentLookup { get; private set; }
        
        void IInitializable.Initialize(ISystemManager systems)
        {
            _objectsSystem = systems.Get<ObjectsSystem>();
            _obstacleSystem = systems.Get<ObstacleSystem>();

            _objectsSystem.OnObjectRegisteredInit += RegisterAgent;
            _objectsSystem.OnObjectUnregisteredInit += UnregisterAgent;
            _objectsSystem.OnObjectsCapacityChanged += UpdateQueriesCapacity;
            
            _queries = new int[_objectsSystem.DefaultCapacity];
            
            AgentLookup = new AgentLookup(_objectsSystem.ChunkSize);
            
            _simulationInterval = new WaitForSeconds(_timeStamp);
            StartCoroutine(Simulate());
        }
        void IInitializable.Deinitialize()
        {
            _objectsSystem.OnObjectRegisteredInit -= RegisterAgent;
            _objectsSystem.OnObjectUnregisteredInit -= UnregisterAgent;
            _objectsSystem.OnObjectsCapacityChanged -= UpdateQueriesCapacity;
            
            StopAllCoroutines();
        }

        public void FindAgentsInRangeRect(SimpleRect range, List<IObject> results)
        {
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
                    //new Rectangle(x / chunkSizeMultiplier, y / chunkSizeMultiplier, 1f / chunkSizeMultiplier, 1f / chunkSizeMultiplier).DrawBorder(Color.red, 1);
                    foreach (var agentIndex in AgentLookup.AgentsLookup.GetValuesForKey(chunkIndex))
                    {
                        Agent agent = AgentLookup.Agents[agentIndex];
                        float2 pos = agent.Position;
                        var radius = agent.Radius;
                        if (_queries[agent.ObjectId] != _queryId
                            && pos.x + radius >= range.MinX && pos.x - radius <= range.MaxX
                            && pos.y + radius >= range.MinY && pos.y - radius <= range.MaxY)
                        {
                            results.Add(_objectsSystem.Objects[agent.ObjectId]);
                            _queries[agent.ObjectId] = _queryId;
                        }
                    }
                }
            }
        }
        
        private IEnumerator Simulate()
        {
            while (true)
            {
                yield return _simulationInterval;

                // SyncAgentDataIn();

                AgentLookup.UpdateAgentLookup();

                new UpdateAgentJobParallel
                {
                    Agents = AgentLookup.Agents,
                    AgentLookup = AgentLookup.AgentsLookup,

                    ObstacleVertices = _obstacleSystem.ObstacleLookup.ObstacleVertices,
                    ObstacleVerticesLookup = _obstacleSystem.ObstacleLookup.ObstacleVerticesLookup,

                    ChunkSizeMultiplier = 1f / _objectsSystem.ChunkSize,
                    TimeStamp = _timeStamp,
                }
                .Schedule(AgentLookup.Agents.Length, 4).Complete();

                // SyncAgentDataOut();

                //Debug.Log("Updated agents");
            }
        }
        
        private void RegisterAgent(IObject obj, int objectId)
        {
            if (!obj.TryGetModule(out IMovingObject _))
            {
                return;
            }
            
            AgentLookup.AddAgent(obj.Bounds.Center, obj.Bounds.Size().x * 0.5f, 10, objectId);
        }
        private void UnregisterAgent(IObject obj, int objectId)
        {
            if (!obj.TryGetModule(out IMovingObject _))
            {
                return;
            }

            AgentLookup.RemoveAgent(objectId);
        }
        
        private void UpdateQueriesCapacity(int newSize)
        {
            Array.Resize(ref _queries, newSize);
        }
        
        private void OnDrawGizmos()
        {
            if (_drawPositions)
            {
                AgentLookup.DrawPositions();
            }
            
            if (_drawVelocities)
            {
                AgentLookup.DrawVelocities();
            }
            
            if (_drawPreferredVelocities)
            {
                AgentLookup.DrawPreferredVelocities();
            }
        }
    }
}