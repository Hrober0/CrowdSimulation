using GridNav;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Paints one rectangle of cost, flags and exits into the grid on the first frame. A stand-in for authored
    /// terrain and a way to exercise the queue -> GridApplySystem path before world objects exist.
    /// </summary>
    public class GridCostPatchAuthoring : MonoBehaviour
    {
        [SerializeField] private Vector2Int _minCell;
        [SerializeField] private Vector2Int _sizeInCells = new(8, 8);

        [SerializeField, Tooltip("Added to the cost of every cell in the rectangle. 255 blocks a cell on its own.")]
        private int _cost = CellData.BLOCKED;

        [SerializeField] private CellFlags _flags;

        [SerializeField, Tooltip("Allowed exits, N/E/S/W bits. Leave all four on unless painting a one-way road.")]
        private bool _north = true, _east = true, _south = true, _west = true;

        private GridCostPatch Patch => new()
        {
            MinCell = new int2(_minCell.x, _minCell.y),
            SizeInCells = math.max(new int2(_sizeInCells.x, _sizeInCells.y), new int2(1, 1)),
            Cost = _cost,
            Flags = _flags,
            Exits = Exits,
        };

        private byte Exits
        {
            get
            {
                byte exits = DirectionUtils.NO_EXITS;
                if (_north)
                {
                    exits = DirectionUtils.Allow(exits, Direction.North);
                }

                if (_east)
                {
                    exits = DirectionUtils.Allow(exits, Direction.East);
                }

                if (_south)
                {
                    exits = DirectionUtils.Allow(exits, Direction.South);
                }

                if (_west)
                {
                    exits = DirectionUtils.Allow(exits, Direction.West);
                }

                return exits;
            }
        }

        private void OnDrawGizmosSelected()
        {
            GridCostPatch patch = Patch;
            float2 min = GridCoords.CellMin(patch.MinCell);
            float2 max = GridCoords.CellMin(patch.MinCell + patch.SizeInCells);

            Gizmos.color = patch.Cost >= CellData.BLOCKED ? Color.red : Color.yellow;
            Gizmos.DrawWireCube(SimToWorld.Position((min + max) * 0.5f), SimToWorld.Direction(max - min));
        }

        private class GridCostPatchBaker : Baker<GridCostPatchAuthoring>
        {
            public override void Bake(GridCostPatchAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, authoring.Patch);
            }
        }
    }
}
