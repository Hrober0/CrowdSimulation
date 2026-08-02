using GridNav;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Where a building stands. Its shape is the <see cref="BuildingFootprintOffset"/> buffer, rotated by
    /// <see cref="Rotation"/> around <see cref="OriginCell"/>.
    ///
    /// A footprint is an arbitrary set of cells (design §5), so L, U and ring shapes need no special
    /// handling anywhere - not in the grid, not in the pathfinder, not in the order layer.
    /// </summary>
    public struct BuildingPlacement : IComponentData
    {
        public int2 OriginCell;
        public GridRotation Rotation;
    }
}
