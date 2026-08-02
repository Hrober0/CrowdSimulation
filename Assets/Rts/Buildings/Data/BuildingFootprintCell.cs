using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// A grid cell a building actually occupies, in absolute coordinates and already rotated.
    ///
    /// Cleanup data, so it outlives the building: demolishing has to give back the exact cells that were
    /// taken, and by then the placement and the footprint are gone. It is also the answer to "which cells
    /// does this building sit on" without recomputing the rotation.
    /// </summary>
    public struct BuildingFootprintCell : ICleanupBufferElementData
    {
        public int2 Cell;
    }
}
