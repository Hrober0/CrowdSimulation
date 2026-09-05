using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// A building that harvests what is standing on the ground near it (design §14 step 10).
    ///
    /// One component covers a mine, a lumber camp and the reaping half of a farm, because none of the
    /// differences between them is a difference in behaviour: what is taken, what it turns into, and how far
    /// out the building will send somebody. A planter is the other half of the same idea and is a component
    /// of its own, so that a building which only plants is a building that simply does not have this one -
    /// absent rather than disabled, so the query never visits it.
    ///
    /// **How many gatherers work here is <see cref="Interior.Capacity"/>**, not a field here. A gatherer is a
    /// worker (§6), and the room it claims is the same room a baker claims at a bench; the only difference is
    /// that it walks back out of the door instead of staying inside.
    /// </summary>
    public struct Reaps : IComponentData
    {
        /// <summary>What it takes: trees for a lumber camp, seams for a mine.</summary>
        public ObjectKind Harvests;

        /// <summary>What arrives on its own shelf as a result.</summary>
        public ItemId Yields;

        /// <summary>
        /// How far out, in cells, a gatherer will be sent.
        ///
        /// A range is safe here in a way the order market's was not (§8). There, a distance limit could
        /// refuse a job that nobody nearer would ever take, and refuse it identically for ever. Here, a
        /// building with nothing in range is a building with nothing to do - it posts no order, its workers
        /// go and do something else, and the player is told the answer by a mine that never fills up.
        /// </summary>
        public int Range;
    }
}
