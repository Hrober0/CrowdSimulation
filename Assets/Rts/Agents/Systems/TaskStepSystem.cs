using Unity.Burst;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Runs the head of every agent's task (design §9, §13.3 #13). The whole of "what an agent is doing" is
    /// this switch plus whatever <see cref="TaskStepKind.Interact"/> is made to mean per kind - there is no
    /// worker state machine, no hauler state machine and no soldier state machine.
    ///
    /// At most one step is retired per agent per frame, on purpose. Arrival is noticed during integration and
    /// consumed at the top of the next frame (§13.3), so an arrival that closed a <c>GoTo</c> belongs to that
    /// <c>GoTo</c> and to nothing else; retiring two steps at once is how it would end up closing the next
    /// one as well.
    ///
    /// The one step this does not run is a door: an <c>Enter</c> or <c>Exit</c> is left at the head for
    /// <see cref="InteriorTransitionSystem"/>, which is what makes the step itself the record of "waiting for
    /// a door" (§15).
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(AgentSpatialHashSystem))]
    [UpdateBefore(typeof(PathRouteSystem))]
    public partial struct TaskStepSystem : ISystem
    {
        /// <summary>Used when a task step is given to an agent that has never been told how close is close enough.</summary>
        private const float DEFAULT_ARRIVE_DISTANCE = 0.4f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InteractionQueue>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            InteractionQueue interactions = SystemAPI.GetSingleton<InteractionQueue>();
            float deltaTime = SystemAPI.Time.DeltaTime;

            // WithPresent on both, because an agent inside a building has PathFollow disabled and is exactly
            // the agent whose Interact steps - a whole shift of them - still have to run.
            foreach ((DynamicBuffer<TaskStep> steps, RefRW<PathFollow> follow,
                      EnabledRefRW<PathFollow> walking, EnabledRefRW<ArrivedTag> arrived, Entity entity)
                     in SystemAPI.Query<DynamicBuffer<TaskStep>, RefRW<PathFollow>,
                                        EnabledRefRW<PathFollow>, EnabledRefRW<ArrivedTag>>()
                                 .WithPresent<PathFollow, ArrivedTag>()
                                 .WithEntityAccess())
            {
                if (steps.IsEmpty)
                {
                    continue;
                }

                TaskStep step = steps[0];

                switch (step.Kind)
                {
                    case TaskStepKind.GoTo:
                        if (walking.ValueRO)
                        {
                            // Still on the way. Retargeting only matters when a step was pushed over a walk
                            // already in progress, which the goal comparison is the whole test for.
                            if (!follow.ValueRO.GoalCell.Equals(step.Cell))
                            {
                                follow.ValueRW = Walk(follow.ValueRO, step);
                            }

                            break;
                        }

                        // The goal has to match: an arrival left over from an earlier walk says nothing
                        // about this step, and consuming it would report the agent already there.
                        if (arrived.ValueRO && follow.ValueRO.GoalCell.Equals(step.Cell))
                        {
                            arrived.ValueRW = false;
                            steps.RemoveAt(0);
                            break;
                        }

                        arrived.ValueRW = false;
                        follow.ValueRW = Walk(follow.ValueRO, step);
                        walking.ValueRW = true;
                        break;

                    case TaskStepKind.Enter:
                    case TaskStepKind.Exit:
                        // Left at the head on purpose. A doorway takes time and admits one agent at a time
                        // (§15), so this step *is* the request to use it: while it is here the agent is either
                        // queueing for the door or walking through it, and InteriorTransitionSystem is what
                        // retires it once the agent is through.
                        break;

                    case TaskStepKind.Interact:
                    {
                        // Counted down in place, so a long Interact does not rewrite the buffer every frame.
                        ref TaskStep head = ref steps.ElementAt(0);
                        head.Duration -= deltaTime;
                        if (head.Duration > 0f)
                        {
                            break;
                        }

                        if (step.Interaction != InteractionKind.None)
                        {
                            interactions.Enqueue(new InteractionEvent
                            {
                                Agent = entity,
                                Target = step.Target,
                                Kind = step.Interaction,
                            });
                        }

                        steps.RemoveAt(0);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Points an agent at a cell. <c>RoutedChunk = -1</c> is no chunk, which is what makes
        /// <see cref="PathRouteSystem"/> work out a route on the first frame rather than trust these values.
        /// </summary>
        private static PathFollow Walk(PathFollow follow, in TaskStep step)
        {
            follow.GoalCell = step.Cell;
            follow.WaypointCell = step.Cell;
            follow.RoutedGoal = step.Cell;
            follow.RoutedChunk = -1;

            // Taken from the step every time, not only when unset: a doorway's wider distance belongs to that
            // step and must not be inherited by whatever the agent is sent to do next.
            follow.ArriveDistance = step.ArriveDistance > 0f ? step.ArriveDistance : DEFAULT_ARRIVE_DISTANCE;

            // A new walk is not a hold, and it starts a fresh wait: whatever queue the agent was in was for a
            // destination it no longer has, and time spent in it must not be carried into the next one.
            follow.Holding = false;
            follow.QueuedSince = -1f;

            return follow;
        }
    }
}
