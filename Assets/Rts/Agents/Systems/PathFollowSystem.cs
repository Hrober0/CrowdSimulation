using GridNav;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Turns the flow field gradient into a preferred velocity (design §4, tier 3, and §13.3 #15).
    ///
    /// The agent steers towards the centre of the next cell rather than along the raw gradient: a field
    /// steers to cell centres by construction, and with one tree able to block a whole cell, cutting corners
    /// is what puts an agent inside one (§3).
    ///
    /// Waiting a turn is the same machinery, not a special case. <see cref="ArrivalQueueSystem"/> hands an
    /// agent a distance it may not come closer than, and the agent keeps following the field until it is
    /// there: the queue decides *how close*, the field decides *which way*. That split is what makes a line
    /// form along the road the traffic actually arrives on, including when the way in is a detour round a
    /// one-way road, and it is why nothing here has to know where a door is.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(PathRequestSystem))]
    public partial struct PathFollowSystem : ISystem
    {
        /// <summary>Distance from the *goal* at which the agent starts easing off, in cells.</summary>
        private const float SLOW_DOWN_DISTANCE = 1.5f;

        /// <summary>
        /// How far inside its allotted distance a waiting agent is left in peace, in cells.
        ///
        /// Without a band an agent one millimetre too close would back off, immediately be too far out, walk
        /// in again, and shuffle on the spot forever - and a shuffling queue is exactly what this whole
        /// mechanism exists to stop being visible.
        /// </summary>
        private const float HOLD_BAND = 0.5f;

        /// <summary>Backing off is a step aside, not a retreat - it is done at half pace.</summary>
        private const float BACK_OFF_SPEED_FRACTION = 0.5f;

        /// <summary>
        /// How far the heading leans to the agent's right, as a fraction of it - about three degrees.
        ///
        /// ORCA is symmetric, so two agents meeting head-on are handed mirror-image constraints, choose the
        /// same side and stop dead instead of passing; nothing in the solver breaks that tie. A constant lean
        /// does: both veer to their own right and pass shoulder to shoulder. It is a road convention rather
        /// than a jitter, so it stays deterministic - the same world replays the same way - and in a crowded
        /// corridor the two directions of travel separate into lanes on their own.
        ///
        /// Invisible on an open walk: the heading is re-aimed at the next cell centre every frame, so three
        /// degrees never accumulates into a detour.
        /// </summary>
        private const float KEEP_RIGHT_LEAN = 0.06f;

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

            foreach ((RefRW<AgentMove> agent, RefRO<PathFollow> path)
                     in SystemAPI.Query<RefRW<AgentMove>, RefRO<PathFollow>>())
            {
                agent.ValueRW.PrefVelocity = PreferredVelocity(map, cache, agent.ValueRO, path.ValueRO);
            }
        }

        private static float2 PreferredVelocity(in GridMap map, in FlowFieldCache cache,
                                                in AgentMove agent, in PathFollow path)
        {
            float toGoal = math.distance(agent.Position, GridCoords.CellCenter(path.GoalCell));

            if (!path.Holding)
            {
                // A gate is a point on the way and gets no brake; a goal is somewhere the agent means to stop.
                float speed = path.WaypointCell.Equals(path.GoalCell) ? Eased(agent, toGoal) : agent.MaxSpeed;
                return Follow(map, cache, agent, path, speed);
            }

            float slack = toGoal - path.HoldDistance;

            // Not yet at its place in the line: the way there is the way in, so it follows the field and eases
            // to a stop where its place is rather than where the goal is.
            if (slack >= 0f)
            {
                return Follow(map, cache, agent, path, Eased(agent, slack));
            }

            return slack > -HOLD_BAND ? float2.zero : BackOff(map, cache, agent, path);
        }

        /// <summary>One step along the field towards <see cref="PathFollow.WaypointCell"/>.</summary>
        private static float2 Follow(in GridMap map, in FlowFieldCache cache,
                                     in AgentMove agent, in PathFollow path, float speed)
        {
            int2 cell = GridCoords.CellOf(agent.Position);

            // Standing on the target cell there is nothing left to follow - walk at the exact point.
            if (cell.Equals(path.WaypointCell))
            {
                return Steer(agent, GridCoords.CellCenter(path.WaypointCell), speed);
            }

            if (!cache.TryGetSlot(path.WaypointCell, out int slot))
            {
                return float2.zero; // the field was asked for and will be there next frame
            }

            if (!cache.TryGetDirection(slot, cell, out Direction step))
            {
                return float2.zero; // outside the window, or nowhere to go from here
            }

            int2 nextCell = cell + DirectionUtils.Offset(step);
            return map.IsPassable(nextCell)
                ? Steer(agent, GridCoords.CellCenter(nextCell), speed)
                : float2.zero;
        }

        /// <summary>
        /// One step *up* the field: back along a route that leads in, for an agent that has been given a place
        /// further out than where it is standing.
        ///
        /// Uphill rather than outward. Away-from-the-goal is a direction, and a direction is only a way out
        /// when the ground is open - back down the road the agent came in on is a *route*, which is what the
        /// field is made of. Every candidate is a legal step (<see cref="GridMap.CanTraverse"/>, so a one-way
        /// road is respected) onto a cell with a finite cost, meaning the agent can still get back in from it.
        /// The cost strictly increases, so this cannot walk in a circle.
        /// </summary>
        private static float2 BackOff(in GridMap map, in FlowFieldCache cache,
                                      in AgentMove agent, in PathFollow path)
        {
            if (!cache.TryGetSlot(path.GoalCell, out int slot))
            {
                return float2.zero;
            }

            int2 cell = GridCoords.CellOf(agent.Position);
            ushort best = cache.IntegrationAt(slot, cell);
            if (best == FlowField.UNREACHABLE)
            {
                return float2.zero;
            }

            int2 target = cell;
            for (int d = 0; d < DirectionUtils.DIRECTION_COUNT; d++)
            {
                var direction = (Direction)d;
                if (!map.CanTraverse(cell, direction))
                {
                    continue;
                }

                int2 neighbour = cell + DirectionUtils.Offset(direction);
                ushort cost = cache.IntegrationAt(slot, neighbour);
                if (cost == FlowField.UNREACHABLE || cost <= best)
                {
                    continue;
                }

                best = cost;
                target = neighbour;
            }

            // Nowhere further out that still leads back in. Standing still is then the honest answer, and the
            // agent is behind the front of a queue rather than in anyone's way.
            return target.Equals(cell)
                ? float2.zero
                : Steer(agent, GridCoords.CellCenter(target), agent.MaxSpeed * BACK_OFF_SPEED_FRACTION);
        }

        /// <summary>
        /// Direction from the steering target, speed from the caller.
        ///
        /// The two are deliberately not the same distance: the target is a cell centre at most a cell and a
        /// half away, so easing off towards *it* means easing off all the time - the agent crawls into every
        /// centre and accelerates out of it, which reads as a turn-based hop rather than a walk. Only the last
        /// stretch to somewhere the agent means to stop is worth braking for.
        /// </summary>
        private static float2 Steer(in AgentMove agent, float2 target, float speed)
        {
            float2 toTarget = target - agent.Position;
            float distance = math.length(toTarget);

            if (distance < math.EPSILON)
            {
                return float2.zero;
            }

            return KeepRight(toTarget / distance) * speed;
        }

        /// <summary>Leans the heading a little to the agent's right. See <see cref="KEEP_RIGHT_LEAN"/>.</summary>
        private static float2 KeepRight(float2 direction)
        {
            // Right of travel in a Y-up plane. The input is already unit length.
            var right = new float2(direction.y, -direction.x);
            return math.normalize(direction + right * KEEP_RIGHT_LEAN);
        }

        private static float Eased(in AgentMove agent, float distance) =>
            distance < SLOW_DOWN_DISTANCE
                ? agent.MaxSpeed * math.max(distance / SLOW_DOWN_DISTANCE, 0f)
                : agent.MaxSpeed;
    }
}
