using GridNav;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// A door in a building's wall, authored unrotated alongside the footprint (design §5).
    ///
    /// <see cref="Offset"/> names the *footprint* cell the door is cut into and <see cref="Side"/> which of
    /// its four walls it is on; the cell an agent actually stands on is the neighbour on that side. Naming
    /// the wall rather than the outside cell is what makes "the approach cell is outside the footprint" a
    /// consequence of the authoring instead of something the author has to keep true by hand - including
    /// after the building is rotated.
    /// </summary>
    public struct BuildingEntranceOffset : IBufferElementData
    {
        public int2 Offset;

        public Direction Side;
    }
}
