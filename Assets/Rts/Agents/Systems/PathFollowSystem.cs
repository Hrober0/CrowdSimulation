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
        /// <summary>Distance from the target at which the agent starts easing off, in cells.</summary>
        private const float SLOW_DOWN_DISTANCE = 1.5f;

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
            int2 cell = GridCoords.CellOf(agent.Position);

            // Standing on the target cell there is nothing left to follow - walk at the exact point.
            if (cell.Equals(path.WaypointCell))
            {
                return Approach(agent, GridCoords.CellCenter(path.WaypointCell));
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
                ? Approach(agent, GridCoords.CellCenter(nextCell))
                : float2.zero;
        }

        private static float2 Approach(in AgentMove agent, float2 target)
        {
            float2 toTarget = target - agent.Position;
            float distance = math.length(toTarget);

            if (distance < math.EPSILON)
            {
                return float2.zero;
            }

            float speed = distance < SLOW_DOWN_DISTANCE
                ? agent.MaxSpeed * (distance / SLOW_DOWN_DISTANCE)
                : agent.MaxSpeed;

            return toTarget / distance * speed;
        }
    }
}
