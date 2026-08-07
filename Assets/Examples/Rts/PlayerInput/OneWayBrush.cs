using GridNav;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Paints one-way roads (design §3, §8). Drag along a road and every cell the drag passes through is
    /// made one-way *in the direction of the drag*; right-click a cell to make it two-way again.
    ///
    /// **What painting a cell does is forbid the reverse direction, not permit only one.** A mask of "east
    /// only" would also forbid stepping off the road sideways, so agents could enter a one-way road and
    /// never leave it - the feature meant to unjam corridors would strand everyone who used one. Forbidding
    /// the reverse gives the thing the player actually wants: traffic that cannot turn back on itself, and
    /// can still get on and off.
    ///
    /// Like every other producer, this only enqueues; <c>GridApplySystem</c> does the writing (§13.2
    /// invariant 1).
    /// </summary>
    public class OneWayBrush : MonoBehaviour
    {
        [SerializeField, Tooltip("Falls back to the main camera.")]
        private Camera _camera;

        [SerializeField, Tooltip("Draw the cell under the cursor and the direction it would be painted.")]
        private bool _drawCursor = true;

        private Camera _resolved;
        private int2 _lastCell;
        private int2 _hoverCell;
        private Direction _lastDirection;
        private bool _painting;
        private bool _hovering;

        private void OnEnable() => _resolved = _camera != null ? _camera : Camera.main;

        private void OnDisable()
        {
            _painting = false;
            _hovering = false;
        }

        private void Update()
        {
            _hovering = false;

            if (_resolved == null || !TryGetGrid(out GridWorld grid))
            {
                return;
            }

            int2 cell = CellUnderCursor();
            _hoverCell = cell;
            _hovering = true;

            if (Input.GetMouseButtonDown(1))
            {
                grid.Edits.Enqueue(GridEdit.SetExits(cell, DirectionUtils.ALL_EXITS));
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                _lastCell = cell;
                _painting = true;
                return;
            }

            if (Input.GetMouseButtonUp(0))
            {
                _painting = false;
                return;
            }

            if (!_painting || cell.Equals(_lastCell))
            {
                return;
            }

            if (TryDirectionBetween(_lastCell, cell, out Direction direction))
            {
                // Both ends: the cell being left is the one the direction is really about, but painting the
                // cell being entered as well is what stops a drag from leaving its last cell two-way. A
                // corner gets painted twice and keeps the later direction, which is the way out of it.
                Paint(grid, _lastCell, direction);
                Paint(grid, cell, direction);

                _lastDirection = direction;
            }

            _lastCell = cell;
        }

        private static void Paint(in GridWorld grid, int2 cell, Direction direction) =>
            grid.Edits.Enqueue(GridEdit.SetExits(
                cell,
                DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, DirectionUtils.Opposite(direction))
            ));

        /// <summary>
        /// The dominant axis of the step, so a fast drag that skips cells still paints something sensible
        /// instead of nothing. A drag is a gesture, not a sequence of grid moves.
        /// </summary>
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

        private int2 CellUnderCursor()
        {
            Vector3 screen = Input.mousePosition;

            // Distance from the camera to the simulation plane, so the same code works for an orthographic
            // 2D camera and a perspective one looking at the same plane.
            screen.z = math.abs(_resolved.transform.position.z);

            return GridCoords.CellOf(SimToWorld.ToSim(_resolved.ScreenToWorldPoint(screen)));
        }

        private static bool TryGetGrid(out GridWorld grid)
        {
            grid = default;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world is not { IsCreated: true })
            {
                return false;
            }

            using EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
                                      .WithAll<GridWorld>()
                                      .Build(world.EntityManager);

            return query.TryGetSingleton(out grid) && grid.Map.IsCreated;
        }

        private void OnDrawGizmos()
        {
            if (!_drawCursor || !_hovering)
            {
                return;
            }

            Vector3 centre = SimToWorld.Position(GridCoords.CellCenter(_hoverCell));
            Vector3 size = SimToWorld.Direction(new float2(1f, 1f));

            Gizmos.color = _painting ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.9f, 0.9f, 0.2f);
            Gizmos.DrawWireCube(centre, size);

            if (!_painting)
            {
                return;
            }

            float2 offset = DirectionUtils.Offset(_lastDirection);
            Gizmos.DrawLine(centre, SimToWorld.Position(GridCoords.CellCenter(_hoverCell) + offset * 0.45f));
        }
    }
}
