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

        public int2 Cell => GridCoords.CellOf(SimToWorld.ToSim(transform.position));

        /// <summary>
        /// What this thing costs to walk through comes from its kind, not from a number set per instance.
        /// Two trees that price differently are two trees the player cannot tell apart, and the cost is
        /// load-bearing enough - it decides whether a cell can be sealed at all - to want one answer.
        /// </summary>
        private ushort Cost => WorldObjectCatalog.Cost(_kind);

        private void OnDrawGizmos()
        {
            Gizmos.color = Cost >= CellData.BLOCKED ? new Color(0.8f, 0.3f, 0.1f) : new Color(0.8f, 0.7f, 0.2f);
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
                    Cost = authoring.Cost,
                    Kind = authoring._kind,
                });
            }
        }
    }
}
