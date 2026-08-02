using System;

namespace GridNav
{
    /// <summary>
    /// Annotations on a cell. None of them affect routing - cost does that - they mark what a cell *is*
    /// so the rules layer can ask questions like "may an idle agent park here" (design §3).
    /// </summary>
    [Flags]
    public enum CellFlags : byte
    {
        None = 0,

        /// <summary>Part of a building footprint.</summary>
        Building = 1 << 0,

        /// <summary>Painted road. The speed-up itself comes from the lower cost, not from this bit.</summary>
        Road = 1 << 1,

        /// <summary>Agents with nothing to do must not stop here. Roads and entrances carry it (§6).</summary>
        NoIdle = 1 << 2,

        /// <summary>A building entrance cell.</summary>
        Entrance = 1 << 3,
    }
}
