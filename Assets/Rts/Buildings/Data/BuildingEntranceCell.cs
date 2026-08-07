using GridNav;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// A resolved entrance: the walkable cell an agent stands on to go in, in absolute coordinates and
    /// already rotated.
    ///
    /// Cleanup data for the same reason as <see cref="BuildingFootprintCell"/> - demolishing has to clear the
    /// exact cells that were flagged, and by then the placement and the authored offsets are gone.
    /// </summary>
    public struct BuildingEntranceCell : ICleanupBufferElementData
    {
        /// <summary>Outside the footprint, and therefore passable.</summary>
        public int2 Cell;

        /// <summary>Direction from <see cref="Cell"/> into the building.</summary>
        public Direction Facing;
    }
}
