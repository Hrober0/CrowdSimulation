using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// One item's worth of storage in a building (design §7). Needs are not involved and there are no
    /// connections to author: what a building wants and what it will give away are two numbers on the slot,
    /// and the haul that follows is worked out from them.
    ///
    /// The two thresholds are both load-bearing. A single "desired amount" cannot say "never give away the
    /// inputs I have already received", and without that a priority-9 crafter strips a priority-8 crafter's
    /// input buffer and both starve.
    ///
    /// <code>
    /// role                     DeliverInUpTo   DeliverOutDownTo   Priority
    /// mine / crafter output    0               0                  0
    /// warehouse                Capacity        0                  1
    /// crafter input            ~2 batches      = DeliverInUpTo    5-9
    /// construction site        required        = required         10
    /// </code>
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct StorageSlot : IBufferElementData
    {
        public ItemId Item;

        public int Amount;

        public int Capacity;

        /// <summary>Requests deliveries while <see cref="Amount"/> is below this.</summary>
        public int DeliverInUpTo;

        /// <summary>Gives units away only while <see cref="Amount"/> is above this.</summary>
        public int DeliverOutDownTo;

        /// <summary>
        /// Who wins when supply is scarce. Zero is reserved for pure sources: it never requests, and it
        /// loses to every requester, since a source must be strictly below its taker (§7).
        /// </summary>
        public byte Priority;

        /// <summary>Units on their way here. Holds the *capacity*, so two haulers cannot fill one space.</summary>
        public int ReservedIn;

        /// <summary>Units promised away. Holds the *stock*, so two haulers cannot claim one crate.</summary>
        public int ReservedOut;

        /// <summary>Units that can be given away without dropping below <see cref="DeliverOutDownTo"/>.</summary>
        public readonly int AvailableOut => math.max(0, Amount - ReservedOut - DeliverOutDownTo);

        /// <summary>Units still wanted before <see cref="DeliverInUpTo"/> is reached.</summary>
        public readonly int WantedIn =>
            math.max(0, math.min(DeliverInUpTo, Capacity) - Amount - ReservedIn);

        /// <summary>Physical room left, thresholds ignored. What a delivery actually needs.</summary>
        public readonly int FreeCapacity => math.max(0, Capacity - Amount - ReservedIn);

        /// <summary>Whether this slot should be posting a haul order right now.</summary>
        public readonly bool Requests => Priority > 0 && WantedIn > 0;
    }
}
