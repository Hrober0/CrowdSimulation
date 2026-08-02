using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// A small pool of flow fields, keyed by destination and evicted least-recently-used (design §4).
    ///
    /// Agents do not build fields; they ask for one and read whatever is there. A field asked for on one
    /// frame is served on the next, which is what keeps field generation and steering from ever needing a
    /// sync point between them (§13.2, invariant 3).
    /// </summary>
    public struct FlowFieldCache : IComponentData, IDisposable
    {
        /// <summary>32 windows of 128x128 at three bytes a cell is about 1.5 MB.</summary>
        public const int CAPACITY = 32;

        private FlowFieldStorage _storage;
        private NativeParallelHashMap<int2, int> _slotByGoal;
        private NativeParallelHashSet<int2> _requests;
        private NativeReference<int> _clock;

        public FlowFieldCache(Allocator allocator)
        {
            _storage = new FlowFieldStorage(CAPACITY, allocator);
            _slotByGoal = new NativeParallelHashMap<int2, int>(CAPACITY * 2, allocator);
            _requests = new NativeParallelHashSet<int2>(256, allocator);
            _clock = new NativeReference<int>(allocator);
        }

        public bool IsCreated => _storage.IsCreated;

        /// <summary>The cells themselves. Hand this to a build job, not the whole cache.</summary>
        public FlowFieldStorage Storage => _storage;

        /// <summary>Destinations asked for since the last build pass. A set, so asking twice is free.</summary>
        public NativeParallelHashSet<int2> Requests => _requests;

        /// <summary>Ask for a field. Cheap and idempotent - agents call it every frame they are walking.</summary>
        public void Request(int2 goalCell) => _requests.Add(goalCell);

        public NativeParallelHashSet<int2>.ParallelWriter RequestWriter() => _requests.AsParallelWriter();

        public FlowFieldSlot GetSlot(int slot) => _storage.GetSlot(slot);

        /// <summary>Finds a built field for a destination. Says nothing about whether it is still fresh.</summary>
        public bool TryGetSlot(int2 goalCell, out int slot) =>
            _slotByGoal.TryGetValue(goalCell, out slot) && _storage.GetSlot(slot).Built;

        /// <summary>Whether nothing under the field's window has changed since it was built.</summary>
        public bool IsFresh(int slot, in GridMap map)
        {
            FlowFieldSlot entry = _storage.GetSlot(slot);
            return entry.Built && entry.VersionStamp == FlowField.VersionStampOf(map, entry.WindowMin);
        }

        public bool Covers(int slot, int2 cell) => FlowField.Contains(_storage.GetSlot(slot).WindowMin, cell);

        /// <summary>
        /// The step to take from <paramref name="cell"/> towards the field's destination. False when the
        /// cell is outside the window, has no route, or is the destination itself.
        /// </summary>
        public bool TryGetDirection(int slot, int2 cell, out Direction direction)
        {
            direction = Direction.North;

            int2 windowMin = _storage.GetSlot(slot).WindowMin;
            if (!FlowField.Contains(windowMin, cell))
            {
                return false;
            }

            byte stored = _storage.ReadDirection(slot, FlowField.LocalIndexOf(windowMin, cell));
            if (stored == FlowField.NO_DIRECTION)
            {
                return false;
            }

            direction = (Direction)stored;
            return true;
        }

        /// <summary>Cost of walking from a cell to the destination, or <see cref="FlowField.UNREACHABLE"/>.</summary>
        public ushort IntegrationAt(int slot, int2 cell)
        {
            int2 windowMin = _storage.GetSlot(slot).WindowMin;
            return FlowField.Contains(windowMin, cell)
                ? _storage.ReadIntegration(slot, FlowField.LocalIndexOf(windowMin, cell))
                : FlowField.UNREACHABLE;
        }

        internal int Tick() => ++_clock.Value;

        internal void MarkUsed(int slot)
        {
            FlowFieldSlot entry = _storage.GetSlot(slot);
            entry.LastUsed = _clock.Value;
            _storage.SetSlot(slot, entry);
        }

        /// <summary>
        /// A slot for this destination: the one it already owns, a free one, or the one used longest ago.
        /// </summary>
        internal int AcquireSlot(int2 goalCell, in GridMap map)
        {
            if (_slotByGoal.TryGetValue(goalCell, out int existing))
            {
                return existing;
            }

            int chosen = 0;
            int oldest = int.MaxValue;
            for (int slot = 0; slot < CAPACITY; slot++)
            {
                FlowFieldSlot entry = _storage.GetSlot(slot);

                // Claimed or read this very frame. Taking it would hand two destinations the same field -
                // note that a slot claimed a moment ago is not Built yet either, so "not built" is not
                // enough to call a slot free.
                if (entry.LastUsed == _clock.Value)
                {
                    continue;
                }

                if (!entry.Built && entry.LastUsed == 0)
                {
                    chosen = slot;
                    break; // never used at all
                }

                if (entry.LastUsed < oldest)
                {
                    oldest = entry.LastUsed;
                    chosen = slot;
                }
            }

            FlowFieldSlot evicted = _storage.GetSlot(chosen);
            if (evicted.Built)
            {
                _slotByGoal.Remove(evicted.GoalCell);
            }

            _slotByGoal[goalCell] = chosen;
            _storage.SetSlot(chosen, new FlowFieldSlot
            {
                GoalCell = goalCell,
                WindowMin = FlowField.WindowMinFor(map, goalCell),
                VersionStamp = 0,
                LastUsed = _clock.Value,
                Built = false,
            });

            return chosen;
        }

        public void Dispose()
        {
            _storage.Dispose();

            if (_slotByGoal.IsCreated)
            {
                _slotByGoal.Dispose();
            }

            if (_requests.IsCreated)
            {
                _requests.Dispose();
            }

            if (_clock.IsCreated)
            {
                _clock.Dispose();
            }
        }
    }
}
