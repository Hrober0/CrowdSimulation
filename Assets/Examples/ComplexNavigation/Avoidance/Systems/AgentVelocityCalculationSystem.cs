using System.Runtime.CompilerServices;
using AgentSimulation;
using CustomNativeCollections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ComplexNavigation
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AgentVelocityCalculationSystem : SystemBase
    {
        [BurstCompile]
        protected override void OnUpdate()
        {
            var agentHashSystem = World.GetExistingSystemManaged<AgentSpatialHashSystem>();
            var obstacleHashSystem = World.GetExistingSystemManaged<AvoidanceObstacleLookupSystem>();

            Dependency = new VelocityUpdateJob
                {
                    AgentSpatialHash = agentHashSystem.SpatialHash,
                    ObstacleSpatialHash = obstacleHashSystem.SpatialLookup,
                    TimeStamp = SystemAPI.Time.DeltaTime,
                }
                .ScheduleParallel(Dependency);
            Dependency.Complete();
        }
    }

    [BurstCompile]
    public partial struct VelocityUpdateJob : IJobEntity
    {
        private const int MAX_AGENT_NEIGHBORS = 8;
        private const float NEIGHBOR_QUERY_DIST = 1f;
        private const float TIME_HORIZON_OBSTACLE = 1f;
        private const float TIME_HORIZON_AGENT = 1f;
        private const float MAX_SPEED = 5f;

        [ReadOnly] public NativeSpatialHash<AgentCoreData> AgentSpatialHash;
        [ReadOnly] public NativeSpatialLookup<ObstacleVertex> ObstacleSpatialHash;
        [ReadOnly] public float TimeStamp;

        public void Execute(ref AgentCoreData agentCoreData)
        {
            var timeStampInv = 1f / TimeStamp;
            var agent = ToAgent(agentCoreData);

            // Compute agent neighbours
            var agentNeighbors = new NativeList<AgentNeighbor>(MAX_AGENT_NEIGHBORS, Allocator.Temp);
            var agentLookupRange = agent.Radius + agent.NeighborDist;
            var agentInsertionProcessor = new AgentNeighborInsertionProcessor
            {
                CurrentAgent = agentCoreData,
                QueryDistance = agentLookupRange,
                MaxNeighbors = MAX_AGENT_NEIGHBORS,
                AgentNeighbors = agentNeighbors
            };
            AgentSpatialHash.ForEachInAABB(
                agentCoreData.Position - new float2(agentLookupRange, agentLookupRange),
                agentCoreData.Position + new float2(agentLookupRange, agentLookupRange),
                ref agentInsertionProcessor
            );

            // Compute obstacle neighbours
            var obstacleNeighbors = new NativeList<ObstacleVertexNeighbor>(32, Allocator.Temp);
            var obstacleLookupRange = agent.Radius + agent.TimeHorizonObstacle * agent.MaxSpeed;
            var obstacleInsertProcessor = new ObstacleNeighborInsertionProcessor
            {
                AgentPosition = agentCoreData.Position,
                RangeSq = obstacleLookupRange * obstacleLookupRange,
                ObstacleVertices = ObstacleSpatialHash.Values,
                ObstacleNeighbors = obstacleNeighbors,
            };
            ObstacleSpatialHash.ForEachInAABB(
                agentCoreData.Position - new float2(obstacleLookupRange, obstacleLookupRange),
                agentCoreData.Position + new float2(obstacleLookupRange, obstacleLookupRange),
                ref obstacleInsertProcessor
            );

            // Compute orca lines
            var orcaLines = new NativeList<Line>(Allocator.Temp);
            Linear.AddObstacleLine(agent, orcaLines, obstacleNeighbors, ObstacleSpatialHash.Values);
            int numObstLines = orcaLines.Length;
            Linear.AddAgentLine(agent, orcaLines, agentNeighbors, timeStampInv);

            // Compute velocity
            var newVelocity = agent.Velocity;
            int lineFail = Linear.LinearProgram2(orcaLines, agent.MaxSpeed, agent.PrefVelocity, false, ref newVelocity);
            if (lineFail < orcaLines.Length)
            {
                Linear.LinearProgram3(orcaLines, numObstLines, lineFail, agent.MaxSpeed, ref newVelocity);
            }


            var velocityLengthSqr = math.lengthsq(newVelocity);
            if (velocityLengthSqr > agent.MaxSpeed)
            {
                newVelocity = newVelocity / velocityLengthSqr * agent.MaxSpeed;
            }

            // Apply
            agentCoreData.Velocity = newVelocity;

            agentNeighbors.Dispose();
            orcaLines.Dispose();
        }

        private struct AgentNeighborInsertionProcessor : ISpatialQueryProcessor<AgentCoreData>
        {
            public AgentCoreData CurrentAgent;
            public float QueryDistance;
            public int MaxNeighbors;
            public NativeList<AgentNeighbor> AgentNeighbors;

            public void Process(AgentCoreData neighborData)
            {
                // Don't insert the agent as its own neighbor
                if (CurrentAgent.Equals(neighborData))
                {
                    return;
                }

                float distSq = math.lengthsq(CurrentAgent.Position - neighborData.Position);
                if (distSq >= QueryDistance * QueryDistance)
                {
                    return;
                }

                // Insert the neighbor into the list
                if (AgentNeighbors.Length < MaxNeighbors)
                {
                    AgentNeighbors.Add(default);
                }

                // Sort to maintain order by distance
                int i = AgentNeighbors.Length - 1;
                while (i != 0 && distSq < AgentNeighbors[i - 1].Distance)
                {
                    AgentNeighbors[i] = AgentNeighbors[i - 1];
                    --i;
                }

                AgentNeighbors[i] = new AgentNeighbor { Distance = distSq, Agent = ToAgent(neighborData) };
            }
        }

        private struct ObstacleNeighborInsertionProcessor : ISpatialQueryProcessor<ObstacleVertex>
        {
            public float RangeSq;
            public float2 AgentPosition;
            public NativeList<ObstacleVertex> ObstacleVertices;
            public NativeList<ObstacleVertexNeighbor> ObstacleNeighbors;

            public void Process(ObstacleVertex vertex)
            {
                float2 startPosition = vertex.Point;
                float2 endPosition = ObstacleVertices[vertex.Next].Point;

                float agentLeftOfLine = RVOMath.LeftOf(startPosition, endPosition, AgentPosition);
                if (agentLeftOfLine >= 0)
                {
                    return;
                }

                float distSqLine = math.square(agentLeftOfLine) / math.lengthsq(endPosition - startPosition);
                if (distSqLine >= RangeSq)
                {
                    // Try obstacle at this node only if agent is on right side of
                    // obstacle (and can see obstacle).
                    return;
                }

                float distSq = RVOMath.DistSqPointLineSegment(startPosition, endPosition, AgentPosition);

                if (distSq >= RangeSq)
                {
                    return;
                }

                ObstacleNeighbors.Add(default);

                // Sort to maintain order by distance
                int i = ObstacleNeighbors.Length - 1;
                while (i != 0 && distSq < ObstacleNeighbors[i - 1].Distance)
                {
                    ObstacleNeighbors[i] = ObstacleNeighbors[i - 1];
                    --i;
                }

                ObstacleNeighbors[i] = new ObstacleVertexNeighbor { Distance = distSq, Obstacle = vertex };
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Agent ToAgent(in AgentCoreData coreData)
        {
            return new Agent
            {
                ObjectId = coreData.Entity.Index,
                Position = coreData.Position,
                Velocity = coreData.Velocity,
                PrefVelocity = coreData.PrefVelocity,
                MaxSpeed = MAX_SPEED,
                Radius = coreData.Radius,
                MaxNeighbors = MAX_AGENT_NEIGHBORS,
                NeighborDist = NEIGHBOR_QUERY_DIST + coreData.Radius,
                TimeHorizonAgent = TIME_HORIZON_AGENT + coreData.Radius,
                TimeHorizonObstacle = TIME_HORIZON_OBSTACLE + coreData.Radius,
            };
        }
    }
}