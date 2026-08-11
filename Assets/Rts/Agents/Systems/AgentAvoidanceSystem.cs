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
        private ComponentLookup<AgentMove> _agents;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AgentSpatialHash>();

            _agents = state.GetComponentLookup<AgentMove>();
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

            _agents.Update(ref state);

            state.Dependency = new AgentVelocityJob
            {
                Entities = entities,
                Agents = _agents,
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

        /// <summary>Seconds ahead ORCA plans around another agent.</summary>
        private const float TIME_HORIZON_AGENT = 1f;

        /// <summary>Unused until building outlines are fed in as obstacles (§5).</summary>
        private const float TIME_HORIZON_OBSTACLE = 1f;

        /// <summary>
        /// How much of <see cref="AgentMove.Radius"/> is body as far as avoidance is concerned. The rest is
        /// personal space that a crowd is allowed to squeeze out of: at a one-cell doorway, two agents kept a
        /// full radius each apart cannot both be near the door, so they shove each other away from it forever.
        ///
        /// The terrain clamp in <see cref="AgentIntegrateSystem"/> keeps using the full radius - overlapping a
        /// neighbour a little is a look, overlapping a wall is a bug.
        /// </summary>
        private const float BODY_RADIUS_SCALE = 0.5f;

        /// <summary>Seconds a standing agent needs to reach full speed, and the same to turn or stop.</summary>
        private const float SPEED_RAMP_SECONDS = 0.25f;

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

            move.Velocity = Ramp(move.Velocity, velocity, agent.MaxSpeed);
            Agents[entity] = move;

            neighbours.Dispose();
            orcaLines.Dispose();
        }

        /// <summary>
        /// Moves the velocity towards what RVO asked for at a finite rate instead of jumping to it. RVO
        /// re-solves from scratch every frame, so its answer swings between frames as neighbours shuffle; a
        /// body with no inertia follows every swing and the crowd twitches. The ramp is also what rounds the
        /// 45 degree corners the flow field hands out into something a person would walk.
        /// </summary>
        private float2 Ramp(float2 current, float2 wanted, float maxSpeed)
        {
            float maxChange = maxSpeed / SPEED_RAMP_SECONDS / InverseTimeStep; // acceleration * deltaTime

            float2 change = wanted - current;
            float length = math.length(change);

            return length <= maxChange ? wanted : current + change / length * maxChange;
        }

        private static Agent ToAgent(in AgentMove move) => new()
        {
            ObjectId = move.Entity.Index,
            Position = move.Position,
            Velocity = move.Velocity,
            PrefVelocity = move.PrefVelocity,
            MaxSpeed = move.MaxSpeed,
            Radius = move.Radius * BODY_RADIUS_SCALE,
            MaxNeighbors = MAX_NEIGHBOURS,
            NeighborDist = SightDistance(move),
            TimeHorizonAgent = TIME_HORIZON_AGENT,
            TimeHorizonObstacle = TIME_HORIZON_OBSTACLE,
        };

        /// <summary>
        /// How far away a neighbour is still worth knowing about, derived from the horizon rather than set on
        /// its own.
        ///
        /// The two cannot be allowed to disagree. ORCA plans a whole <see cref="TIME_HORIZON_AGENT"/> ahead,
        /// so sight shorter than the ground covered in that time means a neighbour's constraint does not
        /// exist until the two are nearly touching - and then arrives as a demand for a velocity change the
        /// agent has no time to make. That is what "avoidance reacts too late" is: not a weak horizon, a blind
        /// agent. A flat one cell of sight gave two agents closing head-on at twice top speed a fifth of a
        /// second of warning.
        /// </summary>
        private static float SightDistance(in AgentMove move) =>
            move.MaxSpeed * TIME_HORIZON_AGENT + move.Radius * 2f;

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

                // Full already, and this one is no closer than the farthest kept, so it is not one of the
                // nearest few. Without the test the write below lands on the last slot regardless of distance,
                // which makes that slot whoever happened to be processed last however far away it was - a real
                // neighbour dropped for one that does not matter. The wider the sight distance the more often
                // that happens, so this has to hold before the range above is worth widening.
                if (Neighbours.Length == MaxNeighbours
                    && distanceSq >= Neighbours[MaxNeighbours - 1].Distance)
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
