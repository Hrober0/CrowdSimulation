using GridNav;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Rts
{
    /// <summary>
    /// A rectangle waiting to be painted into the grid. Disabled once applied, so it is applied exactly once
    /// and re-enabling it in the inspector paints it again.
    /// </summary>
    public struct GridCostPatch : IComponentData, IEnableableComponent
    {
        public int2 MinCell;
        public int2 SizeInCells;
        public int Cost;
        public CellFlags Flags;
        public byte Exits;
    }
}
