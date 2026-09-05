using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Asks for a worker when a building has work to do (design §8, §13.3 #7).
    ///
    /// Same pull rule as storage: a crafter with a free work slot, its inputs on the shelf and room for what
    /// it would make posts an order, and stops posting the moment any of those stops being true. Nothing
    /// pushes a worker at a building, so a crafter that has run out of inputs simply has no order and its
    /// workers drift off to do something else.
    ///
    /// A work slot *is* an interior slot (§6). There is no second occupancy mechanism: the worker claims
    /// room inside exactly as an idle agent claims a bed, and the same enter/exit path releases it.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup))]
    [UpdateAfter(typeof(StorageRequestSystem))]
    [UpdateBefore(typeof(OrderAgingSystem))]
    public partial struct WorkRequestSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OrderBook>();
        }

        public void OnUpdate(ref SystemState state)
        {
            OrderBook book = SystemAPI.GetSingleton<OrderBook>();
            double now = SystemAPI.Time.ElapsedTime;

            var live = new NativeHashSet<Entity>(16, Allocator.Temp);

            foreach ((RefRO<Recipe> recipe, RefRO<Interior> interior, DynamicBuffer<StorageSlot> slots,
                      DynamicBuffer<RecipeInput> inputs, DynamicBuffer<RecipeOutput> outputs, Entity crafter)
                     in SystemAPI.Query<RefRO<Recipe>, RefRO<Interior>, DynamicBuffer<StorageSlot>,
                                        DynamicBuffer<RecipeInput>, DynamicBuffer<RecipeOutput>>()
                                 .WithEntityAccess())
            {
                if (!interior.ValueRO.HasRoom || !RecipeUtils.CanCraft(slots, inputs, outputs))
                {
                    continue;
                }

                Post(book, crafter, recipe.ValueRO, now);
                live.Add(crafter);
            }

            Retire(ref state, book, live);
            live.Dispose();
        }

        private static void Post(OrderBook book, Entity crafter, in Recipe recipe, double now)
        {
            // Work orders carry no item, so (crafter, none) is their key and cannot collide with the haul
            // orders the same building posts for its inputs - those all name a real item.
            if (book.TryFind(crafter, ItemId.None, out int index))
            {
                Order existing = book[index];
                existing.Amount = 1;
                existing.Priority = recipe.Priority;
                book[index] = existing;
                return;
            }

            book.Add(new Order
            {
                Kind = OrderKind.Work,
                Target = crafter,
                Item = ItemId.None,
                Amount = 1,
                Priority = recipe.Priority,
                PostedTime = now,

                // See StorageRequestSystem.Post: zero here would be an order older than the world.
                LastClaimedTime = now,
                Effective = recipe.Priority,
            });
        }

        /// <summary>
        /// Only crafters' work orders. <see cref="GatherRequestSystem"/> posts the same kind of order for
        /// buildings that have no recipe, and a retirement pass that could not tell them apart would delete
        /// the other system's orders on the tick they were posted - each system undoing the other for ever,
        /// with nothing ever getting as far as being claimed.
        ///
        /// A target that no longer exists belongs to neither, so it falls through and goes.
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

                if (entities.Exists(order.Target) && !entities.HasComponent<Recipe>(order.Target))
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
