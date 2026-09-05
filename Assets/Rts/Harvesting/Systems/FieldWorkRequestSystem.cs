using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Asks for a worker when a building has something to do out on the map (design §14 steps 10 and 11):
    /// a mine or a lumber camp with room for what it would bring back, or a planter with ground to plant.
    ///
    /// The same pull rule and very nearly the same code as <see cref="WorkRequestSystem"/>, because a
    /// gatherer *is* a worker: a free bench and something to do, and the building posts a work order; lose
    /// either and it stops asking, and its miners drift off to do something else. What differs is only what
    /// the worker is then told to do, which is <c>OrderAssignSystem</c>'s business rather than this one's.
    ///
    /// **Both halves are asked for here, by one system, on purpose.** A farm has <see cref="Reaps"/> and
    /// <see cref="Sows"/> both, and two systems posting the same kind of order for the same building would
    /// each retire what the other had just posted - which is exactly the trap that had to be closed when
    /// gathering was added beside crafting. One owner per order, and the question does not arise.
    ///
    /// **Whether anything is actually in range is not asked here.** It could be - the searches are bounded -
    /// but the answer is wanted at the moment a worker is being sent, against the world as it is then, and
    /// asking twice would let a seam be found by this system and mined out before the other one looked. A
    /// mine with nothing in range simply fails to place its order, tick after tick, at the cost of one
    /// bounded ring search - and starts working the moment a seam appears in range, with nothing to reset.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup))]
    [UpdateAfter(typeof(StorageRequestSystem))]
    [UpdateBefore(typeof(OrderAgingSystem))]
    public partial struct FieldWorkRequestSystem : ISystem
    {
        /// <summary>
        /// Above a warehouse's standing request and below a crafter's input, so a spare pair of hands goes
        /// gathering before it goes shuffling surplus between stores, and a workshop that is actually short
        /// of something still wins.
        /// </summary>
        private const byte FIELD_WORK_PRIORITY = 4;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OrderBook>();
            state.RequireForUpdate(SystemAPI.QueryBuilder().WithAny<Reaps, Sows>().Build());
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

            // A planter has nowhere to put anything and needs no shelf: what it produces is standing on the
            // ground when it has finished. A building that both sows and reaps is seen by both loops and
            // posts once, because Post refreshes an order it finds rather than adding a second.
            foreach ((RefRO<Interior> interior, Entity building)
                     in SystemAPI.Query<RefRO<Interior>>().WithAll<Sows>().WithEntityAccess())
            {
                if (!interior.ValueRO.HasRoom)
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
                existing.Priority = FIELD_WORK_PRIORITY;
                book[index] = existing;
                return;
            }

            book.Add(new Order
            {
                Kind = OrderKind.Work,
                Target = building,
                Item = ItemId.None,
                Amount = 1,
                Priority = FIELD_WORK_PRIORITY,
                PostedTime = now,

                // See StorageRequestSystem.Post: zero here would be an order older than the world.
                LastClaimedTime = now,
                Effective = FIELD_WORK_PRIORITY,
            });
        }

        /// <summary>
        /// Only ever retires orders belonging to buildings that work the land. <see cref="WorkRequestSystem"/> runs
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

                bool mine = entities.HasComponent<Reaps>(order.Target)
                            || entities.HasComponent<Sows>(order.Target);

                if (entities.Exists(order.Target) && !mine)
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
