using System;
using Unity.Collections;
using Unity.Entities;

namespace GridNav
{
    /// <summary>
    /// Which views of the map somebody actually needs gates for (design §14.4). Singleton, owned by
    /// <see cref="ChunkGateGraphSystem"/>.
    ///
    /// Gates are derived from passability, so a seeker that can break a wall down needs its own graph - and
    /// building one for every traversal that *could* exist would be several full-map scans a frame for views
    /// nobody is walking on. So a graph is built only once something asks, the same bargain the flow field
    /// cache makes: asking is free and idempotent, and an agent asks every frame it is routing.
    ///
    /// Unlike a field, a graph is never evicted. There are at most a handful of traversals in a game, they
    /// are cheap to hold, and dropping one the moment the last soldier of a class died would mean rebuilding
    /// the whole map's gates when the next one is trained.
    /// </summary>
    public struct GateGraphRequests : IComponentData, IDisposable
    {
        private NativeParallelHashSet<Traversal> _wanted;

        public GateGraphRequests(Allocator allocator)
        {
            _wanted = new NativeParallelHashSet<Traversal>(Traversal.MAX_CLASSES, allocator);
        }

        public bool IsCreated => _wanted.IsCreated;

        public NativeParallelHashSet<Traversal> Wanted => _wanted;

        public void Request(Traversal traversal) => _wanted.Add(traversal);

        public void Dispose()
        {
            if (_wanted.IsCreated)
            {
                _wanted.Dispose();
            }
        }
    }
}
