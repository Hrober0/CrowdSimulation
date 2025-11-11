using System;
using System.Collections.Generic;
using System.Linq;
using AgentSimulation;
using HCore.Extensions;
using Navigation;
using UnityEngine;
using Unity.Jobs;
using Unity.Mathematics;
using Random = UnityEngine.Random;

namespace AvoidanceTest
{
    public class AvoidanceVisualTest : MonoBehaviour
    {
        [SerializeField] private List<Transform> _agents;
        [SerializeField] private List<Transform> _obstacles;
        
        [Header("Settings")]
        [SerializeField] private float _timeStamp = 0.05f;
        [SerializeField] private float _chunkSize = 1;
        
        [Header("Debug")]
        [SerializeField] private bool _drawPositions;
        [SerializeField] private bool _drawVelocities;
        [SerializeField] private bool _drawPreferredVelocities;
        [SerializeField] private bool _drawObstacleBorder;

        private AgentLookup _agentLookup;
        private ObstacleLookup _obstacleLookup;
        
        private void Start()
        {
            _agentLookup = new AgentLookup(_chunkSize, 128, 2);
            _obstacleLookup = new ObstacleLookup(_chunkSize, 128, 3);

            AddObstacle();
            AddAgents();
            
            _ = Simulate();
        }

        private void OnDestroy()
        {
            _agentLookup.Dispose();
            _obstacleLookup.Dispose();
        }

        private void AddObstacle()
        {
            for (int index = 0; index < _obstacles.Count; index++)
            {
                Transform obstacleTransform = _obstacles[index];
                var vertices = DebugUtils.GetRectangleFromTransform(obstacleTransform).ToList();
                _obstacleLookup.AddObstacle(vertices, index, false);
            }

            _obstacleLookup.UpdateObstacleVeritiesLookup();
        }
        
        private void AddAgents()
        {
            for (var index = 0; index < _agents.Count; index++)
            {
                Transform agentTransform = _agents[index];
                _agentLookup.AddAgent(agentTransform.position.To2D(), agentTransform.localScale.x / 2f, 5, index);
                var agent = _agentLookup.Agents[index];
                agent.PrefVelocity = Random.insideUnitCircle.normalized;
                _agentLookup.Agents[index] = agent;
            }
        }
        
        private async Awaitable Simulate()
        {
            while (true)
            {
                await Awaitable.WaitForSecondsAsync(_timeStamp);

                _agentLookup.UpdateAgentLookup();

                new UpdateAgentJobParallel
                    {
                        Agents = _agentLookup.Agents,
                        AgentLookup = _agentLookup.AgentsLookup,

                        ObstacleVertices = _obstacleLookup.ObstacleVertices,
                        ObstacleVerticesLookup = _obstacleLookup.ObstacleVerticesLookup,

                        ChunkSizeMultiplier = 1f / _chunkSize,
                        TimeStamp = _timeStamp,
                    }
                    .Schedule(_agentLookup.Agents.Length, 4).Complete();

                MoveAgentByVelocity(_timeStamp);
                SyncAgentTransform();

                //Debug.Log("Updated agents");
            }
        }

        private void MoveAgentByVelocity(float timeStamp)
        {
            for (var index = 0; index < _agentLookup.Agents.Length; index++)
            {
                var agent = _agentLookup.Agents[index];
                agent.Position += agent.Velocity * timeStamp;
                _agentLookup.Agents[index] = agent;
            }
        }
        
        private void SyncAgentTransform()
        {
            for (var index = 0; index < _agents.Count; index++)
            {
                _agents[index].transform.position = _agentLookup.Agents[index].Position.To3D();
            }
        }

        private void OnDrawGizmos()
        {
            if (_agentLookup.IsCreated)
            {
                if (_drawPositions)
                {
                    _agentLookup.DrawPositions();
                }
                
                if (_drawVelocities)
                {
                    _agentLookup.DrawVelocities();
                }
                
                if (_drawPreferredVelocities)
                {
                    _agentLookup.DrawPreferredVelocities();
                }
            }
            
            
            if (_drawObstacleBorder)
            {
                if (_obstacleLookup.IsCreated)
                {
                    _obstacleLookup.DrawObstacle();
                }
                else
                {
                    foreach (var obst in _obstacles)
                    {
                        if (obst != null && obst.gameObject.activeInHierarchy)
                        {
                            DebugUtils.GetRectangleFromTransform(obst).DrawLoop(obst.gameObject.IsSelected() ? Color.green : Color.red);
                        }
                    }
                }
            }
        }
    }
}
