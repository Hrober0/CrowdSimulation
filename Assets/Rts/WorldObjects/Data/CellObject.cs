using GridNav;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// A thing standing on one cell: a tree, a rock, a prop (design §5). Individually addressable, so it can
    /// be harvested, clicked and damaged like any other entity.
    ///
    /// Many objects may share a cell - the grid sums their costs and the cell map holds all of them.
    /// The cell is fixed for the object's lifetime; a thing that moves is an agent, not a cell object.
    /// </summary>
    public struct CellObject : IComponentData
    {
        public int2 Cell;

        /// <summary>Added to the cell's cost sum. <see cref="CellData.BLOCKED"/> blocks the cell on its own.</summary>
        public ushort Cost;

        public ObjectKind Kind;
    }
}
