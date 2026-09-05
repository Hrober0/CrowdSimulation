using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>What a finished planting is about to put on the ground.</summary>
    public struct PlantRequest
    {
        public int2 Cell;

        public Sows What;
    }

    /// <summary>
    /// Plantings waiting to happen (design §14 step 11). Singleton, owned by <see cref="PlantingSystem"/>.
    ///
    /// The same split as the interaction and interior queues, and here it is not only tidiness: creating an
    /// entity is a structural change, and <see cref="InteractionSystem"/> is in the middle of a loop holding
    /// buffers when it finds out that a planting finished. Doing it there would invalidate them underneath
    /// itself. So the interaction says *that* a tree was planted, and one system decides what appears.
    /// </summary>
    public struct PlantQueue : IComponentData, IDisposable
    {
        private NativeQueue<PlantRequest> _pending;

        public PlantQueue(Allocator allocator)
        {
            _pending = new NativeQueue<PlantRequest>(allocator);
        }

        public bool IsCreated => _pending.IsCreated;

        public int Count => _pending.Count;

        public void Enqueue(PlantRequest request) => _pending.Enqueue(request);

        public bool TryDequeue(out PlantRequest request) => _pending.TryDequeue(out request);

        public void Dispose()
        {
            if (_pending.IsCreated)
            {
                _pending.Dispose();
            }
        }
    }
}
