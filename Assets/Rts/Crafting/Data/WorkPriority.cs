using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// How badly a building wants a worker, and the one number about *labour* the player may change.
    ///
    /// Storage already had this: every shelf carries a priority and the panel lets it be nudged, which is the
    /// player's one lever on where goods go (§7). Work had no equivalent - a crafter's priority was a
    /// constant in the catalog and a field worker's was a constant in the system - so there was no way to say
    /// "the planter matters more than the bakery today", and a building with nobody in it looked broken with
    /// nothing to do about it.
    ///
    /// One component for every kind of work, because the order market does not care which: a crafter, a mine
    /// and a planter all post an <see cref="OrderKind.Work"/> order, and they should all rank against each
    /// other on the same scale.
    /// </summary>
    public struct WorkPriority : IComponentData
    {
        public byte Value;

        /// <summary>What a building asks with when nobody has said otherwise.</summary>
        public const byte DEFAULT = 5;

        /// <summary>The band the panel may move it within. Zero would be "never staffed", which is useful.</summary>
        public const byte MAX = 20;
    }
}
