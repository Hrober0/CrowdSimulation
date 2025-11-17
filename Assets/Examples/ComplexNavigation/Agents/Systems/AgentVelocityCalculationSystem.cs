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
    [UpdateAfter(typeof(AgentSpatialHashUpdateSystem))]
    public partial class AgentVelocityCalculationSystem : SystemBase
    {
        [BurstCompile]
        protected override void OnUpdate()
        {
            var agentHashSystem = World.GetExistingSystemManaged<AgentSpatialHashSystem>();

            Dependency = new VelocityUpdateJob
                {
                    AgentSpatialHash = agentHashSystem.SpatialHash,
                    TimeStamp = .05f
                }
                .ScheduleParallel(Dependency);
        }
    }

    [BurstCompile]
    public partial struct VelocityUpdateJob : IJobEntity
    {
        private const int MAX_AGENT_NEIGHBORS = 16;
        private const float NEIGHBOR_QUERY_DIST = 2f;
        private const float TIME_HORIZON_OBSTACLE = 2f;
        private const float TIME_HORIZON_AGENT = 2f;

        [ReadOnly] public NativeSpatialHash<AgentCoreData> AgentSpatialHash;
        [ReadOnly] public float TimeStamp;

        public void Execute(ref AgentCoreData agentCoreData)
        {
            var timeStampInv = 1f / TimeStamp;
            var agent = ToAgent(agentCoreData);

            // Compute agent neighbours
            var agentNeighbors = new NativeList<AgentNeighbor>(MAX_AGENT_NEIGHBORS, Allocator.Temp);
            var agentInsertionProcessor = new NeighborInsertionProcessor
            {
                CurrentAgent = agentCoreData,
                QueryDistance = NEIGHBOR_QUERY_DIST,
                MaxNeighbors = MAX_AGENT_NEIGHBORS,
                AgentNeighbors = agentNeighbors
            };
            AgentSpatialHash.ForEachInAABB(
                agentCoreData.Position - new float2(agentCoreData.Radius, agentCoreData.Radius),
                agentCoreData.Position + new float2(agentCoreData.Radius, agentCoreData.Radius),
                ref agentInsertionProcessor
            );

            // Compute obstacle neighbours
            // var obstacleNeighbors = new NativeList<ObstacleVertexNeighbor>(Allocator.Temp);

            // Compute orca lines
            var orcaLines = new NativeList<Line>(Allocator.Temp);
            // Linear.AddObstacleLine(agent, orcaLines, obstacleNeighbors, ObstacleVertices);
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

        private struct NeighborInsertionProcessor : NativeSpatialHash<AgentCoreData>.ISpatialProcessor
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Agent ToAgent(in AgentCoreData coreData)
        {
            return new Agent
            {
                ObjectId = coreData.Entity.Index,
                Position = coreData.Position,
                Velocity = coreData.Velocity,
                PrefVelocity = coreData.PrefVelocity,
                MaxSpeed = math.length(coreData.PrefVelocity),
                Radius = coreData.Radius,
                MaxNeighbors = MAX_AGENT_NEIGHBORS,
                NeighborDist = NEIGHBOR_QUERY_DIST + coreData.Radius,
                TimeHorizonAgent = TIME_HORIZON_AGENT + coreData.Radius,
                TimeHorizonObstacle = TIME_HORIZON_OBSTACLE + coreData.Radius,
            };
        }
    }
}