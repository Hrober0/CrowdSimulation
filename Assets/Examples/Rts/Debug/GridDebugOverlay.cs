using GridNav;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Draws the live grid around this object: bounds, chunk borders, cost heat, blocked cells and one-way
    /// exits. Read-only, and only inside a window of cells - a 512x512 map is 262k cells and gizmos are not
    /// free (design §3, §13.3 #23).
    /// </summary>
    public class GridDebugOverlay : MonoBehaviour
    {
        /// <summary>How far from the centre the blocked-edge wedge stands, in cells. Just inside the edge.</summary>
        private const float BLOCKED_EDGE_INSET = 0.45f;

        /// <summary>Half the width of the wedge's base, in cells. Short of the full edge, deliberately.</summary>
        private const float BLOCKED_EDGE_HALF_WIDTH = 0.32f;

        private const int BLOCKED_EDGE_HATCHING = 4;

        [SerializeField] private bool _drawBounds = true;
        [SerializeField] private bool _drawChunks = true;
        [SerializeField] private bool _drawCost = true;
        [SerializeField, Tooltip("Marks the edges a one-way cell will not let an agent cross.")]
        private bool _drawOneWay = true;

        [SerializeField, Min(1), Tooltip("Half-size, in cells, of the window drawn around this object.")]
        private int _windowRadius = 32;

        [SerializeField, Min(1), Tooltip("Cost that maps to the hottest colour. Anything at or above BLOCKED is drawn solid.")]
        private int _hottestCost = 32;

        private void OnDrawGizmos()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            using EntityQuery query = new EntityQueryBuilder(Allocator.Temp)
                                      .WithAll<GridWorld>()
                                      .Build(world.EntityManager);

            if (!query.TryGetSingleton(out GridWorld gridWorld) || !gridWorld.Map.IsCreated)
            {
                return;
            }

            GridMap map = gridWorld.Map;

            if (_drawBounds)
            {
                DrawBounds(map);
            }

            int2 center = GridCoords.CellOf(SimToWorld.ToSim(transform.position));
            int2 min = math.max(center - _windowRadius, map.MinCell);
            int2 max = math.min(center + _windowRadius, map.MaxCell);

            // Asked once for the whole draw - see UiGizmos.ClearRect.
            Rect clear = UiGizmos.ClearRect();

            if (_drawChunks)
            {
                DrawChunks(map, min, max, clear);
            }

            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    var cell = new int2(x, y);
                    Vector3 cellCenter = SimToWorld.Position(GridCoords.CellCenter(cell));
                    if (UiGizmos.Hides(clear, cellCenter))
                    {
                        continue;
                    }

                    CellData data = map.GetCell(cell);

                    if (_drawCost)
                    {
                        DrawCost(cellCenter, data);
                    }

                    if (_drawOneWay && data.IsOneWay)
                    {
                        DrawBlockedExits(cell, data);
                    }
                }
            }
        }

        private static void DrawBounds(GridMap map)
        {
            float2 min = GridCoords.CellMin(map.MinCell);
            float2 max = GridCoords.CellMax(map.MaxCell);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(SimToWorld.Position((min + max) * 0.5f), SimToWorld.Direction(max - min));
        }

        private static void DrawChunks(GridMap map, int2 min, int2 max, in Rect clear)
        {
            int2 firstChunk = map.ChunkCoordOf(min);
            int2 lastChunk = map.ChunkCoordOf(max);

            Gizmos.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            for (int y = firstChunk.y; y <= lastChunk.y; y++)
            {
                for (int x = firstChunk.x; x <= lastChunk.x; x++)
                {
                    float2 chunkMin = GridCoords.CellMin(map.ChunkMinCell(new int2(x, y)));
                    float2 chunkMax = chunkMin + GridMap.CHUNK_SIZE;
                    Vector3 chunkCenter = SimToWorld.Position((chunkMin + chunkMax) * 0.5f);
                    if (UiGizmos.Hides(clear, chunkCenter))
                    {
                        continue;
                    }

                    Gizmos.DrawWireCube(chunkCenter, SimToWorld.Direction(chunkMax - chunkMin));
                }
            }
        }

        private void DrawCost(Vector3 center, CellData data)
        {
            if (data.CostSum == 0)
            {
                return;
            }

            if (!data.IsPassable)
            {
                Gizmos.color = new Color(0.8f, 0.1f, 0.1f, 0.5f);
                Gizmos.DrawCube(center, SimToWorld.Direction(new float2(0.9f, 0.9f)));
                return;
            }

            float heat = math.saturate(data.CostSum / (float)_hottestCost);
            Gizmos.color = new Color(1f, 1f - heat, 0f, 0.15f + 0.35f * heat);
            Gizmos.DrawCube(center, SimToWorld.Direction(new float2(0.9f, 0.9f)));
        }

        /// <summary>
        /// Marks the cell *edge* an agent may not cross: a wedge standing on that edge with its point at the
        /// cell centre.
        ///
        /// An arrow was the obvious thing and the wrong one, both ways round. Drawing the permitted directions
        /// puts three arrows on every one-way cell and leaves the reader to spot the missing one. Drawing the
        /// forbidden direction puts one arrow on the cell pointing *against* the traffic, which reads as the
        /// road running the other way - and did. A shape with no direction in it cannot be read backwards: the
        /// closed side is the side with the wedge on it.
        /// </summary>
        private static void DrawBlockedExits(int2 cell, CellData data)
        {
            float2 center = GridCoords.CellCenter(cell);

            Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.9f);
            for (int i = 0; i < DirectionUtils.DIRECTION_COUNT; i++)
            {
                var direction = (Direction)i;
                if (data.CanExit(direction))
                {
                    continue;
                }

                float2 offset = DirectionUtils.Offset(direction);

                // Just inside the edge, so the two cells either side of a closed seam do not draw over one
                // another and you can tell which of them is the one refusing to let you out.
                float2 edge = center + offset * BLOCKED_EDGE_INSET;
                float2 along = new float2(-offset.y, offset.x) * BLOCKED_EDGE_HALF_WIDTH;

                float2 left = edge + along;
                float2 right = edge - along;

                Gizmos.DrawLine(SimToWorld.Position(left), SimToWorld.Position(right));
                Gizmos.DrawLine(SimToWorld.Position(right), SimToWorld.Position(center));
                Gizmos.DrawLine(SimToWorld.Position(center), SimToWorld.Position(left));

                // Gizmos has no filled triangle. Hatching it with a few lines shrinking towards the point
                // reads as solid at any zoom worth looking at, and costs three calls rather than a mesh.
                for (int line = 1; line < BLOCKED_EDGE_HATCHING; line++)
                {
                    float t = line / (float)BLOCKED_EDGE_HATCHING;
                    Gizmos.DrawLine(
                        SimToWorld.Position(math.lerp(left, center, t)),
                        SimToWorld.Position(math.lerp(right, center, t))
                    );
                }
            }
        }
    }
}
