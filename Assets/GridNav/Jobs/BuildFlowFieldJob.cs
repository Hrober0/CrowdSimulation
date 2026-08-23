using System;
using CustomNativeCollections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Builds one flow field per index: an integration pass outwards from the destination, then a direction
    /// pass that reads the gradient (design §4).
    ///
    /// The integration expands *backwards* - it asks "who can reach this cell", not "where can I go" - so it
    /// tests the neighbour's exit bit. Getting that the wrong way round builds a field that sends agents the
    /// wrong way down a one-way road, and nothing anywhere reports an error (§3).
    ///
    /// A <see cref="NavLink"/> is one more incoming edge, relaxed by the same rule from the cell it lands on.
    /// Both mouths have to be inside the window - a field is a window on the map and a link is only expressible
    /// in it if both of its ends are in view - which for a bridge shorter than the window is the ordinary case,
    /// and when it is not the coarse graph carries the crossing instead (§4.1).
    /// </summary>
    [BurstCompile]
    public struct BuildFlowFieldJob : IJobParallelFor
    {
        /// <summary>
        /// A cell can be improved once per incoming edge, and a link is a fifth one, so the bound has to make
        /// room for it - a queue that overflows loses relaxations silently and builds a field with holes in it.
        /// </summary>
        private const int QUEUE_CAPACITY =
            FlowField.WINDOW_CELLS * (DirectionUtils.DIRECTION_COUNT + 1) + 1;

        public GridMap Map;

        [ReadOnly] public NativeArray<int> Slots;

        public FlowFieldStorage Storage;

        public void Execute(int index)
        {
            int slot = Slots[index];
            FlowFieldSlot entry = Storage.GetSlot(slot);

            Integrate(slot, entry);
            StepDownhill(slot, entry);

            entry.VersionStamp = FlowField.VersionStampOf(Map, entry.WindowMin);
            entry.Built = true;
            Storage.SetSlot(slot, entry);
        }

        private void Integrate(int slot, in FlowFieldSlot entry)
        {
            for (int i = 0; i < FlowField.WINDOW_CELLS; i++)
            {
                Storage.WriteIntegration(slot, i, FlowField.UNREACHABLE);
            }

            if (!Map.IsPassable(entry.GoalCell) || !FlowField.Contains(entry.WindowMin, entry.GoalCell))
            {
                return;
            }

            var queue = new NativePriorityQueue<CellNode>(QUEUE_CAPACITY, Allocator.Temp);

            int goalLocal = FlowField.LocalIndexOf(entry.WindowMin, entry.GoalCell);
            Storage.WriteIntegration(slot, goalLocal, 0);
            queue.Enqueue(new CellNode { LocalIndex = goalLocal, Cost = 0 });

            while (queue.Count > 0)
            {
                CellNode node = queue.Dequeue();
                if (node.Cost > Storage.ReadIntegration(slot, node.LocalIndex))
                {
                    continue;
                }

                int2 cell = FlowField.CellOf(entry.WindowMin, node.LocalIndex);
                int stepCost = NavCost.OfCell(Map.GetCost(cell));

                for (int d = 0; d < DirectionUtils.DIRECTION_COUNT; d++)
                {
                    var direction = (Direction)d;
                    int2 neighbour = cell + DirectionUtils.Offset(direction);

                    if (!FlowField.Contains(entry.WindowMin, neighbour))
                    {
                        continue;
                    }

                    // The real move is neighbour -> cell, so it is the neighbour that must be allowed to leave.
                    if (!Map.CanTraverseFromNeighbour(cell, direction))
                    {
                        continue;
                    }

                    int cost = node.Cost + stepCost;
                    if (cost >= FlowField.UNREACHABLE)
                    {
                        continue; // further than the field can express; leave it unreachable
                    }

                    int neighbourLocal = FlowField.LocalIndexOf(entry.WindowMin, neighbour);
                    if (cost >= Storage.ReadIntegration(slot, neighbourLocal))
                    {
                        continue;
                    }

                    Storage.WriteIntegration(slot, neighbourLocal, (ushort)cost);
                    queue.Enqueue(new CellNode { LocalIndex = neighbourLocal, Cost = cost });
                }

                RelaxLink(slot, entry, ref queue, cell, node.Cost, stepCost);
            }

            queue.Dispose();
        }

        /// <summary>
        /// Relaxes the mouth of a link that lands on this cell.
        ///
        /// The same arithmetic as a neighbour, and deliberately so: reaching <paramref name="cell"/> from the
        /// link's <c>From</c> costs the crossing plus stepping onto this cell, exactly as reaching it from a
        /// neighbour costs the step onto this cell alone. Sharing <paramref name="stepCost"/> with the
        /// neighbour loop is what keeps the two consistent - if a link were priced any other way, the
        /// direction pass could not recognise it as the predecessor it was.
        /// </summary>
        private void RelaxLink(int slot, in FlowFieldSlot entry, ref NativePriorityQueue<CellNode> queue,
                               int2 cell, int here, int stepCost)
        {
            if (!Map.TryGetLinkTo(cell, out NavLink link) || !FlowField.Contains(entry.WindowMin, link.From))
            {
                return;
            }

            // The mouth is a cell an agent has to be able to stand on. A blocked one means the bridge has been
            // built over or its bank has been walled in, and the crossing is not available until that changes.
            if (!Map.IsPassable(link.From))
            {
                return;
            }

            int cost = here + stepCost + link.Cost;
            if (cost >= FlowField.UNREACHABLE)
            {
                return;
            }

            int mouthLocal = FlowField.LocalIndexOf(entry.WindowMin, link.From);
            if (cost >= Storage.ReadIntegration(slot, mouthLocal))
            {
                return;
            }

            Storage.WriteIntegration(slot, mouthLocal, (ushort)cost);
            queue.Enqueue(new CellNode { LocalIndex = mouthLocal, Cost = cost });
        }

        private void StepDownhill(int slot, in FlowFieldSlot entry)
        {
            for (int local = 0; local < FlowField.WINDOW_CELLS; local++)
            {
                Storage.WriteDirection(slot, local, FlowField.NO_DIRECTION);

                ushort here = Storage.ReadIntegration(slot, local);
                if (here == FlowField.UNREACHABLE || here == 0)
                {
                    continue; // nowhere to go from an unreachable cell, and nowhere to go from the destination
                }

                int2 cell = FlowField.CellOf(entry.WindowMin, local);
                ushort best = here;
                byte bestDirection = FlowField.NO_DIRECTION;

                for (int d = 0; d < DirectionUtils.DIRECTION_COUNT; d++)
                {
                    var direction = (Direction)d;
                    int2 neighbour = cell + DirectionUtils.Offset(direction);

                    if (!FlowField.Contains(entry.WindowMin, neighbour) || !Map.CanTraverse(cell, direction))
                    {
                        continue;
                    }

                    ushort there = Storage.ReadIntegration(slot, FlowField.LocalIndexOf(entry.WindowMin, neighbour));
                    if (there >= best)
                    {
                        continue;
                    }

                    best = there;
                    bestDirection = (byte)d;
                }

                if (TakesLink(slot, entry, cell, here, best))
                {
                    bestDirection = FlowField.LINK_STEP;
                }

                Storage.WriteDirection(slot, local, bestDirection);
            }
        }

        /// <summary>
        /// Whether the way on from this cell is over a link.
        ///
        /// The test is that the link *is* the predecessor the integration used: walking the crossing reproduces
        /// this cell's cost exactly. That is the strongest statement available and it costs one addition, which
        /// matters because both ways of being wrong are bad in a way nothing reports - a false yes puts an agent
        /// on a bridge its route never asked for, and a false no leaves it standing on a mouth with no direction
        /// to follow and no reason it can be told.
        ///
        /// The second half breaks a tie towards walking. When a neighbour gets there for the same money the
        /// agent should stay on the ground: a bridge has a queue at its mouth and open ground does not.
        /// </summary>
        private bool TakesLink(int slot, in FlowFieldSlot entry, int2 cell, ushort here, ushort bestNeighbour)
        {
            if (!Map.TryGetLinkFrom(cell, out NavLink link) || !FlowField.Contains(entry.WindowMin, link.To))
            {
                return false;
            }

            ushort across = Storage.ReadIntegration(slot, FlowField.LocalIndexOf(entry.WindowMin, link.To));
            if (across == FlowField.UNREACHABLE || across > bestNeighbour)
            {
                return false;
            }

            return across + NavCost.OfCell(Map.GetCost(link.To)) + link.Cost == here;
        }

        private struct CellNode : IComparable<CellNode>
        {
            public int LocalIndex;
            public int Cost;

            public int CompareTo(CellNode other) => Cost.CompareTo(other.Cost);
        }
    }
}
