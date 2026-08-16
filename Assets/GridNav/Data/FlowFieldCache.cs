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

        /// <summary>
        /// Finds a built field for a destination. Says nothing about whether it is still fresh.
        ///
        /// **The goal check is the load-bearing half.** A slot can be recycled for a different destination
        /// while a mapping to it still exists, and serving that field steers every agent bound for one place
        /// towards another - which on screen is a crowd walking somewhere it has no business being and then
        /// standing there, because arrival is measured against the goal it is still, correctly, aiming at. A
        /// mapping that no longer owns its slot reads as "no field", the agent asks again, and the next frame
        /// builds it one: wrong-and-invisible becomes late-by-a-frame.
        /// </summary>
        public bool TryGetSlot(int2 goalCell, out int slot)
        {
            if (!_slotByGoal.TryGetValue(goalCell, out slot))
            {
                return false;
            }

            FlowFieldSlot entry = _storage.GetSlot(slot);
            return entry.Built && entry.GoalCell.Equals(goalCell);
        }

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

        /// <summary>
        /// Whether a built and up-to-date field says there is no route to <paramref name="destination"/> from
        /// <paramref name="from"/> at all.
        ///
        /// The three ways of not knowing - no field yet, a field built before the last grid change, a cell
        /// outside its window - all answer **false**, and deliberately so. Callers use this to decide *not* to
        /// try something, and refusing on a guess is how an agent ends up with nothing to do next to a door it
        /// could have walked into. A wrong "false" costs one attempt, which builds the field that answers
        /// properly next time; a wrong "true" is a task nobody ever picks up.
        /// </summary>
        public bool IsKnownUnreachable(int2 destination, int2 from, in GridMap map) =>
            TryGetSlot(destination, out int slot)
            && IsFresh(slot, map)
            && Covers(slot, from)
            && IntegrationAt(slot, from) == FlowField.UNREACHABLE;

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
        ///
        /// False when every slot is being read or built *this frame*. There is genuinely nowhere to put the
        /// field, and saying so is the only safe answer: the request is dropped, the agent asks again next
        /// frame, and it waits one frame more than it wanted to. Serving it anyway - by taking a slot somebody
        /// else is already using - is how one field ends up answering for two destinations, and that is not a
        /// frame of latency but a crowd walking to the wrong place indefinitely.
        /// </summary>
        internal bool TryAcquireSlot(int2 goalCell, in GridMap map, out int slot)
        {
            if (_slotByGoal.TryGetValue(goalCell, out slot))
            {
                if (_storage.GetSlot(slot).GoalCell.Equals(goalCell))
                {
                    return true;
                }

                // A leftover from an eviction: the slot belongs to somebody else now, so this mapping is
                // worse than nothing and goes before a new slot is looked for.
                _slotByGoal.Remove(goalCell);
            }

            if (!TryTakeSlot(out slot))
            {
                return false;
            }

            ReleaseMapping(slot);

            _slotByGoal[goalCell] = slot;
            _storage.SetSlot(slot, new FlowFieldSlot
            {
                GoalCell = goalCell,
                WindowMin = FlowField.WindowMinFor(map, goalCell),
                VersionStamp = 0,
                LastUsed = _clock.Value,
                Built = false,
            });

            return true;
        }

        /// <summary>
        /// A slot nothing is using this frame: a never-used one, or the one asked for longest ago.
        /// </summary>
        private bool TryTakeSlot(out int slot)
        {
            slot = -1;
            int oldest = int.MaxValue;

            for (int candidate = 0; candidate < CAPACITY; candidate++)
            {
                FlowFieldSlot entry = _storage.GetSlot(candidate);

                // Claimed or read this very frame. Taking it would hand two destinations the same field -
                // note that a slot claimed a moment ago is not Built yet either, so "not built" is not
                // enough to call a slot free.
                if (entry.LastUsed == _clock.Value)
                {
                    continue;
                }

                if (!entry.Built && entry.LastUsed == 0)
                {
                    slot = candidate;
                    return true; // never used at all
                }

                if (entry.LastUsed < oldest)
                {
                    oldest = entry.LastUsed;
                    slot = candidate;
                }
            }

            return slot >= 0;
        }

        /// <summary>
        /// Drops whatever destination is still pointing at this slot, built or not.
        ///
        /// "Built or not" is the bug this replaced: a slot claimed but not yet built was recycled without its
        /// mapping being cleared, so the destination that had claimed it went on resolving to a field that by
        /// then belonged to somewhere else. The ownership test is what makes the removal safe - a slot that
        /// was never used carries a default goal cell, which may well be a real destination living in another
        /// slot, and removing *that* would strand it.
        /// </summary>
        private void ReleaseMapping(int slot)
        {
            int2 previous = _storage.GetSlot(slot).GoalCell;

            if (_slotByGoal.TryGetValue(previous, out int owner) && owner == slot)
            {
                _slotByGoal.Remove(previous);
            }
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
