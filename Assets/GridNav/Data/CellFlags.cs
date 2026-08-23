using System;

namespace GridNav
{
    /// <summary>
    /// Annotations on a cell. Most of them do not affect routing - cost does that - they mark what a cell *is*
    /// so the rules layer can ask questions like "may an idle agent park here" (design §3).
    ///
    /// The two link bits are the exception, and they are the exception on purpose: they are the fast reject in
    /// front of the <see cref="NavLink"/> table, so a search can ask "does anything unusual happen here" with
    /// the byte it was going to read anyway. They are set and cleared only by the link operations of
    /// <see cref="GridEdit"/>, never by <c>AddFlags</c>, because a bit that changes where agents can walk has
    /// to bump the passability version and the flag operations deliberately bump nothing.
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

        /// <summary>A <see cref="NavLink"/> starts here: stepping onto this cell leads somewhere far away.</summary>
        LinkEntry = 1 << 4,

        /// <summary>A <see cref="NavLink"/> ends here.</summary>
        LinkExit = 1 << 5,
    }
}
