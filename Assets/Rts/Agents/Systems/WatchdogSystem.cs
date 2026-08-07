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
    /// Noticing is deliberately blunt: drop the task. Everything that follows is machinery that already
    /// exists - <see cref="OrderCompletionSystem"/> unwinds the reservations and the interior claim,
    /// <see cref="StorageRequestSystem"/> re-posts the demand because the demand never went away, and the
    /// agent is picked up as free. There is no special "recovering" state, and nothing had to be taught what
    /// a deadlock is.
    ///
    /// Only walking agents are watched. An agent standing through an <c>Interact</c>, resting in a hut or
    /// idle on the ground all have <see cref="PathFollow"/> disabled, so standing still is not something the
    /// watchdog can mistake for being stuck.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(OrderCompletionSystem))]
    public partial struct WatchdogSystem : ISystem
    {
        /// <summary>Long enough that a crowded junction resolves itself first, short enough to be unseen.</summary>
        private const float STALL_SECONDS = 5f;

        /// <summary>
        /// How far counts as having got somewhere. Half a cell, because anything smaller is inside the range
        /// avoidance shoves agents around by while they queue.
        /// </summary>
        private const float PROGRESS_DISTANCE = 0.5f;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;

            foreach ((RefRO<AgentMove> agent, RefRW<MovementWatchdog> watchdog,
                      EnabledRefRW<PathFollow> walking, DynamicBuffer<TaskStep> steps)
                     in SystemAPI.Query<RefRO<AgentMove>, RefRW<MovementWatchdog>,
                                        EnabledRefRW<PathFollow>, DynamicBuffer<TaskStep>>())
            {
                MovementWatchdog timer = watchdog.ValueRO;
                float2 position = agent.ValueRO.Position;

                if (math.distancesq(position, timer.LastProgressPosition)
                    >= PROGRESS_DISTANCE * PROGRESS_DISTANCE)
                {
                    timer.LastProgressPosition = position;
                    timer.StalledSeconds = 0f;
                    watchdog.ValueRW = timer;
                    continue;
                }

                timer.StalledSeconds += deltaTime;
                if (timer.StalledSeconds >= STALL_SECONDS)
                {
                    steps.Clear();
                    walking.ValueRW = false;

                    timer.StalledSeconds = 0f;
                    timer.LastProgressPosition = position;
                }

                watchdog.ValueRW = timer;
            }
        }
    }
}
