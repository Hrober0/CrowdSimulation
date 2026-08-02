using GridNav;
using Rts;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// A building with an arbitrary footprint. Offsets are authored unrotated, around the object's own cell;
    /// leaving gaps or making a ring is fine, nothing downstream assumes a rectangle.
    /// </summary>
    public class BuildingAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Footprint cells as offsets from this object's cell, before rotation.")]
        private Vector2Int[] _footprintOffsets =
        {
            new(0, 0), new(1, 0),
            new(0, 1), new(1, 1),
        };

        [SerializeField] private GridRotation _rotation;

        public int2 OriginCell => GridCoords.CellOf(SimToWorld.ToSim(transform.position));

        private void OnDrawGizmos()
        {
            int2 origin = OriginCell;
            Vector3 cellSize = SimToWorld.Direction(new float2(1f, 1f));

            Gizmos.color = new Color(0.2f, 0.5f, 0.9f);
            foreach (Vector2Int offset in _footprintOffsets)
            {
                int2 cell = origin + RotationUtils.Rotate(new int2(offset.x, offset.y), _rotation);
                Gizmos.DrawWireCube(SimToWorld.Position(GridCoords.CellCenter(cell)), cellSize);
            }
        }

        private class BuildingBaker : Baker<BuildingAuthoring>
        {
            public override void Bake(BuildingAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new BuildingPlacement
                {
                    OriginCell = authoring.OriginCell,
                    Rotation = authoring._rotation,
                });

                DynamicBuffer<BuildingFootprintOffset> footprint = AddBuffer<BuildingFootprintOffset>(entity);
                foreach (Vector2Int offset in authoring._footprintOffsets)
                {
                    footprint.Add(new BuildingFootprintOffset { Offset = new int2(offset.x, offset.y) });
                }
            }
        }
    }
}
