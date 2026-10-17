using GridNav;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Moves agents by their velocity, keeps them out of blocked cells, and notices arrivals
    /// (design §13.3 #17).
    ///
    /// The clamp is the cheap half of the "agents do not end up inside things" story. RVO knows about other
    /// agents but not about walls, so in a dense crowd an agent shoved sideways can land inside a footprint
    /// or a tree. Rather than pay for an obstacle per tree - 20k trees would be 80k obstacle vertices - a
    /// move that would end in a blocked cell is simply refused, one axis at a time (§3).
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(AgentAvoidanceSystem))]
    [BurstCompile]
    public partial struct AgentIntegrateSystem : ISystem
    {
        private EntityQuery _walking;
        private ComponentTypeHandle<AgentMove> _moves;
        private ComponentTypeHandle<PathFollow> _follows;
        private ComponentTypeHandle<ArrivedTag> _arrived;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();

            // Present, not All, for ArrivedTag: it is disabled on exactly the agents this is for.
            _walking = SystemAPI.QueryBuilder()
                                .WithAllRW<AgentMove, PathFollow>()
                                .WithPresentRW<ArrivedTag>()
                                .Build();

            _moves = state.GetComponentTypeHandle<AgentMove>();
            _follows = state.GetComponentTypeHandle<PathFollow>();
            _arrived = state.GetComponentTypeHandle<ArrivedTag>();
        }

        /// <remarks>
        /// A parallel job (RtsPerformance.md §4): the map is immutable for the whole of
        /// <c>SimulationSystemGroup</c> (§13.2 invariant 1), and each agent writes only its own position and
        /// its own two flags.
        ///
        /// Completed before returning. The map is containers inside a singleton, which ECS does not track, so
        /// nothing else would stop the next grid write from landing while the job still reads it.
        ///
        /// A chunk job rather than an entity job, because it reads <see cref="PathFollow"/> and switches it off
        /// on arrival, and an entity job cannot take a component and its enabled bit through two handles.
        /// </remarks>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _moves.Update(ref state);
            _follows.Update(ref state);
            _arrived.Update(ref state);

            state.Dependency = new IntegrateJob
            {
                Map = SystemAPI.GetSingleton<GridWorld>().Map,
                DeltaTime = SystemAPI.Time.DeltaTime,
                Moves = _moves,
                Follows = _follows,
                Arrived = _arrived,
            }.ScheduleParallel(_walking, state.Dependency);

            state.Dependency.Complete();
        }

        [BurstCompile]
        private struct IntegrateJob : IJobChunk
        {
            [ReadOnly] public GridMap Map;
            public float DeltaTime;

            public ComponentTypeHandle<AgentMove> Moves;
            public ComponentTypeHandle<PathFollow> Follows;
            public ComponentTypeHandle<ArrivedTag> Arrived;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                                in v128 chunkEnabledMask)
            {
                NativeArray<AgentMove> moves = chunk.GetNativeArray(ref Moves);
                NativeArray<PathFollow> follows = chunk.GetNativeArray(ref Follows);
                EnabledMask walking = chunk.GetEnabledMask(ref Follows);
                EnabledMask arrived = chunk.GetEnabledMask(ref Arrived);

                var agents = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (agents.NextEntityIndex(out int i))
                {
                    AgentMove move = moves[i];
                    PathFollow path = follows[i];

                    move.Position = Advance(Map, move, DeltaTime);

                    float2 goalPoint = GridCoords.CellCenter(path.GoalCell);
                    if (math.distance(move.Position, goalPoint) <= path.ArriveDistance)
                    {
                        // Both velocities, not just the current one: nothing updates PrefVelocity once
                        // PathFollow is off, so a leftover value would keep being honoured.
                        move.Velocity = float2.zero;
                        move.PrefVelocity = float2.zero;
                        walking[i] = false;
                        arrived[i] = true;
                    }

                    moves[i] = move;
                }
            }
        }

        /// <summary>
        /// Integrates one step, refusing the part of the move that would put the agent's edge inside a
        /// blocked cell. Axis by axis, so sliding along a wall still works instead of stopping dead.
        /// </summary>
        private static float2 Advance(in GridMap map, in AgentMove move, float deltaTime)
        {
            float2 wanted = move.Position + move.Velocity * deltaTime;
            float2 resolved = move.Position;

            if (IsClear(map, new float2(wanted.x, resolved.y), move.Radius, new float2(move.Velocity.x, 0f)))
            {
                resolved.x = wanted.x;
            }

            if (IsClear(map, new float2(resolved.x, wanted.y), move.Radius, new float2(0f, move.Velocity.y)))
            {
                resolved.y = wanted.y;
            }

            return resolved;
        }

        /// <summary>Whether the agent's leading edge would still be on a walkable cell.</summary>
        private static bool IsClear(in GridMap map, float2 position, float radius, float2 direction)
        {
            if (!map.IsPassable(GridCoords.CellOf(position)))
            {
                return false;
            }

            float2 lead = position + math.normalizesafe(direction) * radius;
            return map.IsPassable(GridCoords.CellOf(lead));
        }
    }
}
