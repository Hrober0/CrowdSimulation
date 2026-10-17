using GridNav;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Gives up on a walk that is going nowhere (design §8, §13.3 #21).
    ///
    /// This is the one generic rule behind the last row of §8's table of ways agents get stuck. Every other
    /// failure mode there is prevented structurally - reservations cap commitments, claims happen before
    /// approach, strict priority forbids ping-pong - but two agents wedged against each other in a corridor
    /// is a *geometry* problem, and geometry cannot be reasoned about in advance. So it is not prevented; it
    /// is noticed.
    ///
    /// It notices two different things, and the difference is worth the two constants. **A stall is a guess**:
    /// an agent that has not got anywhere in five seconds is *probably* wedged, and five seconds is long
    /// enough that a busy junction gets to resolve itself first. **No route is a fact**: the destination's own
    /// flow field says there is no way in from where the agent stands, which is not a suspicion to sit on for
    /// five seconds - most often it is a road the player has just painted one-way. Waiting out the full stall
    /// window for it is what makes an agent look permanently frozen while it is really being handed the same
    /// impossible walk over and over.
    ///
    /// Noticing is deliberately blunt: drop the task. Everything that follows is machinery that already
    /// exists - <see cref="OrderCompletionSystem"/> unwinds the reservations and the interior claim,
    /// <see cref="StorageRequestSystem"/> re-posts the demand because the demand never went away, and the
    /// agent is picked up as free. There is no special "recovering" state, and nothing had to be taught what
    /// a deadlock is. What stops the same impossible walk being handed straight back is that the assigning
    /// systems ask the same field the same question before they hand anything out.
    ///
    /// Only walking agents are watched. An agent standing through an <c>Interact</c>, waiting for a door,
    /// resting in a hut or idle on the ground all have <see cref="PathFollow"/> disabled, so standing still is
    /// not something the watchdog can mistake for being stuck.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(OrderCompletionSystem))]
    public partial struct WatchdogSystem : ISystem
    {
        /// <summary>Long enough that a crowded junction resolves itself first, short enough to be unseen.</summary>
        private const float STALL_SECONDS = 5f;

        /// <summary>
        /// How long a walk with no route at all is given. Not zero: a field is a frame behind the world it
        /// describes (§13.2 invariant 3), so a route that opened this frame deserves to be noticed before the
        /// task that would have used it is thrown away.
        /// </summary>
        private const float NO_ROUTE_SECONDS = 0.5f;

        /// <summary>
        /// How far counts as having got somewhere. Half a cell, because anything smaller is inside the range
        /// avoidance shoves agents around by while they queue.
        /// </summary>
        private const float PROGRESS_DISTANCE = 0.5f;

        private EntityQuery _walking;
        private ComponentTypeHandle<AgentMove> _moves;
        private ComponentTypeHandle<PathFollow> _follows;
        private ComponentTypeHandle<MovementWatchdog> _watchdogs;
        private BufferTypeHandle<TaskStep> _steps;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
            state.RequireForUpdate<FlowFieldCache>();

            _walking = SystemAPI.QueryBuilder()
                                .WithAll<AgentMove>()
                                .WithAllRW<PathFollow, MovementWatchdog>()
                                .WithAllRW<TaskStep>()
                                .Build();

            _moves = state.GetComponentTypeHandle<AgentMove>(isReadOnly: true);
            _follows = state.GetComponentTypeHandle<PathFollow>();
            _watchdogs = state.GetComponentTypeHandle<MovementWatchdog>();
            _steps = state.GetBufferTypeHandle<TaskStep>();
        }

        /// <remarks>
        /// A parallel job: each agent reads the immutable map and the field cache and writes only its own
        /// timer, steps and walking bit. A chunk job for the reason <see cref="AgentIntegrateSystem"/> gives:
        /// it reads <see cref="PathFollow"/> and switches it off.
        ///
        /// Completed before returning, for the reason integrate gives: the cache is containers inside a
        /// singleton, and next frame's field system writes them with nothing to make it wait.
        /// </remarks>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _moves.Update(ref state);
            _follows.Update(ref state);
            _watchdogs.Update(ref state);
            _steps.Update(ref state);

            state.Dependency = new WatchJob
            {
                Map = SystemAPI.GetSingleton<GridWorld>().Map,
                Cache = SystemAPI.GetSingleton<FlowFieldCache>(),
                DeltaTime = SystemAPI.Time.DeltaTime,
                Moves = _moves,
                Follows = _follows,
                Watchdogs = _watchdogs,
                Steps = _steps,
            }.ScheduleParallel(_walking, state.Dependency);

            state.Dependency.Complete();
        }

        [BurstCompile]
        private struct WatchJob : IJobChunk
        {
            [ReadOnly] public GridMap Map;
            [ReadOnly] public FlowFieldCache Cache;
            public float DeltaTime;

            [ReadOnly] public ComponentTypeHandle<AgentMove> Moves;
            public ComponentTypeHandle<PathFollow> Follows;
            public ComponentTypeHandle<MovementWatchdog> Watchdogs;
            public BufferTypeHandle<TaskStep> Steps;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                                in v128 chunkEnabledMask)
            {
                NativeArray<AgentMove> moves = chunk.GetNativeArray(ref Moves);
                NativeArray<PathFollow> follows = chunk.GetNativeArray(ref Follows);
                NativeArray<MovementWatchdog> watchdogs = chunk.GetNativeArray(ref Watchdogs);
                BufferAccessor<TaskStep> steps = chunk.GetBufferAccessor(ref Steps);
                EnabledMask walking = chunk.GetEnabledMask(ref Follows);

                var agents = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (agents.NextEntityIndex(out int i))
                {
                    MovementWatchdog timer = watchdogs[i];
                    float2 position = moves[i].Position;
                    PathFollow follow = follows[i];

                    // Waiting a turn is not being stuck. Safe to exempt because only the ranks *behind* the front
                    // of a queue ever hold: the agent at the front is still watched, so a head that really is
                    // wedged is still given up on and the line moves up (see ArrivalQueueSystem). A held agent
                    // always has a route, too - the queue drops the ones that do not.
                    if (follow.Holding)
                    {
                        watchdogs[i] = Reset(timer, position);
                        continue;
                    }

                    // Asked of the agent's own cost model: a soldier that can break through a wall has a route
                    // where a hauler does not, and giving up on its walk because the *civilian* field says there
                    // is no way round would be giving up on the way in.
                    bool noRoute = Cache.IsKnownUnreachable(
                        follow.GoalCell, GridCoords.CellOf(position), Map, follow.Traversal);

                    // Progress means nothing when there is nowhere to progress *to*: an agent with no route can
                    // still be shoved half a cell by a crowd, and reading that as "getting somewhere" is what
                    // would keep it walking at a wall forever.
                    if (!noRoute
                        && math.distancesq(position, timer.LastProgressPosition)
                        >= PROGRESS_DISTANCE * PROGRESS_DISTANCE)
                    {
                        watchdogs[i] = Reset(timer, position);
                        continue;
                    }

                    timer.StalledSeconds += DeltaTime;
                    if (timer.StalledSeconds >= (noRoute ? NO_ROUTE_SECONDS : STALL_SECONDS))
                    {
                        steps[i].Clear();
                        walking[i] = false;

                        timer = Reset(timer, position);
                    }

                    watchdogs[i] = timer;
                }
            }
        }

        private static MovementWatchdog Reset(MovementWatchdog timer, float2 position)
        {
            timer.LastProgressPosition = position;
            timer.StalledSeconds = 0f;
            return timer;
        }
    }
}
