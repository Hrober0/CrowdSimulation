using GridNav;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

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

        private ComponentLookup<AgentMove> _agents;
        private ComponentLookup<Health> _health;
        private ComponentLookup<Post> _posts;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InteractionQueue>();

            _agents = state.GetComponentLookup<AgentMove>(isReadOnly: true);
            _health = state.GetComponentLookup<Health>(isReadOnly: true);
            _posts = state.GetComponentLookup<Post>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            InteractionQueue interactions = SystemAPI.GetSingleton<InteractionQueue>();
            float deltaTime = SystemAPI.Time.DeltaTime;

            _agents.Update(ref state);
            _health.Update(ref state);
            _posts.Update(ref state);

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

                    case TaskStepKind.Engage:
                    {
                        // Re-aimed every tick, because this is the one step whose destination walks away.
                        // Everything that ends a chase ends it here: the target dying, the target getting
                        // out of the leash, or the soldier getting close enough that the weapon can do the
                        // rest without anybody taking another step.
                        if (!TryChase(entity, step, ref follow.ValueRW, out bool closeEnough))
                        {
                            walking.ValueRW = false;
                            arrived.ValueRW = false;
                            steps.RemoveAt(0);
                            break;
                        }

                        if (closeEnough)
                        {
                            // In range and standing. The weapon is fired by AttackSystem, which needs no
                            // telling - it shoots whatever is nearest, and this is what put the soldier
                            // where that is the right thing.
                            walking.ValueRW = false;
                            break;
                        }

                        arrived.ValueRW = false;
                        walking.ValueRW = true;
                        break;
                    }

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
                                Cell = step.Cell,
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
        /// <summary>
        /// Keeps a chase pointed at its target, and says whether there is still a chase to keep.
        ///
        /// False means the step is over, for one of three reasons that all end the same way: the target is
        /// gone or dead, the chaser has no body to chase with, or the *target* has left the chaser's leash.
        /// The leash is measured from the post rather than from the soldier, which is what makes it a limit
        /// on where the fight may happen rather than on how far a soldier may walk in one go - a soldier
        /// dragged out and released comes back because the order stops being handed to it, not because it
        /// hit an invisible wall.
        /// </summary>
        private bool TryChase(Entity chaser, in TaskStep step, ref PathFollow follow, out bool closeEnough)
        {
            closeEnough = false;

            if (!_agents.HasComponent(chaser) || !_agents.HasComponent(step.Target))
            {
                return false;
            }

            if (_health.HasComponent(step.Target) && !_health[step.Target].IsAlive)
            {
                return false;
            }

            // Out of the world rather than out of range: a target that has stepped inside a building has
            // AgentMove disabled, cannot be shot, and is not worth standing outside for.
            if (!_agents.IsComponentEnabled(step.Target))
            {
                return false;
            }

            float2 target = _agents[step.Target].Position;

            if (_posts.HasComponent(chaser))
            {
                Post post = _posts[chaser];
                if (post.Leash > 0f && !post.IsWithinLeash(target))
                {
                    return false;
                }
            }

            float2 here = _agents[chaser].Position;
            if (math.lengthsq(target - here) <= step.ArriveDistance * step.ArriveDistance)
            {
                closeEnough = true;
                return true;
            }

            int2 cell = GridCoords.CellOf(target);
            if (!follow.GoalCell.Equals(cell))
            {
                TaskStep aimed = step;
                aimed.Cell = cell;
                follow = Walk(follow, aimed);
            }

            return true;
        }

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
