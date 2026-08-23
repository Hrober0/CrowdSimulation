using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// A one-way connection between two cells that are not neighbours: the grid's only adjacency that is not
    /// geometric.
    ///
    /// Everything else in <see cref="GridMap"/> says what a cell *is* and lets adjacency fall out of the
    /// coordinates - four neighbours, gated by an exit bit. A bridge cannot be said that way. Its deck is
    /// blocked like any other footprint, so the two banks are not neighbours and no exit mask can make them
    /// one; what connects them is a rule rather than a shape, and a rule needs somewhere to live.
    ///
    /// So a link is stored, not derived, and it is stored in the grid rather than beside it. That is the whole
    /// reason the three searches - the flow field, the chunk search and the coarse graph - need no new
    /// argument threaded through them to see a bridge: they already carry the <see cref="GridMap"/>, and the
    /// link is part of it.
    ///
    /// **Directed, always.** <see cref="From"/> to <see cref="To"/> and no way back. A two-way crossing is two
    /// links, which is the honest way to say it - the pair then has two mouths, two queues and two costs, and
    /// nothing has to special-case a bridge that happens to be symmetric.
    /// </summary>
    public struct NavLink
    {
        /// <summary>
        /// The walkable cell an agent steps onto to be taken across. A cell may be the mouth of at most one
        /// link, which is what lets every consumer answer "where does this cell lead" with one lookup instead
        /// of a set.
        /// </summary>
        public int2 From;

        /// <summary>The walkable cell it is put down on. Also at most one link's far end.</summary>
        public int2 To;

        /// <summary>
        /// What the crossing costs, in <see cref="NavCost"/> units, on top of stepping onto <see cref="To"/>.
        ///
        /// It has to be paid or the searches would treat a bridge as a free teleport and route half the map
        /// over it. A crossing that takes as long as walking the same span should cost the same as walking it,
        /// which is <see cref="NavCost.STEP"/> per cell of span.
        ///
        /// Zero means the slot is empty - a real link always costs at least one step.
        /// </summary>
        public ushort Cost;

        public bool IsValid => Cost > 0;

        public override string ToString() => $"NavLink({From} -> {To}, cost {Cost})";
    }
}
