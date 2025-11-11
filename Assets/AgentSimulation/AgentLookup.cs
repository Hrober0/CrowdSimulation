using System;
using HCore.Extensions;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace AgentSimulation
{
    public struct AgentLookup : IDisposable
    {
        public NativeList<Agent> Agents;
        public NativeParallelMultiHashMap<int, int> AgentsLookup;   // position hash index to agent index

        private readonly float _chunkSize;
        private readonly int _lookupEntriesMultiplier;
        
        public bool IsCreated => Agents.IsCreated;
        
        public AgentLookup(float chunkSize, int capacity, int lookupEntriesMultiplier)
        {
            _lookupEntriesMultiplier = lookupEntriesMultiplier;
            _chunkSize = chunkSize;
            
            Agents = new(capacity,Allocator.Persistent);
            AgentsLookup = new(capacity * _lookupEntriesMultiplier, Allocator.Persistent);
        }
        
        /// <summary>
        /// Adds a new agent to simulation.
        /// Will be added to tree in next simulation step.
        /// </summary>
        public void AddAgent(float2 position, float range, float maxSpeed, int objectId)
        {
            Agents.Add(new Agent()
            {
                ObjectId = objectId,
                
                Position = position,
                Velocity = float2.zero,
                PrefVelocity = float2.zero,
                MaxSpeed = maxSpeed,
                Radius = range,

                MaxNeighbors = 10,
                NeighborDist = range + 2f,
                TimeHorizonAgent = range + 2f,
                TimeHorizonObstacle = range + 1f,
            });
        }

        /// <summary>
        /// Remove agent from the simulation.
        /// </summary>
        /// <param name="objectId">>Obstacle id.</param>
        /// <param name="updateLookup">To remove agent from simulation list must be updated, it can be done automatically or manually later</param>
        public void RemoveAgent(int objectId, bool updateLookup = true)
        {
            for (int i = 0; i < Agents.Length; i++)
            {
                var agent = Agents[i];
                if (agent.ObjectId == objectId)
                {
                    Agents.RemoveAt(i);
                    if (updateLookup)
                    {
                        UpdateAgentLookup();
                    }
                    return;
                }
            }
            Debug.LogWarning($"Agent not found {objectId}");
        }

        public void UpdateAgentLookup()
        {
            AgentsLookup.Clear();
            var requiredCapacity = math.max(AgentsLookup.Capacity, Agents.Length * _lookupEntriesMultiplier);
            if (requiredCapacity > AgentsLookup.Capacity)
            {
                Debug.LogWarning($"Map capacity ({AgentsLookup.Capacity}) exceeded, map was relocated, it will cause memory leak!");
                AgentsLookup.Capacity = requiredCapacity + 2048;
            }
            new BuildAgentLookupJob
            {
                Agents = Agents,
                Lookup = AgentsLookup.AsParallelWriter(),
                ChunkSizeMultiplier = 1f / _chunkSize,
            }
            .Schedule(Agents.Length, 4).Complete();
        }

        public void Clear()
        {
            Agents.Clear();
            AgentsLookup.Clear();
        }
        
        public void Dispose()
        {
            Agents.Dispose();
            AgentsLookup.Dispose();
        }
        
        
#if UNITY_EDITOR
        
        public void DrawPositions()
        {
            if (!Agents.IsCreated)
            {
                return;
            }

            Gizmos.color = Color.white;
            foreach (var agent in Agents)
            {
                Gizmos.DrawSphere(agent.Position.To3D(), agent.Radius);
            }
        }

        public void DrawVelocities()
        {
            if (!Agents.IsCreated)
            {
                return;
            }
            
            Gizmos.color = Color.yellow;
            foreach (var agent in Agents)
            {
                Gizmos.DrawLine(agent.Position.To3D(), (agent.Position + agent.Velocity).To3D());
            }
        }

        public void DrawPreferredVelocities()
        {
            if (!Agents.IsCreated)
            {
                return;
            }
            
            Gizmos.color = Color.green;
            foreach (var agent in Agents)
            {
                Gizmos.DrawLine(agent.Position.To3D(), (agent.Position + agent.PrefVelocity).To3D());
            }
        }
        
#endif
    }
}