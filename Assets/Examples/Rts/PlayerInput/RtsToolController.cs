using System.Collections.Generic;
using GridNav;
using HCore;
using Rts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    public enum RtsTool
    {
        /// <summary>Click anything to see what it is doing.</summary>
        Inspect,

        Build,
        Demolish,
        Road,
        SpawnAgent,
    }

    /// <summary>
    /// All world mouse input for the RTS example, in one place.
    ///
    /// One controller rather than a script per tool, because only one of them may have the mouse at a time -
    /// several MonoBehaviours each reading <c>Input</c> would all act on the same click. The panel sets
    /// <see cref="Tool"/>; everything here is about turning a click into a cell and then into the one action
    /// that tool means.
    ///
    /// Nothing here writes the grid directly. Roads enqueue edits and <c>GridApplySystem</c> applies them
    /// (§13.2 invariant 1); buildings and agents are entities, and their systems pick them up.
    /// </summary>
    public class RtsToolController : MonoBehaviour
    {
        [SerializeField, Tooltip("Falls back to the main camera.")]
        private Camera _camera;

        [SerializeField, Min(1), Tooltip("Agents spawned per click of the spawn tool.")]
        private int _spawnCount = 5;

        [SerializeField, Min(0f), Tooltip("How near a click has to be to an agent to select it, in cells.")]
        private float _agentPickRadius = 0.6f;

        private readonly List<GridEdit> _edits = new();
        private readonly HashSet<int2> _paintedThisDrag = new();

        private Camera _resolved;
        private EntityManager _entities;
        private bool _worldReady;

        private uint _spawnSeed = 1;
        private int2 _hoverCell;
        private int2 _lastPaintCell;
        private int2 _firstPaintCell;
        private bool _hovering;
        private bool _painting;

        /// <summary>The leading cell of a one-way drag is waiting to be told which way the drag went.</summary>
        private bool _firstNeedsDirection;

        public RtsTool Tool { get; set; } = RtsTool.Inspect;

        public BuildingKind BuildKind { get; set; } = BuildingKind.Farm;

        /// <summary>
        /// Which way round the next building goes. Rotated with R.
        ///
        /// It exists because of bridges and is useful to everything: a one-way crossing that could only ever
        /// run east would be a feature the map has to be built around. `BuildingPlacement.Rotation` and
        /// `RotationUtils` were already there and already tested - all that was missing was a key.
        /// </summary>
        public GridRotation BuildRotation { get; private set; } = GridRotation.None;

        public RoadBrushMode RoadMode { get; set; } = RoadBrushMode.TwoWay;

        /// <summary>Whether the cell under the cursor would accept the building currently selected.</summary>
        public bool CanPlaceHere { get; private set; }

        private void OnEnable()
        {
            _resolved = _camera != null ? _camera : Camera.main;

            World world = World.DefaultGameObjectInjectionWorld;
            _worldReady = world is { IsCreated: true };
            if (_worldReady)
            {
                _entities = world.EntityManager;
            }
        }

        private void Update()
        {
            _hovering = false;

            if (!_worldReady || _resolved == null || !TryGetGrid(out GridWorld grid))
            {
                return;
            }

            _hoverCell = CellUnderCursor();
            _hovering = true;

            if (Tool == RtsTool.Build && Input.GetKeyDown(KeyCode.R))
            {
                BuildRotation = (GridRotation)(((int)BuildRotation + 1) & 3);
            }

            CanPlaceHere = Tool == RtsTool.Build
                           && RtsConstruction.CanPlace(
                               grid.Map, BuildingCatalog.Of(BuildKind), _hoverCell, BuildRotation);

            if (EventBus.InvokeWithResult<IPointerOverUiQuery, bool>(q => q.IsPointerOverUi(), false))
            {
                _painting = false;
                return;
            }

            if (Tool == RtsTool.Road)
            {
                UpdateRoadBrush(grid);
                return;
            }

            _painting = false;

            if (Input.GetMouseButtonDown(0))
            {
                Act(grid, _hoverCell);
            }
        }

        private void Act(in GridWorld grid, int2 cell)
        {
            switch (Tool)
            {
                case RtsTool.Build:
                    Build(grid, cell);
                    break;

                case RtsTool.Demolish:
                    Demolish(cell);
                    break;

                case RtsTool.SpawnAgent:
                    Spawn(grid, cell);
                    break;

                default:
                    Select(cell);
                    break;
            }
        }

        // ---- tools -------------------------------------------------------------------------------------

        private void Build(in GridWorld grid, int2 cell)
        {
            BuildingBlueprint blueprint = BuildingCatalog.Of(BuildKind);
            if (!RtsConstruction.CanPlace(grid.Map, blueprint, cell, BuildRotation))
            {
                return;
            }

            Entity building = RtsConstruction.Place(_entities, blueprint, cell, BuildRotation);
            EventBus.Invoke<ISelectionHandler>(h => h.OnSelectionChanged(RtsSelection.Of(building, cell)));
        }

        /// <summary>
        /// Takes away whatever is on the cell: a building, or the trees and rocks that were the one thing on
        /// the map nothing could remove.
        ///
        /// A tree blocks a cell exactly as a building does - it is 255 of cost and nothing may walk through it
        /// (§3, §5) - but it has no view of its own, so on screen it is a red square with no explanation, and
        /// the demolish tool only ever looked for buildings. Clearing land is what the cell -> object map is
        /// there for.
        /// </summary>
        private void Demolish(int2 cell)
        {
            if (TryFindBuildingAt(cell, out Entity building))
            {
                RtsConstruction.Demolish(_entities, building);
                EventBus.Invoke<ISelectionHandler>(h => h.OnSelectionChanged(RtsSelection.Nothing));
                return;
            }

            if (ClearObjectsAt(cell))
            {
                EventBus.Invoke<ISelectionHandler>(h => h.OnSelectionChanged(RtsSelection.Ground(cell)));
            }
        }

        /// <summary>
        /// Destroys every world object standing on a cell. The cost and the map entry are given back by
        /// <c>CellObjectRegistrationSystem</c> on the next grid phase, from its cleanup component - so this
        /// really is just "destroy the entity", exactly as demolishing a building is.
        /// </summary>
        private bool ClearObjectsAt(int2 cell)
        {
            using EntityQuery query = _entities.CreateEntityQuery(ComponentType.ReadOnly<CellObjectMap>());
            if (!query.TryGetSingleton(out CellObjectMap objects) || !objects.IsCreated)
            {
                return false;
            }

            // Collected first: destroying an entity while walking the map that lists it is asking the
            // enumerator to skip the rest of the cell.
            var doomed = new NativeList<Entity>(4, Allocator.Temp);
            foreach (Entity standing in objects.GetObjectsAt(cell))
            {
                doomed.Add(standing);
            }

            foreach (Entity standing in doomed)
            {
                if (_entities.Exists(standing))
                {
                    _entities.DestroyEntity(standing);
                }
            }

            bool cleared = !doomed.IsEmpty;
            doomed.Dispose();
            return cleared;
        }

        private void Spawn(in GridWorld grid, int2 cell)
        {
            if (!grid.Map.IsPassable(cell))
            {
                return;
            }

            Entity request = _entities.CreateEntity(typeof(AgentSpawn));
            _entities.SetComponentData(request, new AgentSpawn
            {
                Count = _spawnCount,
                Center = GridCoords.CellCenter(cell),
                Size = new float2(4f, 4f),
                MaxSpeed = 3f,
                Radius = 0.42f,
                CarryCapacity = 10,
                Idle = true,

                // A different scatter each click, without a static counter and without a clock the
                // simulation would rather not depend on.
                Seed = ++_spawnSeed,
            });
        }

        private void Select(int2 cell)
        {
            if (TryFindAgentAt(cell, out Entity agent))
            {
                EventBus.Invoke<ISelectionHandler>(h => h.OnSelectionChanged(RtsSelection.Agent(agent, cell)));
                return;
            }

            if (TryFindBuildingAt(cell, out Entity building))
            {
                EventBus.Invoke<ISelectionHandler>(h => h.OnSelectionChanged(RtsSelection.Of(building, cell)));
                return;
            }

            EventBus.Invoke<ISelectionHandler>(h => h.OnSelectionChanged(RtsSelection.Ground(cell)));
        }

        /// <summary>
        /// Roads are painted by dragging, so the direction of a one-way road is the direction you drew it.
        /// Cells already done this drag are skipped, because the grid is written a frame later and reading it
        /// back mid-drag would discount the same cell twice.
        /// </summary>
        private void UpdateRoadBrush(in GridWorld grid)
        {
            if (Input.GetMouseButtonDown(0))
            {
                _painting = true;
                _paintedThisDrag.Clear();
                _lastPaintCell = _hoverCell;
                _firstPaintCell = _hoverCell;

                // The first cell of a drag has no previous cell to take a direction from, so it is laid
                // two-way and its mask corrected below the moment the drag says which way it is going. A
                // hardcoded direction here is what used to put the leading cell of every one-way road the
                // wrong way round; leaving it two-way is also the right answer for a single click, which
                // never reveals a direction at all.
                RoadBrushMode firstMode = RoadMode == RoadBrushMode.OneWay ? RoadBrushMode.TwoWay : RoadMode;
                _firstNeedsDirection = PaintRoad(grid, _hoverCell, firstMode, default)
                                    && RoadMode == RoadBrushMode.OneWay;
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                _painting = false;
                _firstNeedsDirection = false;
                return;
            }

            if (!_painting || _hoverCell.Equals(_lastPaintCell))
            {
                return;
            }

            if (TryDirectionBetween(_lastPaintCell, _hoverCell, out Direction direction))
            {
                if (_firstNeedsDirection)
                {
                    // The exits only, and only because SetExits is an absolute write rather than a delta:
                    // painting the cell again would discount it twice, since its first paint is still queued
                    // and the cell does not read as a road yet.
                    grid.Edits.Enqueue(
                        GridEdit.SetExits(_firstPaintCell, RoadBrush.ExitsFor(RoadMode, direction))
                    );

                    _firstNeedsDirection = false;
                }

                PaintRoad(grid, _lastPaintCell, RoadMode, direction);
                PaintRoad(grid, _hoverCell, RoadMode, direction);
            }

            _lastPaintCell = _hoverCell;
        }

        /// <summary>Whether the cell was actually laid, which a cell already done or unpaintable was not.</summary>
        private bool PaintRoad(in GridWorld grid, int2 cell, RoadBrushMode mode, Direction direction)
        {
            if (!_paintedThisDrag.Add(cell))
            {
                return false;
            }

            CellData current = grid.Map.GetCell(cell);
            if (!current.IsPassable || current.Has(CellFlags.Building))
            {
                return false;
            }

            _edits.Clear();
            RoadBrush.Paint(_edits, cell, current, mode, direction);

            foreach (GridEdit edit in _edits)
            {
                grid.Edits.Enqueue(edit);
            }

            return true;
        }

        // ---- picking -----------------------------------------------------------------------------------

        private bool TryFindBuildingAt(int2 cell, out Entity building)
        {
            using EntityQuery query = _entities.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingFootprintCell>());

            using NativeArray<Entity> candidates = query.ToEntityArray(Allocator.Temp);
            foreach (Entity candidate in candidates)
            {
                foreach (BuildingFootprintCell occupied in _entities.GetBuffer<BuildingFootprintCell>(candidate))
                {
                    if (occupied.Cell.Equals(cell))
                    {
                        building = candidate;
                        return true;
                    }
                }
            }

            building = Entity.Null;
            return false;
        }

        private bool TryFindAgentAt(int2 cell, out Entity agent)
        {
            float2 point = GridCoords.CellCenter(cell);
            float best = _agentPickRadius * _agentPickRadius;
            agent = Entity.Null;

            using EntityQuery query = _entities.CreateEntityQuery(ComponentType.ReadOnly<AgentMove>());
            using NativeArray<AgentMove> agents = query.ToComponentDataArray<AgentMove>(Allocator.Temp);

            foreach (AgentMove move in agents)
            {
                float distance = math.distancesq(move.Position, point);
                if (distance < best)
                {
                    best = distance;
                    agent = move.Entity;
                }
            }

            return agent != Entity.Null;
        }

        // ---- cursor ------------------------------------------------------------------------------------

        private int2 CellUnderCursor()
        {
            Vector3 screen = Input.mousePosition;

            // Distance from the camera to the simulation plane, so this works for an orthographic 2D camera
            // and a perspective one looking at the same plane.
            screen.z = math.abs(_resolved.transform.position.z);

            return GridCoords.CellOf(SimToWorld.ToSim(_resolved.ScreenToWorldPoint(screen)));
        }

        /// <summary>The dominant axis, so a fast drag that skips cells still paints something sensible.</summary>
        private static bool TryDirectionBetween(int2 from, int2 to, out Direction direction)
        {
            int2 delta = to - from;
            if (delta.x == 0 && delta.y == 0)
            {
                direction = default;
                return false;
            }

            direction = math.abs(delta.x) >= math.abs(delta.y)
                ? delta.x > 0 ? Direction.East : Direction.West
                : delta.y > 0 ? Direction.North : Direction.South;

            return true;
        }

        private bool TryGetGrid(out GridWorld grid)
        {
            using EntityQuery query = _entities.CreateEntityQuery(ComponentType.ReadOnly<GridWorld>());
            return query.TryGetSingleton(out grid) && grid.Map.IsCreated;
        }

        // ---- cursor gizmo ------------------------------------------------------------------------------

        private void OnDrawGizmos()
        {
            if (!_hovering)
            {
                return;
            }

            // The cursor gizmo follows the cursor, so it is the one gizmo guaranteed to be under the panel
            // whenever the player reaches for a button.
            if (UiGizmos.Hides(SimToWorld.Position(GridCoords.CellCenter(_hoverCell))))
            {
                return;
            }

            Vector3 size = SimToWorld.Direction(new float2(1f, 1f));

            if (Tool == RtsTool.Build)
            {
                DrawBuildPreview();
                return;
            }

            Gizmos.color = Tool switch
            {
                RtsTool.Demolish => new Color(0.9f, 0.3f, 0.3f),
                RtsTool.Road => new Color(0.9f, 0.9f, 0.3f),
                RtsTool.SpawnAgent => new Color(0.3f, 0.7f, 0.9f),
                _ => new Color(0.9f, 0.9f, 0.9f),
            };

            Gizmos.DrawWireCube(SimToWorld.Position(GridCoords.CellCenter(_hoverCell)), size);
        }

        /// <summary>
        /// The building as it will actually be laid: every cell it takes, rotated, plus the doorstep.
        ///
        /// Drawn cell by cell rather than as one box, because a box is a lie about two of the three shapes here.
        /// An L or a ring is not its extent, and a bridge is emphatically not its extent - the cells under the
        /// deck are *not* being taken, and an outline over them would say they were.
        ///
        /// **The doorstep is drawn because it is the half of a placement the player cannot otherwise see.** A
        /// building whose door lands against a wall is sealed, and the rule that decides it - the south wall,
        /// turned by the rotation - is invisible until an agent fails to reach it. It is the one thing rotation
        /// is usually *for*.
        /// </summary>
        private void DrawBuildPreview()
        {
            BuildingBlueprint blueprint = BuildingCatalog.Of(BuildKind);
            int2 origin = RtsConstruction.OriginFor(blueprint, _hoverCell, BuildRotation);

            Gizmos.color = CanPlaceHere ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.9f, 0.3f, 0.3f);

            if (blueprint.IsBridge)
            {
                DrawBridgePreview(blueprint, origin);
                return;
            }

            Vector3 cell = SimToWorld.Direction(new float2(0.94f, 0.94f));

            for (int y = 0; y < blueprint.Size.y; y++)
            {
                for (int x = 0; x < blueprint.Size.x; x++)
                {
                    int2 taken = origin + RotationUtils.Rotate(new int2(x, y), BuildRotation);
                    Gizmos.DrawWireCube(SimToWorld.Position(GridCoords.CellCenter(taken)), cell);
                }
            }

            DrawDoorstep(RtsConstruction.DoorstepOf(origin, BuildRotation), origin);
        }

        /// <summary>
        /// The doorstep, and a line to it from the wall it is cut into, so which way the building faces is
        /// readable at a glance. Amber rather than the placement colour: it is not a cell being taken.
        /// </summary>
        private void DrawDoorstep(int2 doorstep, int2 origin)
        {
            Gizmos.color = new Color(0.95f, 0.8f, 0.3f, 0.9f);

            Vector3 step = SimToWorld.Position(GridCoords.CellCenter(doorstep));
            Gizmos.DrawWireCube(step, SimToWorld.Direction(new float2(0.6f, 0.6f)));
            Gizmos.DrawLine(SimToWorld.Position(GridCoords.CellCenter(origin)), step);
        }

        /// <summary>
        /// A bridge preview: a box on each pier, nothing over the gap, and a line from the mouth agents get on
        /// at to the one they are put down on. The crossing is one way, and which way is the thing that cannot
        /// be seen at all once it is built.
        /// </summary>
        private void DrawBridgePreview(in BuildingBlueprint blueprint, int2 origin)
        {
            // Asked of the same function the validation asks, so the preview cannot draw a bridge in a place
            // the placement would not put one.
            RtsConstruction.MouthsOf(blueprint, origin, BuildRotation, out int2 entry, out int2 exit);

            if (!Bridge.TryShape(entry, exit, out BridgeShape shape))
            {
                return;
            }

            Vector3 cell = SimToWorld.Direction(new float2(0.94f, 0.94f));
            Gizmos.DrawWireCube(SimToWorld.Position(GridCoords.CellCenter(shape.NearPier)), cell);
            Gizmos.DrawWireCube(SimToWorld.Position(GridCoords.CellCenter(shape.FarPier)), cell);

            Vector3 from = SimToWorld.Position(GridCoords.CellCenter(entry));
            Vector3 to = SimToWorld.Position(GridCoords.CellCenter(exit));

            Gizmos.DrawLine(from, to);
            Gizmos.DrawWireSphere(from, 0.25f);
            Gizmos.DrawWireCube(to, SimToWorld.Direction(new float2(0.6f, 0.6f)));
        }
    }
}
