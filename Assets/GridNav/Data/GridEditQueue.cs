using System;
using Unity.Collections;

namespace GridNav
{
    /// <summary>
    /// The only way into the grid. Producers - world objects, building footprints, player brushes - enqueue
    /// <see cref="GridEdit"/>s from anywhere; GridApplySystem drains the queue once per frame and is the only
    /// code that can (draining is internal to GridNav, so the assembly boundary enforces the single writer).
    ///
    /// Producers take the <see cref="GridWorld"/> singleton read-write. That serialises the producer jobs
    /// against each other, which is what the job safety system requires of a shared queue and costs nothing:
    /// enqueueing is a handful of instructions next to the work that decided to enqueue.
    /// </summary>
    public struct GridEditQueue : IDisposable
    {
        private NativeQueue<GridEdit> _edits;

        public GridEditQueue(Allocator allocator)
        {
            _edits = new NativeQueue<GridEdit>(allocator);
        }

        public bool IsCreated => _edits.IsCreated;

        public int Count => _edits.Count;

        public void Enqueue(GridEdit edit) => _edits.Enqueue(edit);

        public ParallelWriter AsParallelWriter() => new(_edits.AsParallelWriter());

        internal bool TryDequeue(out GridEdit edit) => _edits.TryDequeue(out edit);

        public void Dispose()
        {
            if (_edits.IsCreated)
            {
                _edits.Dispose();
            }
        }

        public struct ParallelWriter
        {
            private NativeQueue<GridEdit>.ParallelWriter _writer;

            internal ParallelWriter(NativeQueue<GridEdit>.ParallelWriter writer)
            {
                _writer = writer;
            }

            public void Enqueue(GridEdit edit) => _writer.Enqueue(edit);
        }
    }
}
