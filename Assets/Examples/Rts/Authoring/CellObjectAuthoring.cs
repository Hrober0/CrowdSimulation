using GridNav;
using Rts;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// A tree, rock or prop. The cell it occupies comes from where the object sits in the scene, so placing
    /// one is dragging it around.
    /// </summary>
    public class CellObjectAuthoring : MonoBehaviour
    {
        [SerializeField] private ObjectKind _kind = ObjectKind.Tree;

        [SerializeField, Range(0, CellData.BLOCKED)]
        [Tooltip("Added to the cell's cost. 255 blocks the cell on its own; ~120 makes two of them block it. " +
                 "Above 255 would be indistinguishable from 255 for one object, so that is the top of the range.")]
        private int _cost = CellData.BLOCKED;

        public int2 Cell => GridCoords.CellOf(SimToWorld.ToSim(transform.position));

        private void OnDrawGizmos()
        {
            Gizmos.color = _cost >= CellData.BLOCKED ? new Color(0.8f, 0.3f, 0.1f) : new Color(0.8f, 0.7f, 0.2f);
            Gizmos.DrawWireCube(SimToWorld.Position(GridCoords.CellCenter(Cell)), SimToWorld.Direction(new float2(1f, 1f)));
        }

        private class CellObjectBaker : Baker<CellObjectAuthoring>
        {
            public override void Bake(CellObjectAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new CellObject
                {
                    Cell = authoring.Cell,
                    Cost = (ushort)math.clamp(authoring._cost, 0, ushort.MaxValue),
                    Kind = authoring._kind,
                });
            }
        }
    }
}
