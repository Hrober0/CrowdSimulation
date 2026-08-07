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
        [SerializeField] private bool _drawBounds = true;
        [SerializeField] private bool _drawChunks = true;
        [SerializeField] private bool _drawCost = true;
        [SerializeField] private bool _drawOneWay = true;

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

            if (_drawChunks)
            {
                DrawChunks(map, min, max);
            }

            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    var cell = new int2(x, y);
                    CellData data = map.GetCell(cell);

                    if (_drawCost)
                    {
                        DrawCost(cell, data);
                    }

                    if (_drawOneWay && data.IsOneWay)
                    {
                        DrawExits(cell, data);
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

        private static void DrawChunks(GridMap map, int2 min, int2 max)
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
                    Gizmos.DrawWireCube(
                        SimToWorld.Position((chunkMin + chunkMax) * 0.5f),
                        SimToWorld.Direction(chunkMax - chunkMin)
                    );
                }
            }
        }

        private void DrawCost(int2 cell, CellData data)
        {
            if (data.CostSum == 0)
            {
                return;
            }

            Vector3 center = SimToWorld.Position(GridCoords.CellCenter(cell));
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
        /// Draws what the cell will *not* let an agent do. Forbidden rather than allowed, because a one-way
        /// road cell forbids exactly one direction and permits the other three (see <see cref="OneWayBrush"/>):
        /// drawing the permitted set would put three arrows on every road cell and leave the reader to work
        /// out which one is missing.
        /// </summary>
        private static void DrawExits(int2 cell, CellData data)
        {
            float2 center = GridCoords.CellCenter(cell);

            Gizmos.color = Color.magenta;
            for (int i = 0; i < DirectionUtils.DIRECTION_COUNT; i++)
            {
                var direction = (Direction)i;
                if (data.CanExit(direction))
                {
                    continue;
                }

                float2 offset = DirectionUtils.Offset(direction);
                float2 tip = center + offset * 0.45f;
                float2 side = new float2(-offset.y, offset.x) * 0.12f;

                Gizmos.DrawLine(SimToWorld.Position(center), SimToWorld.Position(tip));
                Gizmos.DrawLine(SimToWorld.Position(tip), SimToWorld.Position(tip - offset * 0.15f + side));
                Gizmos.DrawLine(SimToWorld.Position(tip), SimToWorld.Position(tip - offset * 0.15f - side));
            }
        }
    }
}
