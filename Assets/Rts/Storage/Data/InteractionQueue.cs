using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    public enum InteractionKind : byte
    {
        /// <summary>Standing about. A rest, a shift at a machine - nothing changes hands.</summary>
        None,

        Pickup,

        Deposit,

        /// <summary>One batch at a crafter. Repeats itself while there is work (§9's <c>Interact(inf)</c>).</summary>
        Work,

        /// <summary>
        /// A sapling goes in the ground. The first interaction about a *place* rather than about a thing -
        /// which is why the event below carries a cell as well as a target.
        /// </summary>
        Plant,
    }

    /// <summary>An <see cref="TaskStepKind.Interact"/> step that has run its course.</summary>
    public struct InteractionEvent
    {
        public Entity Agent;

        public Entity Target;

        /// <summary>
        /// Where the step happened. Empty for the interactions that are about an entity - a pickup works off
        /// the source's identity and never needs to know where it was standing - and load-bearing for the
        /// ones that are about ground.
        /// </summary>
        public int2 Cell;

        public InteractionKind Kind;
    }

    /// <summary>
    /// Finished interactions waiting to be settled (design §13.3 #13 and #19). Singleton, owned by
    /// <see cref="InteractionSystem"/>.
    ///
    /// Same split as the interior queue, for the same reason: the step machine says *that* something
    /// happened, one system decides *what it costs*. Here that keeps §13.2 invariant 2 true - slot amounts
    /// have exactly one writer, and it is not the sixty-hertz parallel phase.
    /// </summary>
    public struct InteractionQueue : IComponentData, IDisposable
    {
        private NativeQueue<InteractionEvent> _pending;

        public InteractionQueue(Allocator allocator)
        {
            _pending = new NativeQueue<InteractionEvent>(allocator);
        }

        public bool IsCreated => _pending.IsCreated;

        public int Count => _pending.Count;

        public void Enqueue(InteractionEvent interaction) => _pending.Enqueue(interaction);

        public bool TryDequeue(out InteractionEvent interaction) => _pending.TryDequeue(out interaction);

        public void Dispose()
        {
            if (_pending.IsCreated)
            {
                _pending.Dispose();
            }
        }
    }
}
