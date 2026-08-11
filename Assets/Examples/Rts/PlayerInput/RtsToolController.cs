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
        private bool _hovering;
        private bool _painting;

        public RtsTool Tool { get; set; } = RtsTool.Inspect;

        public BuildingKind BuildKind { get; set; } = BuildingKind.Farm;

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
            CanPlaceHere = Tool == RtsTool.Build
                           && RtsConstruction.CanPlace(grid.Map, BuildingCatalog.Of(BuildKind), _hoverCell);

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
            if (!RtsConstruction.CanPlace(grid.Map, blueprint, cell))
            {
                return;
            }

            Entity building = RtsConstruction.Place(_entities, blueprint, cell);
            EventBus.Invoke<ISelectionHandler>(h => h.OnSelectionChanged(RtsSelection.Of(building, cell)));
        }

        private void Demolish(int2 cell)
        {
            if (!TryFindBuildingAt(cell, out Entity building))
            {
                return;
            }

            RtsConstruction.Demolish(_entities, building);
            EventBus.Invoke<ISelectionHandler>(h => h.OnSelectionChanged(RtsSelection.Nothing));
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
                Radius = 0.35f,
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
                PaintRoad(grid, _hoverCell, Direction.North);
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                _painting = false;
                return;
            }

            if (!_painting || _hoverCell.Equals(_lastPaintCell))
            {
                return;
            }

            if (TryDirectionBetween(_lastPaintCell, _hoverCell, out Direction direction))
            {
                PaintRoad(grid, _lastPaintCell, direction);
                PaintRoad(grid, _hoverCell, direction);
            }

            _lastPaintCell = _hoverCell;
        }

        private void PaintRoad(in GridWorld grid, int2 cell, Direction direction)
        {
            if (!_paintedThisDrag.Add(cell))
            {
                return;
            }

            CellData current = grid.Map.GetCell(cell);
            if (!current.IsPassable || current.Has(CellFlags.Building))
            {
                return;
            }

            _edits.Clear();
            RoadBrush.Paint(_edits, cell, current, RoadMode, direction);

            foreach (GridEdit edit in _edits)
            {
                grid.Edits.Enqueue(edit);
            }
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
                BuildingBlueprint blueprint = BuildingCatalog.Of(BuildKind);
                float2 min = GridCoords.CellMin(_hoverCell);
                float2 max = GridCoords.CellMax(_hoverCell + blueprint.Size - 1);

                Gizmos.color = CanPlaceHere ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.9f, 0.3f, 0.3f);
                Gizmos.DrawWireCube(SimToWorld.Position((min + max) * 0.5f), SimToWorld.Direction(max - min));
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
    }
}
