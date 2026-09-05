using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Asks for a gatherer when a harvesting building has somewhere to put what it would bring back
    /// (design §14 step 10).
    ///
    /// The same pull rule and very nearly the same code as <see cref="WorkRequestSystem"/>, because a
    /// gatherer *is* a worker: a free bench and room on the shelf, and the building posts a work order; lose
    /// either and it stops asking, and its miners drift off to do something else. What differs is only what
    /// the worker is then told to do, which is <c>OrderAssignSystem</c>'s business rather than this one's.
    ///
    /// **Whether anything is actually in range is not asked here.** It could be - the search is bounded - but
    /// the answer is wanted at the moment a worker is being sent, against the stock that exists then, and
    /// asking twice would let a seam be found by this system and mined out before the other one looked. A
    /// mine with nothing in range simply fails to place its order, tick after tick, at the cost of one
    /// bounded ring search - and starts working the moment a seam appears in range, with nothing to reset.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup))]
    [UpdateAfter(typeof(StorageRequestSystem))]
    [UpdateBefore(typeof(OrderAgingSystem))]
    public partial struct GatherRequestSystem : ISystem
    {
        /// <summary>
        /// Above a warehouse's standing request and below a crafter's input, so a spare pair of hands goes
        /// gathering before it goes shuffling surplus between stores, and a workshop that is actually short
        /// of something still wins.
        /// </summary>
        private const byte GATHER_PRIORITY = 4;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OrderBook>();
            state.RequireForUpdate(SystemAPI.QueryBuilder().WithAll<Reaps>().Build());
        }

        public void OnUpdate(ref SystemState state)
        {
            OrderBook book = SystemAPI.GetSingleton<OrderBook>();
            double now = SystemAPI.Time.ElapsedTime;

            var live = new NativeHashSet<Entity>(16, Allocator.Temp);

            foreach ((RefRO<Reaps> reaps, RefRO<Interior> interior, DynamicBuffer<StorageSlot> slots,
                      Entity building)
                     in SystemAPI.Query<RefRO<Reaps>, RefRO<Interior>, DynamicBuffer<StorageSlot>>()
                                 .WithEntityAccess())
            {
                if (!interior.ValueRO.HasRoom || !HasRoomFor(slots, reaps.ValueRO.Yields))
                {
                    continue;
                }

                Post(book, building, now);
                live.Add(building);
            }

            Retire(ref state, book, live);
            live.Dispose();
        }

        /// <summary>
        /// Room counted against the reservations, not just against what is on the shelf. Two gatherers out
        /// with a load each have already spent that room, and a mine that forgot so would keep hiring until
        /// the deliveries started bouncing off a full shelf.
        /// </summary>
        private static bool HasRoomFor(in DynamicBuffer<StorageSlot> slots, ItemId item) =>
            StorageSlotUtils.TryGetSlotIndex(slots, item, out int index) && slots[index].FreeCapacity > 0;

        private static void Post(OrderBook book, Entity building, double now)
        {
            // Keyed on (building, no item), exactly as a crafter's work order is - and for the same reason it
            // cannot collide with the haul orders a building posts, which all name a real item.
            if (book.TryFind(building, ItemId.None, out int index))
            {
                Order existing = book[index];
                existing.Amount = 1;
                existing.Priority = GATHER_PRIORITY;
                book[index] = existing;
                return;
            }

            book.Add(new Order
            {
                Kind = OrderKind.Work,
                Target = building,
                Item = ItemId.None,
                Amount = 1,
                Priority = GATHER_PRIORITY,
                PostedTime = now,

                // See StorageRequestSystem.Post: zero here would be an order older than the world.
                LastClaimedTime = now,
                Effective = GATHER_PRIORITY,
            });
        }

        /// <summary>
        /// Only ever retires orders belonging to harvesting buildings. <see cref="WorkRequestSystem"/> runs
        /// its own retirement over the same order kind, and a pass that could not tell the two apart would
        /// spend every tick deleting the other's orders.
        ///
        /// A target that no longer exists belongs to nobody, so it falls through to the live check and goes -
        /// otherwise a demolished mine would leave a work order asking for ever on behalf of a building that
        /// is not there.
        /// </summary>
        private static void Retire(ref SystemState state, OrderBook book, in NativeHashSet<Entity> live)
        {
            EntityManager entities = state.EntityManager;

            for (int i = book.Length - 1; i >= 0; i--)
            {
                Order order = book[i];
                if (order.Kind != OrderKind.Work)
                {
                    continue;
                }

                if (entities.Exists(order.Target) && !entities.HasComponent<Reaps>(order.Target))
                {
                    continue;
                }

                if (!live.Contains(order.Target))
                {
                    book.RemoveAt(i);
                }
            }
        }
    }
}
