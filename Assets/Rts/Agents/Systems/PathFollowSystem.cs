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
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(PathRequestSystem))]
    public partial struct PathFollowSystem : ISystem
    {
        /// <summary>Distance from the *goal* at which the agent starts easing off, in cells.</summary>
        private const float SLOW_DOWN_DISTANCE = 1.5f;

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
            // Waiting its turn for a destination somebody else is using. The hold point is a cell or two away
            // on ground the agent is already standing on, so it is steered at directly - no field, no route.
            if (path.Holding)
            {
                return Steer(agent, path, path.HoldPoint);
            }

            int2 cell = GridCoords.CellOf(agent.Position);

            // Standing on the target cell there is nothing left to follow - walk at the exact point.
            if (cell.Equals(path.WaypointCell))
            {
                return Steer(agent, path, GridCoords.CellCenter(path.WaypointCell));
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
                ? Steer(agent, path, GridCoords.CellCenter(nextCell))
                : float2.zero;
        }

        /// <summary>
        /// Direction from the steering target, speed from the distance left to the goal.
        ///
        /// The two are deliberately not the same distance: the target is a cell centre at most a cell and a
        /// half away, so easing off towards *it* means easing off all the time - the agent crawls into every
        /// centre and accelerates out of it, which reads as a turn-based hop rather than a walk. Only the
        /// last stretch to the goal is worth braking for.
        /// </summary>
        private static float2 Steer(in AgentMove agent, in PathFollow path, float2 target)
        {
            float2 toTarget = target - agent.Position;
            float distance = math.length(toTarget);

            if (distance < math.EPSILON)
            {
                return float2.zero;
            }

            return KeepRight(toTarget / distance) * StoppingSpeed(agent, path);
        }

        /// <summary>Leans the heading a little to the agent's right. See <see cref="KEEP_RIGHT_LEAN"/>.</summary>
        private static float2 KeepRight(float2 direction)
        {
            // Right of travel in a Y-up plane. The input is already unit length.
            var right = new float2(direction.y, -direction.x);
            return math.normalize(direction + right * KEEP_RIGHT_LEAN);
        }

        /// <summary>
        /// Full speed unless the agent is walking at somewhere it means to stop, and then eased off over the
        /// last stretch. A gate is a point on the way and gets no brake; a goal and a queue hold both do.
        /// </summary>
        private static float StoppingSpeed(in AgentMove agent, in PathFollow path)
        {
            if (path.Holding)
            {
                return Eased(agent, math.distance(agent.Position, path.HoldPoint));
            }

            if (!path.WaypointCell.Equals(path.GoalCell))
            {
                return agent.MaxSpeed;
            }

            return Eased(agent, math.distance(agent.Position, GridCoords.CellCenter(path.GoalCell)));
        }

        private static float Eased(in AgentMove agent, float distance) =>
            distance < SLOW_DOWN_DISTANCE
                ? agent.MaxSpeed * (distance / SLOW_DOWN_DISTANCE)
                : agent.MaxSpeed;
    }
}
