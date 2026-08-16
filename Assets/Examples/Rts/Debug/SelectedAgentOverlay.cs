using GridNav;
using HCore;
using Rts;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Draws what the agent you clicked on is trying to do, and whether the grid lets it.
    ///
    /// Two different things are drawn, and telling them apart is the whole point:
    ///
    /// **The plan** (cyan) is the agent's <see cref="TaskStep"/> buffer - the cells it means to visit, in
    /// order. It says what the economy asked of it.
    ///
    /// **The route** (green) is the walk itself, read out of the destination's flow field one cell at a time
    /// exactly as <c>PathFollowSystem</c> reads it. It says what the grid will actually let the agent do, so
    /// it is the answer to "why is it standing still": a route that stops dead at the agent's feet is a
    /// destination with no way in - usually a one-way road painted across the last step - and it is drawn red
    /// rather than green so there is nothing to interpret.
    ///
    /// A route drawn from the field rather than from a path the agent stores is not a convenience. Agents have
    /// no stored path (§4): they read a shared field per cell. Drawing anything else would be drawing a
    /// different algorithm's answer and calling it the agent's.
    ///
    /// Selection arrives through the <see cref="EventBus"/>, so this knows nothing about the mouse and the
    /// tool controller knows nothing about gizmos.
    /// </summary>
    public class SelectedAgentOverlay : MonoBehaviour, ISelectionHandler
    {
        /// <summary>Cells the route walk will follow before giving up. A field window is 128 across.</summary>
        private const int MAX_ROUTE_CELLS = 400;

        [SerializeField, Tooltip("The task steps: the cells the agent means to visit, in order.")]
        private bool _drawPlan = true;

        [SerializeField, Tooltip("The walk the flow field actually hands out, cell by cell.")]
        private bool _drawRoute = true;

        [SerializeField, Tooltip("For a queueing agent, the ring around its destination it may not cross.")]
        private bool _drawHold = true;

        private RtsSelection _selection;

        public void OnSelectionChanged(RtsSelection selection) => _selection = selection;

        private void OnEnable() => EventBus.RegisterHandler<ISelectionHandler>(this);

        private void OnDisable()
        {
            EventBus.UnregisterHandler<ISelectionHandler>(this);
            _selection = RtsSelection.Nothing;
        }

        private void OnDrawGizmos()
        {
            if (_selection.Kind != SelectionKind.Agent || !TryGetWorld(out EntityManager entities))
            {
                return;
            }

            Entity agent = _selection.Entity;
            if (!entities.Exists(agent) || !entities.HasComponent<AgentMove>(agent))
            {
                return;
            }

            var move = entities.GetComponentData<AgentMove>(agent);

            // Inside a building it has no position on the map worth drawing a route from - it is not on the
            // map at all - so the plan is the only honest thing to show.
            bool onMap = !entities.IsComponentEnabled<InsideBuilding>(agent);

            if (_drawPlan && entities.HasBuffer<TaskStep>(agent))
            {
                DrawPlan(move.Position, entities.GetBuffer<TaskStep>(agent), onMap);
            }

            if (!onMap || !entities.IsComponentEnabled<PathFollow>(agent))
            {
                return;
            }

            var path = entities.GetComponentData<PathFollow>(agent);

            if (_drawHold && path.Holding)
            {
                Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.8f);
                Gizmos.DrawWireSphere(Point(path.GoalCell), path.HoldDistance);
            }

            if (_drawRoute && TryGetNavigation(entities, out GridMap map, out FlowFieldCache fields))
            {
                DrawRoute(map, fields, move.Position, path);
            }
        }

        /// <summary>
        /// The task, as a chain of the cells it names. Steps with no cell of their own - a pickup, a shift at
        /// a bench - happen wherever the step before them ended, so they add a marker there rather than a leg.
        /// </summary>
        private static void DrawPlan(float2 position, in DynamicBuffer<TaskStep> steps, bool onMap)
        {
            Vector3 previous = SimToWorld.Position(position);

            for (int i = 0; i < steps.Length; i++)
            {
                TaskStep step = steps[i];

                // The head is what the agent is doing now, so it is drawn brightest; the rest fades back.
                float weight = 1f - i / (float)math.max(steps.Length, 1) * 0.6f;
                Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f + 0.5f * weight);

                if (step.Kind == TaskStepKind.Interact)
                {
                    Gizmos.DrawWireSphere(previous, 0.35f);
                    continue;
                }

                Vector3 cell = Point(step.Cell);

                if (onMap || i > 0)
                {
                    Gizmos.DrawLine(previous, cell);
                }

                Gizmos.DrawWireCube(cell, SimToWorld.Direction(new float2(0.5f, 0.5f)));
                previous = cell;
            }
        }

        /// <summary>
        /// Walks the field the same way the agent does - one cell downhill at a time - and draws where that
        /// takes it. Nothing here interprets the grid; it only reports what path following will be handed.
        /// </summary>
        private static void DrawRoute(in GridMap map, in FlowFieldCache fields, float2 position, in PathFollow path)
        {
            int2 cell = GridCoords.CellOf(position);
            int2 destination = path.WaypointCell;

            // No field yet is not the same as no route - it is one frame of latency (§13.2 invariant 3) - but
            // an agent that stays like this is one whose field never gets built, which is worth seeing.
            if (!fields.TryGetSlot(destination, out int slot))
            {
                DrawNoRoute(position, destination);
                return;
            }

            if (cell.Equals(destination))
            {
                return; // standing on it: there is no route left to draw
            }

            // No way in from where the agent stands. This is the one that a one-way road produces.
            if (fields.IntegrationAt(slot, cell) == FlowField.UNREACHABLE)
            {
                DrawNoRoute(position, destination);
                return;
            }

            Gizmos.color = new Color(0.3f, 1f, 0.45f, 0.9f);
            Vector3 previous = SimToWorld.Position(position);

            for (int i = 0; i < MAX_ROUTE_CELLS && !cell.Equals(destination); i++)
            {
                // The field runs out part way along - a dead end inside the window.
                if (!fields.TryGetDirection(slot, cell, out Direction step))
                {
                    DrawNoRoute(GridCoords.CellCenter(cell), destination);
                    return;
                }

                cell += DirectionUtils.Offset(step);

                Vector3 next = Point(cell);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }

            Gizmos.DrawWireCube(Point(destination), SimToWorld.Direction(new float2(0.8f, 0.8f)));
        }

        /// <summary>
        /// A red cross where the walk gives out and a red box on the destination. Whichever of the two the
        /// cross is drawn at is the diagnosis: at the agent's feet means there is no way in from where it
        /// stands, further along means the field runs out part way.
        /// </summary>
        private static void DrawNoRoute(float2 from, int2 destination)
        {
            Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.9f);

            Vector3 point = SimToWorld.Position(from);
            Vector3 across = SimToWorld.Direction(new float2(0.4f, 0.4f));
            Vector3 back = SimToWorld.Direction(new float2(0.4f, -0.4f));

            Gizmos.DrawLine(point - across, point + across);
            Gizmos.DrawLine(point - back, point + back);

            Vector3 goal = Point(destination);
            Gizmos.DrawLine(point, goal);
            Gizmos.DrawWireCube(goal, SimToWorld.Direction(new float2(0.9f, 0.9f)));
        }

        private static Vector3 Point(int2 cell) => SimToWorld.Position(GridCoords.CellCenter(cell));

        private static bool TryGetWorld(out EntityManager entities)
        {
            entities = default;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return false;
            }

            entities = world.EntityManager;
            return true;
        }

        private static bool TryGetNavigation(EntityManager entities, out GridMap map, out FlowFieldCache fields)
        {
            using EntityQuery grids = entities.CreateEntityQuery(ComponentType.ReadOnly<GridWorld>());
            using EntityQuery caches = entities.CreateEntityQuery(ComponentType.ReadOnly<FlowFieldCache>());

            map = grids.TryGetSingleton(out GridWorld grid) ? grid.Map : default;
            fields = caches.TryGetSingleton(out FlowFieldCache cache) ? cache : default;

            return map.IsCreated && fields.IsCreated;
        }
    }
}
