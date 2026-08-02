using System;

namespace GridNav
{
    /// <summary>
    /// Which ways a chunk border can be crossed at a gate. A one-way road reaching a border makes the
    /// crossing one-way too, and the coarse graph has to know - otherwise it hands the agent a route the
    /// fine layer cannot walk and the agent stalls with no visible cause (§4.1).
    /// </summary>
    [Flags]
    public enum GateCrossing : byte
    {
        None = 0,

        /// <summary>From the owning chunk into the neighbour.</summary>
        AToB = 1 << 0,

        /// <summary>From the neighbour into the owning chunk.</summary>
        BToA = 1 << 1,

        Both = AToB | BToA,
    }
}
