using GridNav;
using Unity.Burst;
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

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
            state.RequireForUpdate<FlowFieldCache>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;
            FlowFieldCache cache = SystemAPI.GetSingleton<FlowFieldCache>();
            float deltaTime = SystemAPI.Time.DeltaTime;

            foreach ((RefRO<AgentMove> agent, RefRO<PathFollow> follow, RefRW<MovementWatchdog> watchdog,
                      EnabledRefRW<PathFollow> walking, DynamicBuffer<TaskStep> steps)
                     in SystemAPI.Query<RefRO<AgentMove>, RefRO<PathFollow>, RefRW<MovementWatchdog>,
                                        EnabledRefRW<PathFollow>, DynamicBuffer<TaskStep>>())
            {
                MovementWatchdog timer = watchdog.ValueRO;
                float2 position = agent.ValueRO.Position;

                // Waiting a turn is not being stuck. Safe to exempt because only the ranks *behind* the front
                // of a queue ever hold: the agent at the front is still watched, so a head that really is
                // wedged is still given up on and the line moves up (see ArrivalQueueSystem). A held agent
                // always has a route, too - the queue drops the ones that do not.
                if (follow.ValueRO.Holding)
                {
                    watchdog.ValueRW = Reset(timer, position);
                    continue;
                }

                bool noRoute = cache.IsKnownUnreachable(
                    follow.ValueRO.GoalCell, GridCoords.CellOf(position), map);

                // Progress means nothing when there is nowhere to progress *to*: an agent with no route can
                // still be shoved half a cell by a crowd, and reading that as "getting somewhere" is what
                // would keep it walking at a wall forever.
                if (!noRoute
                    && math.distancesq(position, timer.LastProgressPosition)
                    >= PROGRESS_DISTANCE * PROGRESS_DISTANCE)
                {
                    watchdog.ValueRW = Reset(timer, position);
                    continue;
                }

                timer.StalledSeconds += deltaTime;
                if (timer.StalledSeconds >= (noRoute ? NO_ROUTE_SECONDS : STALL_SECONDS))
                {
                    steps.Clear();
                    walking.ValueRW = false;

                    timer = Reset(timer, position);
                }

                watchdog.ValueRW = timer;
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
