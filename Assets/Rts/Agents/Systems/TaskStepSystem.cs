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

        /// <summary>
        /// How close a chase target has to be, in cells, before the chaser stops asking for a flow field and
        /// walks straight at it - provided nothing blocks the way (Docs/RtsBattleScaling.md P2).
        /// </summary>
        private const float DIRECT_CHASE_DISTANCE = 8f;

        /// <summary>Spacing of the samples along a straight chase that have to be passable, in cells.</summary>
        private const float CLEAR_WALK_STEP = 0.25f;

        /// <summary>
        /// Half the width of the corridor a straight chase checks, in cells: a body's radius and a little, so a
        /// line that grazes a tree's corner does not count as clear for something that is 0.84 across.
        /// </summary>
        private const float CLEAR_WALK_HALF_WIDTH = 0.45f;

        /// <summary>
        /// A far chase aims at the middle of the 4x4 block its target stands in rather than at the target's own
        /// cell, so soldiers closing on the same part of an enemy line share a field instead of each wanting
        /// one - and the goal moves a quarter as often (Docs/RtsBattleScaling.md P2).
        /// </summary>
        private const int FAR_GOAL_BLOCK = 4;

        /// <summary>
        /// How far outside a building's edge a soldier walking up to it aims, in cells: past a body's radius,
        /// so the point is somewhere a body can actually stand.
        /// </summary>
        private const float SIEGE_STAND_OFF = 0.6f;

        private ComponentLookup<AgentMove> _agents;
        private ComponentLookup<Health> _health;
        private ComponentLookup<Post> _posts;
        private BufferLookup<BuildingFootprintCell> _footprints;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InteractionQueue>();

            _agents = state.GetComponentLookup<AgentMove>(isReadOnly: true);
            _health = state.GetComponentLookup<Health>(isReadOnly: true);
            _posts = state.GetComponentLookup<Post>(isReadOnly: true);
            _footprints = state.GetBufferLookup<BuildingFootprintCell>(isReadOnly: true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            InteractionQueue interactions = SystemAPI.GetSingleton<InteractionQueue>();
            float deltaTime = SystemAPI.Time.DeltaTime;

            // Optional, because a chase can still be run on fields alone: without a grid there is simply never a
            // straight line to take.
            GridMap map = SystemAPI.TryGetSingleton(out GridWorld grid) ? grid.Map : default;

            _agents.Update(ref state);
            _health.Update(ref state);
            _posts.Update(ref state);
            _footprints.Update(ref state);

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
                        if (!TryChase(entity, step, map, ref follow.ValueRW, out bool closeEnough))
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
        ///
        /// The target is a body or a building. A building is chased to a standing place outside its wall and
        /// fought from weapon range of its nearest edge (<see cref="TryBesiege"/>).
        /// </summary>
        private bool TryChase(Entity chaser, in TaskStep step, in GridMap map, ref PathFollow follow,
                              out bool closeEnough)
        {
            closeEnough = false;

            if (!_agents.HasComponent(chaser))
            {
                return false;
            }

            if (_health.HasComponent(step.Target) && !_health[step.Target].IsAlive)
            {
                return false;
            }

            float2 here = _agents[chaser].Position;

            if (_agents.HasComponent(step.Target))
            {
                return TryChaseBody(chaser, step, map, here, ref follow, out closeEnough);
            }

            // A building: gone once demolished, and not yet anywhere until its footprint has been laid.
            return _footprints.HasBuffer(step.Target)
                   && FootprintBounds.TryOf(_footprints[step.Target], out FootprintBounds bounds)
                   && TryBesiege(chaser, step, map, here, bounds, ref follow, out closeEnough);
        }

        private bool TryChaseBody(Entity chaser, in TaskStep step, in GridMap map, float2 here,
                                  ref PathFollow follow, out bool closeEnough)
        {
            closeEnough = false;

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

            if (math.lengthsq(target - here) <= step.ArriveDistance * step.ArriveDistance)
            {
                closeEnough = true;
                return true;
            }

            Pursue(step, map, here, target, GridCoords.CellOf(target), shareFarGoal: true, ref follow);
            return true;
        }

        /// <summary>
        /// Walks up to a building and stops at weapon range from its nearest edge (see
        /// <see cref="FootprintBounds"/>). The cell walked to is a standing place just outside the wall on the
        /// side the soldier is coming from, because the building's own cells are the one place nobody can stand.
        /// </summary>
        private bool TryBesiege(Entity chaser, in TaskStep step, in GridMap map, float2 here,
                                in FootprintBounds bounds, ref PathFollow follow, out bool closeEnough)
        {
            closeEnough = false;

            // The same leash rule as for a body, measured to the nearest part of the building.
            if (_posts.HasComponent(chaser))
            {
                Post post = _posts[chaser];
                if (post.Leash > 0f && bounds.DistanceSq(post.Home) > post.Leash * post.Leash)
                {
                    return false;
                }
            }

            if (bounds.DistanceSq(here) <= step.ArriveDistance * step.ArriveDistance)
            {
                closeEnough = true;
                return true;
            }

            float2 edge = bounds.ClosestPoint(here);
            float2 outward = here - edge;
            if (math.lengthsq(outward) < math.EPSILON)
            {
                outward = here - bounds.Centre;
            }

            float2 standOff = edge + math.normalizesafe(outward, new float2(0f, -1f)) * SIEGE_STAND_OFF;
            int2 cell = StandingCell(map, bounds, here, GridCoords.CellOf(standOff));

            // A building does not move, so its approach cell is already shared by everyone coming from that
            // side; a far block would only walk them somewhere beside it.
            Pursue(step, map, here, GridCoords.CellCenter(cell), cell, shareFarGoal: false, ref follow);
            return true;
        }

        /// <summary>
        /// The cell to walk to: <paramref name="preferred"/> if a body can stand there, otherwise the free cell
        /// in the ring round the footprint nearest the soldier. Without a grid, the preferred cell regardless.
        /// </summary>
        private static int2 StandingCell(in GridMap map, in FootprintBounds bounds, float2 here, int2 preferred)
        {
            if (!map.IsCreated || map.IsPassable(preferred))
            {
                return preferred;
            }

            // The footprint's own cells, grown by one on every side; only the outer ring is looked at.
            int2 min = GridCoords.CellOf(bounds.Min) - 1;
            int2 max = GridCoords.CellOf(bounds.Max - 0.5f) + 1;

            int2 best = preferred;
            float bestSq = float.MaxValue;

            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    bool ring = x == min.x || x == max.x || y == min.y || y == max.y;
                    var cell = new int2(x, y);

                    if (!ring || !map.IsPassable(cell))
                    {
                        continue;
                    }

                    float distanceSq = math.distancesq(GridCoords.CellCenter(cell), here);
                    if (distanceSq < bestSq)
                    {
                        bestSq = distanceSq;
                        best = cell;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Points a chase at <paramref name="aim"/>: straight there when it is close and in plain sight,
        /// otherwise along a field to <paramref name="goalCell"/> - or, for a far body, to the middle of the
        /// block it stands in, so soldiers closing on the same part of a line share one field.
        /// </summary>
        private static void Pursue(in TaskStep step, in GridMap map, float2 here, float2 aim, int2 goalCell,
                                   bool shareFarGoal, ref PathFollow follow)
        {
            // Close and in plain sight: straight at it, no field. The goal cell is still kept on the target, so
            // anything reading where this agent is going gets an honest answer.
            if (map.IsCreated && HasClearWalk(map, here, aim))
            {
                if (!follow.GoalCell.Equals(goalCell))
                {
                    follow = Walk(follow, Aimed(step, goalCell));
                }

                follow.Direct = true;
                follow.DirectPoint = aim;
                return;
            }

            // Only far chases share a block. One that is close but blocked - a tree in the way - needs the field
            // to lead it round to the target itself, not to the middle of a block beside it.
            bool far = math.distancesq(here, aim) > DIRECT_CHASE_DISTANCE * DIRECT_CHASE_DISTANCE;
            int2 goal = map.IsCreated && far && shareFarGoal ? FarGoal(map, goalCell) : goalCell;

            if (!follow.GoalCell.Equals(goal) || follow.Direct)
            {
                follow = Walk(follow, Aimed(step, goal));
            }
        }

        private static TaskStep Aimed(in TaskStep step, int2 cell)
        {
            TaskStep aimed = step;
            aimed.Cell = cell;
            return aimed;
        }

        /// <summary>
        /// Whether a body could walk the straight line from one point to the other: within
        /// <see cref="DIRECT_CHASE_DISTANCE"/>, and every sample along the middle and both edges of a body-wide
        /// corridor on a passable cell.
        /// </summary>
        private static bool HasClearWalk(in GridMap map, float2 from, float2 to)
        {
            float2 delta = to - from;
            float length = math.length(delta);

            if (length > DIRECT_CHASE_DISTANCE)
            {
                return false;
            }

            if (length < math.EPSILON)
            {
                return true;
            }

            float2 side = new float2(-delta.y, delta.x) / length * CLEAR_WALK_HALF_WIDTH;
            int samples = (int)math.ceil(length / CLEAR_WALK_STEP);

            for (int i = 1; i <= samples; i++)
            {
                float2 point = from + delta * (i / (float)samples);

                if (!map.IsPassable(GridCoords.CellOf(point))
                    || !map.IsPassable(GridCoords.CellOf(point + side))
                    || !map.IsPassable(GridCoords.CellOf(point - side)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The middle of the <see cref="FAR_GOAL_BLOCK"/> block a far target stands in, or the target's own
        /// cell when that middle is not somewhere a field can lead to.
        /// </summary>
        private static int2 FarGoal(in GridMap map, int2 cell)
        {
            int2 block = (int2)math.floor((float2)cell / FAR_GOAL_BLOCK) * FAR_GOAL_BLOCK + FAR_GOAL_BLOCK / 2;
            return map.IsPassable(block) ? block : cell;
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

            // Every walk starts on the field; only a chase that can see its target turns this back on.
            follow.Direct = false;

            return follow;
        }
    }
}
