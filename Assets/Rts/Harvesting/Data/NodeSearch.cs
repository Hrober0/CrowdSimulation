using GridNav;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Finding the next thing to harvest (design §14 step 10).
    ///
    /// **Nearest first, and it rings outward to get there.** A wood is felled from the outside in, which is
    /// what lets a stand dig its way back out of anything it has grown itself into: a tree that cannot be
    /// reached today is reached once the ring in front of it has gone. Ringing out from the door also stops
    /// the search at the first hit, so a mine standing on a seam pays for one cell rather than for its whole
    /// range.
    ///
    /// It is deliberately *not* a scan of every node in the world. That is the difference the
    /// <see cref="ResourceNode"/> exclusion buys: the economy's linear passes stay proportional to the
    /// number of buildings, and finding a tree costs a bounded walk over cells instead.
    /// </summary>
    public static class NodeSearch
    {
        /// <summary>
        /// The nearest harvestable object of the wanted kind, with something left in it, that an agent
        /// could actually walk to from <paramref name="from"/>.
        ///
        /// Unreachable candidates are skipped rather than ending the search, for the same reason the order
        /// market passes over an agent that cannot get somewhere: the point is to find one that works, and
        /// the far side of a river is a perfectly good reason for the second-nearest tree to be the answer.
        /// </summary>
        public static bool TryFindNearest(
            in CellObjectMap objects,
            in ComponentLookup<CellObject> cellObjects,
            in BufferLookup<StorageSlot> slots,
            in Reachability reach,
            int2 from,
            in Reaps reaps,
            out Entity node,
            out int2 cell)
        {
            for (int ring = 0; ring <= reaps.Range; ring++)
            {
                if (TryFindInRing(objects, cellObjects, slots, reach, from, reaps, ring, out node, out cell))
                {
                    return true;
                }
            }

            node = Entity.Null;
            cell = default;
            return false;
        }

        /// <summary>
        /// One square ring of cells at Chebyshev distance <paramref name="ring"/>. Walking the ring rather
        /// than the whole square is what makes the search cost what it finds: the first ring holding
        /// anything ends it, and the rings beyond are never visited.
        /// </summary>
        private static bool TryFindInRing(
            in CellObjectMap objects,
            in ComponentLookup<CellObject> cellObjects,
            in BufferLookup<StorageSlot> slots,
            in Reachability reach,
            int2 centre,
            in Reaps reaps,
            int ring,
            out Entity node,
            out int2 cell)
        {
            if (ring == 0)
            {
                return TryFindOn(objects, cellObjects, slots, reach, centre, reaps, centre, out node, out cell);
            }

            for (int x = -ring; x <= ring; x++)
            {
                int2 top = centre + new int2(x, ring);
                if (TryFindOn(objects, cellObjects, slots, reach, top, reaps, centre, out node, out cell))
                {
                    return true;
                }

                int2 bottom = centre + new int2(x, -ring);
                if (TryFindOn(objects, cellObjects, slots, reach, bottom, reaps, centre, out node, out cell))
                {
                    return true;
                }
            }

            // The corners belong to the rows above and are skipped here, or they would be visited twice.
            for (int y = -ring + 1; y <= ring - 1; y++)
            {
                int2 left = centre + new int2(-ring, y);
                if (TryFindOn(objects, cellObjects, slots, reach, left, reaps, centre, out node, out cell))
                {
                    return true;
                }

                int2 right = centre + new int2(ring, y);
                if (TryFindOn(objects, cellObjects, slots, reach, right, reaps, centre, out node, out cell))
                {
                    return true;
                }
            }

            node = Entity.Null;
            cell = default;
            return false;
        }

        private static bool TryFindOn(
            in CellObjectMap objects,
            in ComponentLookup<CellObject> cellObjects,
            in BufferLookup<StorageSlot> slots,
            in Reachability reach,
            int2 at,
            in Reaps reaps,
            int2 from,
            out Entity node,
            out int2 cell)
        {
            node = Entity.Null;
            cell = at;

            if (!objects.IsOccupied(at))
            {
                return false;
            }

            NativeParallelMultiHashMap<int2, Entity>.Enumerator here = objects.GetObjectsAt(at);
            while (here.MoveNext())
            {
                Entity candidate = here.Current;

                if (!cellObjects.TryGetComponent(candidate, out CellObject standing)
                    || standing.Kind != reaps.Harvests)
                {
                    continue;
                }

                if (!slots.TryGetBuffer(candidate, out DynamicBuffer<StorageSlot> stock)
                    || !StorageSlotUtils.TryGetSlotIndex(stock, reaps.Yields, out int index)
                    || stock[index].AvailableOut <= 0)
                {
                    continue;
                }

                if (!reach.CanTry(at, from))
                {
                    // Nothing on this cell can be got at, so the rest of what is standing on it is no better.
                    return false;
                }

                node = candidate;
                return true;
            }

            return false;
        }
    }
}
