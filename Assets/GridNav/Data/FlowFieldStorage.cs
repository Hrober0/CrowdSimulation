using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace GridNav
{
    /// <summary>
    /// The raw cells of every cached field. Split out from <see cref="FlowFieldCache"/> because this is the
    /// part a build job touches: the cache's lookup structures are hash containers, which no parallel job
    /// may carry at all, even without reading them.
    ///
    /// Builds run one field per slot and never touch another slot's range, which is what makes lifting the
    /// per-index job restriction safe here.
    /// </summary>
    public struct FlowFieldStorage : IDisposable
    {
        [NativeDisableParallelForRestriction] private NativeArray<ushort> _integration;
        [NativeDisableParallelForRestriction] private NativeArray<byte> _directions;
        [NativeDisableParallelForRestriction] private NativeArray<FlowFieldSlot> _slots;

        public FlowFieldStorage(int capacity, Allocator allocator)
        {
            _integration = new NativeArray<ushort>(capacity * FlowField.WINDOW_CELLS, allocator);
            _directions = new NativeArray<byte>(capacity * FlowField.WINDOW_CELLS, allocator);
            _slots = new NativeArray<FlowFieldSlot>(capacity, allocator);
        }

        public bool IsCreated => _integration.IsCreated;

        public FlowFieldSlot GetSlot(int slot) => _slots[slot];

        public void SetSlot(int slot, FlowFieldSlot entry) => _slots[slot] = entry;

        public ushort ReadIntegration(int slot, int localIndex) =>
            _integration[slot * FlowField.WINDOW_CELLS + localIndex];

        public void WriteIntegration(int slot, int localIndex, ushort cost) =>
            _integration[slot * FlowField.WINDOW_CELLS + localIndex] = cost;

        public byte ReadDirection(int slot, int localIndex) =>
            _directions[slot * FlowField.WINDOW_CELLS + localIndex];

        public void WriteDirection(int slot, int localIndex, byte direction) =>
            _directions[slot * FlowField.WINDOW_CELLS + localIndex] = direction;

        public void Dispose()
        {
            if (_integration.IsCreated)
            {
                _integration.Dispose();
            }

            if (_directions.IsCreated)
            {
                _directions.Dispose();
            }

            if (_slots.IsCreated)
            {
                _slots.Dispose();
            }
        }
    }
}
