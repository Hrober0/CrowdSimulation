using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    public enum InteriorTransitionKind : byte
    {
        Enter,
        Exit,
    }

    /// <summary>One agent crossing a threshold, in whichever direction.</summary>
    public struct InteriorTransition
    {
        public Entity Agent;

        public Entity Building;

        /// <summary>The entrance cell: where the agent came from, or where it is put back down.</summary>
        public int2 Cell;

        public InteriorTransitionKind Kind;
    }

    /// <summary>
    /// Thresholds waiting to be crossed (design §13.3 #13 and #18). Singleton, owned by
    /// <see cref="InteriorTransitionSystem"/>.
    ///
    /// The queue exists because entering a building touches two entities at once - the agent's enable flags
    /// and the building's occupancy - and §13.2 keeps that kind of work single-threaded and in one place.
    /// The step machine only says "this agent is going in"; one system decides what that costs.
    /// </summary>
    public struct InteriorTransitionQueue : IComponentData, IDisposable
    {
        private NativeQueue<InteriorTransition> _pending;

        public InteriorTransitionQueue(Allocator allocator)
        {
            _pending = new NativeQueue<InteriorTransition>(allocator);
        }

        public bool IsCreated => _pending.IsCreated;

        public int Count => _pending.Count;

        public void Enqueue(InteriorTransition transition) => _pending.Enqueue(transition);

        public bool TryDequeue(out InteriorTransition transition) => _pending.TryDequeue(out transition);

        public void Dispose()
        {
            if (_pending.IsCreated)
            {
                _pending.Dispose();
            }
        }
    }
}
