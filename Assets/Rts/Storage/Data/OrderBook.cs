using System;
using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Every unclaimed order in the world (design §8). Singleton, owned by <see cref="StorageRequestSystem"/>.
    ///
    /// A list rather than a queue, because an order is not consumed by being looked at: the assign pass sorts
    /// by effective priority, takes what it can, and leaves the rest to age. Orders are matched by
    /// (target, item), so a request that is still unmet next tick is the *same* order a tick older rather
    /// than a new one that has forgotten how long it has been waiting.
    /// </summary>
    public struct OrderBook : IComponentData, IDisposable
    {
        private NativeList<Order> _orders;

        public OrderBook(int capacity, Allocator allocator)
        {
            _orders = new NativeList<Order>(capacity, allocator);
        }

        public bool IsCreated => _orders.IsCreated;

        public int Length => _orders.Length;

        public Order this[int index]
        {
            get => _orders[index];
            set => _orders[index] = value;
        }

        public bool TryFind(Entity target, ItemId item, out int index)
        {
            for (int i = 0; i < _orders.Length; i++)
            {
                if (_orders[i].Target == target && _orders[i].Item == item)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        public void Add(Order order) => _orders.Add(order);

        /// <summary>Order within the book carries no meaning, so removal is a swap-back.</summary>
        public void RemoveAt(int index) => _orders.RemoveAtSwapBack(index);

        public void Clear() => _orders.Clear();

        public NativeArray<Order> AsArray() => _orders.AsArray();

        public void Dispose()
        {
            if (_orders.IsCreated)
            {
                _orders.Dispose();
            }
        }
    }
}
