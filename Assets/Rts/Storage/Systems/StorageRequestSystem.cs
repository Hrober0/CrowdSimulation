using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Turns storage thresholds into haul orders (design §7, §13.3 #6). This is the whole of "who wants
    /// what": a slot below its <see cref="StorageSlot.DeliverInUpTo"/> with a non-zero priority is asking,
    /// and nothing else in the game asks for anything.
    ///
    /// A warehouse's standing low-priority request is what reproduces the old connection pairs with no pairs
    /// to author and no connection UI - it simply always wants more, and loses to everyone who wants it more.
    ///
    /// Requests are refreshed rather than reposted. An unmet request is the same order a tick older, which is
    /// what gives <see cref="OrderAgingSystem"/> something to age; deleting and re-adding it every tick would
    /// reset its age forever and starve it by construction.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup), OrderFirst = true)]
    public partial struct StorageRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton(new OrderBook(64, Allocator.Persistent), "OrderBook");
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out OrderBook book))
            {
                book.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            OrderBook book = SystemAPI.GetSingleton<OrderBook>();
            double now = SystemAPI.Time.ElapsedTime;

            var live = new NativeHashSet<int>(64, Allocator.Temp);

            foreach ((DynamicBuffer<StorageSlot> slots, Entity building)
                     in SystemAPI.Query<DynamicBuffer<StorageSlot>>().WithEntityAccess())
            {
                foreach (StorageSlot slot in slots)
                {
                    if (!slot.Requests)
                    {
                        continue;
                    }

                    Post(book, building, slot, now);
                    live.Add(RequestKey(building, slot.Item));
                }
            }

            Retire(book, live);
            live.Dispose();
        }

        private static void Post(OrderBook book, Entity building, in StorageSlot slot, double now)
        {
            if (book.TryFind(building, slot.Item, out int index))
            {
                Order existing = book[index];
                existing.Amount = slot.WantedIn;
                existing.Priority = slot.Priority;
                book[index] = existing;
                return;
            }

            book.Add(new Order
            {
                Kind = OrderKind.Haul,
                Target = building,
                Item = slot.Item,
                Amount = slot.WantedIn,
                Priority = slot.Priority,
                PostedTime = now,

                // Seeded, not left at zero: aging reads the difference from *now*, so a default of zero is
                // an order that has been waiting since the world began and outranks everything on sight.
                LastClaimedTime = now,
                Effective = slot.Priority,
            });
        }

        /// <summary>
        /// Drops orders whose demand has gone - the slot filled up, the threshold was lowered, the building
        /// was demolished. Pull means an order exists only while somebody is still asking.
        /// </summary>
        private static void Retire(OrderBook book, in NativeHashSet<int> live)
        {
            for (int i = book.Length - 1; i >= 0; i--)
            {
                if (book[i].Kind != OrderKind.Haul)
                {
                    continue;
                }

                if (!live.Contains(RequestKey(book[i].Target, book[i].Item)))
                {
                    book.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// A request is identified by which building wants which item. Entity index and version are folded
        /// in with the item so that a recycled entity id cannot inherit the previous occupant's request.
        /// </summary>
        private static int RequestKey(Entity building, ItemId item) =>
            building.Index * 397 ^ building.Version * 31 ^ item.Value;
    }
}
