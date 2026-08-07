using Unity.Entities;

namespace Rts
{
    public enum OrderKind : byte
    {
        Haul,
        Work,
        Fight,
    }

    /// <summary>
    /// A piece of work the world wants doing (design §8). Orders are posted by **demand only** - pull, never
    /// push - which is what stops the economy from generating work nobody asked for.
    ///
    /// One order per (<see cref="Target"/>, <see cref="Item"/>) request, carrying the whole outstanding need
    /// rather than one order per unit. A construction site wanting 500 planks is one order of 500, not 500
    /// orders: the number of *trips* is capped by hauler count and <see cref="HaulLimit"/>, and the number of
    /// units in flight is capped by the reservations, so nothing is gained by making the queue enormous.
    ///
    /// <see cref="Source"/> is empty until the order is claimed. Where the goods come from is a matching
    /// decision made against live stock at claim time (§7), not something the requester can know.
    /// </summary>
    public struct Order
    {
        public OrderKind Kind;

        public Entity Source;

        public Entity Target;

        public ItemId Item;

        /// <summary>Units still wanted. Counted down as haulers claim batches of it.</summary>
        public int Amount;

        public byte Priority;

        public double PostedTime;

        /// <summary>
        /// <see cref="Priority"/> plus age, recomputed each tick by <see cref="OrderAgingSystem"/>. Aging is
        /// what stops a low-priority request from starving forever behind a busy high-priority one - it
        /// generalises the "oldest pickup first" tie-break the delivery example used.
        /// </summary>
        public float Effective;
    }
}
