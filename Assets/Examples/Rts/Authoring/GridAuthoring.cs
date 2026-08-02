using GridNav;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Bakes the map size. One of these in the subscene is what brings the grid into existence.
    /// </summary>
    public class GridAuthoring : MonoBehaviour
    {
        [SerializeField] private Vector2Int _sizeInCells = new(512, 512);
        [SerializeField] private bool _centerOnOrigin = true;

        /// <summary>Rounded up to whole 32x32 chunks, so the drawn bounds match what the grid will allocate.</summary>
        public GridSettings Settings =>
            GridSettings.FromCells(new int2(_sizeInCells.x, _sizeInCells.y), _centerOnOrigin);

        private void OnDrawGizmosSelected()
        {
            GridSettings settings = Settings;
            float2 min = GridCoords.CellMin(settings.MinCell);
            float2 max = GridCoords.CellMin(settings.MinCell + settings.ChunkCount * GridMap.CHUNK_SIZE);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(SimToWorld.Position((min + max) * 0.5f), SimToWorld.Direction(max - min));
        }

        private class GridBaker : Baker<GridAuthoring>
        {
            public override void Bake(GridAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, authoring.Settings);
            }
        }
    }
}
