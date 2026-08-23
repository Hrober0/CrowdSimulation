namespace GridNav
{
    /// <summary>
    /// What kind of opening a gate slot holds.
    ///
    /// The two borders a chunk owns are shared with a neighbour, so each is owned by exactly one of the two -
    /// the one on the lower side - and there is no chance of building the same gate twice. A
    /// <see cref="Link"/> gate follows the same rule for the same reason: it is owned by the chunk its
    /// <see cref="NavLink.From"/> mouth stands in.
    /// </summary>
    public enum GateBorder : byte
    {
        East = 0,
        North = 1,

        /// <summary>
        /// A <see cref="NavLink"/> that leaves the chunk. Not a border at all - the two cells are nowhere near
        /// each other - but it is a node in the coarse graph for exactly the same reason a border gate is: it is
        /// a way out of this chunk into another one, at a known cost, in a known direction.
        /// </summary>
        Link = 2,
    }
}
