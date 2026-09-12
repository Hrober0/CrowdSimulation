using System;
using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>A hit that has landed. What it costs is <see cref="DamageApplySystem"/>'s to decide.</summary>
    public struct DamageEvent
    {
        public Entity Target;

        public int Amount;
    }

    /// <summary>
    /// Damage waiting to be applied (design §13.5, §14 step 12). Singleton, owned by
    /// <see cref="DamageApplySystem"/>.
    ///
    /// The same split as the interaction, interior and planting queues, and here it earns itself twice over.
    /// **One writer of <see cref="Health"/>**: a turret, and soon a soldier, are many producers of the same
    /// fact, and a queue is what lets many producers run behind one consumer (§13.5). **No structural change
    /// at the producer**: what dies is destroyed, and an entity vanishing underneath a system that is in the
    /// middle of iterating is the failure the plant queue was introduced to avoid.
    /// </summary>
    public struct DamageQueue : IComponentData, IDisposable
    {
        private NativeQueue<DamageEvent> _pending;

        public DamageQueue(Allocator allocator)
        {
            _pending = new NativeQueue<DamageEvent>(allocator);
        }

        public bool IsCreated => _pending.IsCreated;

        public int Count => _pending.Count;

        public void Enqueue(DamageEvent damage) => _pending.Enqueue(damage);

        public bool TryDequeue(out DamageEvent damage) => _pending.TryDequeue(out damage);

        public void Dispose()
        {
            if (_pending.IsCreated)
            {
                _pending.Dispose();
            }
        }
    }
}
