using Unity.Burst;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Lets waiting orders climb (design §8, §13.3 #9).
    ///
    /// <c>effective = priority + time since last served * rate</c>. Without it a stream of high-priority
    /// requests starves every low-priority one forever - the warehouse that always wants more would never be
    /// filled while any crafter still wanted anything. With it, waiting is itself a claim on attention, and
    /// the ordering stays a single number the assign pass can sort on.
    ///
    /// **Since last served, not since posted** (<see cref="Order.LastClaimedTime"/>). Age measured from
    /// posting is never spent, so an order that is being served keeps the lead it started with and its
    /// equals never get a turn. Measured from the last claim, a batch handed out costs the order its banked
    /// age and the next one goes to whoever has waited longest - which is what "waiting is a claim on
    /// attention" meant in the first place.
    ///
    /// The rate is deliberately slow: a minute of waiting is worth one priority band, so aging breaks
    /// deadlocks without letting a stale warehouse top-up outrank a construction site that has just asked.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup))]
    [UpdateAfter(typeof(StorageRequestSystem))]
    public partial struct OrderAgingSystem : ISystem
    {
        /// <summary>Priority bands gained per second of waiting.</summary>
        private const float AGING_RATE = 1f / 60f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OrderBook>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            OrderBook book = SystemAPI.GetSingleton<OrderBook>();
            double now = SystemAPI.Time.ElapsedTime;

            for (int i = 0; i < book.Length; i++)
            {
                Order order = book[i];
                order.Effective = order.Priority + (float)(now - order.LastClaimedTime) * AGING_RATE;
                book[i] = order;
            }
        }
    }
}
