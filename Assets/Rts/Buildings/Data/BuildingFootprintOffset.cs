using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// One cell of a building's shape, as an offset from its origin and before rotation - the design-time
    /// template. What the building ends up standing on is <see cref="BuildingFootprintCell"/>: absolute,
    /// rotated, and resolved once at placement.
    /// </summary>
    public struct BuildingFootprintOffset : IBufferElementData
    {
        public int2 Offset;
    }
}
