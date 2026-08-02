using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// What <c>CellObjectRegistrationSystem</c> actually applied for an object, kept so that removal can
    /// refund exactly what was added.
    ///
    /// Cleanup data, so it outlives the entity: destroying a tree leaves this behind for one frame, which is
    /// how the system learns that a cost has to be given back and a map entry erased. Refunding from the
    /// live <see cref="CellObject"/> instead would be impossible - by then it is gone.
    /// </summary>
    public struct CellObjectRegistered : ICleanupComponentData
    {
        public int2 Cell;
        public ushort Cost;
    }
}
