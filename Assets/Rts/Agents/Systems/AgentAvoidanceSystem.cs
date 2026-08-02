using Avoidance;
using CustomNativeCollections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Runs RVO2 over the preferred velocities, turning "where I want to go" into "where I can go without
    /// walking through my neighbours" (design §4 tier 3, §13.3 #16). The avoidance module is grid-agnostic -
    /// positions, velocities and radii - so it is reused exactly as it stands.
    ///
    /// Static obstacles are not fed in yet: blocked cells keep *paths* out of buildings, and the
    /// blocked-cell clamp in <see cref="AgentIntegrateSystem"/> catches agents shoved into one. Registering
    /// building outlines as RVO obstacles comes with the building work of §5.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(PathFollowSystem))]
    public partial struct AgentAvoidanceSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AgentSpatialHash>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Walking agents only. A parked one still has to be *avoided* - it is in the spatial hash - but
            // solving for a velocity it will never use is the per-frame cost §6 exists to avoid.
            EntityQuery query = SystemAPI.QueryBuilder().WithAllRW<AgentMove>().WithAll<PathFollow>().Build();
            int agentCount = query.CalculateEntityCount();
            if (agentCount == 0)
            {
                return;
            }

            float deltaTime = SystemAPI.Time.DeltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob);

            state.Dependency = new AgentVelocityJob
            {
                Entities = entities,
                Agents = state.GetComponentLookup<AgentMove>(),
                SpatialHash = SystemAPI.GetSingleton<AgentSpatialHash>().Hash,
                InverseTimeStep = 1f / deltaTime,
            }.ScheduleParallel(agentCount, 64, state.Dependency);

            state.Dependency.Complete();
            entities.Dispose();
        }
    }

    [BurstCompile]
    public struct AgentVelocityJob : IJobFor
    {
        private const int MAX_NEIGHBOURS = 8;
        private const float NEIGHBOUR_DISTANCE = 1f;
        private const float TIME_HORIZON_AGENT = 1f;
        private const float TIME_HORIZON_OBSTACLE = 1f;

        [ReadOnly] public NativeArray<Entity> Entities;

        [NativeDisableParallelForRestriction] public ComponentLookup<AgentMove> Agents;

        [ReadOnly] public NativeSpatialHash<AgentMove> SpatialHash;
        [ReadOnly] public float InverseTimeStep;

        public void Execute(int index)
        {
            Entity entity = Entities[index];
            AgentMove move = Agents[entity];
            Agent agent = ToAgent(move);

            var neighbours = new NativeList<AgentNeighbor>(MAX_NEIGHBOURS, Allocator.Temp);
            float range = agent.Radius + agent.NeighborDist;

            var insertion = new NeighbourInsertion
            {
                Current = move,
                QueryDistance = range,
                MaxNeighbours = MAX_NEIGHBOURS,
                Neighbours = neighbours,
            };

            SpatialHash.ForEachInAABB(move.Position - range, move.Position + range, ref insertion);

            var orcaLines = new NativeList<Line>(Allocator.Temp);
            Linear.AddAgentLine(agent, orcaLines, neighbours, InverseTimeStep);

            float2 velocity = agent.Velocity;
            int lineFail = Linear.LinearProgram2(orcaLines, agent.MaxSpeed, agent.PrefVelocity, false, ref velocity);
            if (lineFail < orcaLines.Length)
            {
                Linear.LinearProgram3(orcaLines, 0, lineFail, agent.MaxSpeed, ref velocity);
            }

            float speedSq = math.lengthsq(velocity);
            if (speedSq > agent.MaxSpeed * agent.MaxSpeed)
            {
                velocity = velocity / math.sqrt(speedSq) * agent.MaxSpeed;
            }

            move.Velocity = velocity;
            Agents[entity] = move;

            neighbours.Dispose();
            orcaLines.Dispose();
        }

        private static Agent ToAgent(in AgentMove move) => new()
        {
            ObjectId = move.Entity.Index,
            Position = move.Position,
            Velocity = move.Velocity,
            PrefVelocity = move.PrefVelocity,
            MaxSpeed = move.MaxSpeed,
            Radius = move.Radius,
            MaxNeighbors = MAX_NEIGHBOURS,
            NeighborDist = NEIGHBOUR_DISTANCE + move.Radius,
            TimeHorizonAgent = TIME_HORIZON_AGENT + move.Radius,
            TimeHorizonObstacle = TIME_HORIZON_OBSTACLE + move.Radius,
        };

        /// <summary>Keeps the nearest few neighbours, closest first, which is the order RVO wants them in.</summary>
        private struct NeighbourInsertion : ISpatialQueryProcessor<AgentMove>
        {
            public AgentMove Current;
            public float QueryDistance;
            public int MaxNeighbours;
            public NativeList<AgentNeighbor> Neighbours;

            public void Process(AgentMove neighbour)
            {
                if (Current.Equals(neighbour))
                {
                    return;
                }

                float distanceSq = math.lengthsq(Current.Position - neighbour.Position);
                if (distanceSq >= QueryDistance * QueryDistance)
                {
                    return;
                }

                if (Neighbours.Length < MaxNeighbours)
                {
                    Neighbours.Add(default);
                }

                int i = Neighbours.Length - 1;
                while (i != 0 && distanceSq < Neighbours[i - 1].Distance)
                {
                    Neighbours[i] = Neighbours[i - 1];
                    --i;
                }

                Neighbours[i] = new AgentNeighbor { Distance = distanceSq, Agent = ToAgent(neighbour) };
            }
        }
    }
}
