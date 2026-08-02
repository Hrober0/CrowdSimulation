using GridNav;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Decides what an agent should be steering at: the goal itself, or the next gate on the way there.
    /// This is where the long-range tier meets the local one (design §4).
    ///
    /// The test is geometric and costs nothing: if the agent stands inside the window the goal's field
    /// covers, that field can steer it the whole rest of the way, so the gate graph is not consulted at all.
    /// Only agents further out than that walk gate to gate - and because the waypoint is a *cell*, every
    /// agent heading through the same gate shares one field for it, exactly as they would for a warehouse.
    ///
    /// A route is worked out again whenever the agent changes chunk, which keeps it honest when avoidance
    /// pushes an agent somewhere it did not intend to go, and removes any need to track progress along it.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(AgentSpatialHashSystem))]
    [UpdateBefore(typeof(PathRequestSystem))]
    public partial struct PathRouteSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
            state.RequireForUpdate<ChunkGateGraph>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;
            ChunkGateGraph graph = SystemAPI.GetSingleton<ChunkGateGraph>();

            var gates = new NativeList<int>(16, Allocator.Temp);

            foreach ((RefRO<AgentMove> agent, RefRW<PathFollow> follow, DynamicBuffer<PathRoute> route)
                     in SystemAPI.Query<RefRO<AgentMove>, RefRW<PathFollow>, DynamicBuffer<PathRoute>>())
            {
                PathFollow path = follow.ValueRO;
                int2 cell = GridCoords.CellOf(agent.ValueRO.Position);

                if (FlowField.Contains(FlowField.WindowMinFor(map, path.GoalCell), cell))
                {
                    // Near enough for the goal's own field to reach the agent. RoutedGoal is kept in step
                    // even though no route was worked out, so the component never claims to be routed for
                    // a goal the agent no longer has; RoutedChunk = -1 is what forces a route if it
                    // wanders back out of the window.
                    path.WaypointCell = path.GoalCell;
                    path.RoutedGoal = path.GoalCell;
                    path.RoutedChunk = -1;
                    route.Clear();
                    follow.ValueRW = path;
                    continue;
                }

                int chunk = graph.ChunkIndex(map.ChunkCoordOf(cell));
                if (!path.RoutedGoal.Equals(path.GoalCell) || path.RoutedChunk != chunk)
                {
                    gates.Clear();
                    GatePathFinder.TryFindGatePath(map, graph, cell, path.GoalCell, gates);

                    route.Clear();
                    foreach (int gate in gates)
                    {
                        route.Add(new PathRoute { GateIndex = gate });
                    }

                    path.RoutedGoal = path.GoalCell;
                    path.RoutedChunk = chunk;
                }

                // The far side of the next gate, so that walking to the waypoint crosses it.
                path.WaypointCell = route.IsEmpty
                    ? path.GoalCell
                    : graph.CellInChunk(route[0].GateIndex, graph.OtherChunkOf(route[0].GateIndex, chunk));

                follow.ValueRW = path;
            }

            gates.Dispose();
        }
    }
}
