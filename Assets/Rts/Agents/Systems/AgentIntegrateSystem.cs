using GridNav;
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
    public partial struct AgentIntegrateSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
        }

        public void OnUpdate(ref SystemState state)
        {
            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;
            float deltaTime = SystemAPI.Time.DeltaTime;

            // WithPresent, because ArrivedTag is disabled on exactly the agents this loop is for.
            foreach ((RefRW<AgentMove> agent, RefRO<PathFollow> path,
                      EnabledRefRW<PathFollow> walking, EnabledRefRW<ArrivedTag> arrived)
                     in SystemAPI.Query<RefRW<AgentMove>, RefRO<PathFollow>,
                                        EnabledRefRW<PathFollow>, EnabledRefRW<ArrivedTag>>()
                                 .WithPresent<ArrivedTag>())
            {
                AgentMove move = agent.ValueRO;
                move.Position = Advance(map, move, deltaTime);

                float2 goalPoint = GridCoords.CellCenter(path.ValueRO.GoalCell);
                if (math.distance(move.Position, goalPoint) <= path.ValueRO.ArriveDistance)
                {
                    // Both velocities, not just the current one: nothing updates PrefVelocity once
                    // PathFollow is off, so a leftover value would keep being honoured.
                    move.Velocity = float2.zero;
                    move.PrefVelocity = float2.zero;
                    walking.ValueRW = false;
                    arrived.ValueRW = true;
                }

                agent.ValueRW = move;
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
