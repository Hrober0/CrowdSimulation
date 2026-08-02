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
    /// </summary>
    [BurstCompile]
    public struct BuildFlowFieldJob : IJobParallelFor
    {
        private const int QUEUE_CAPACITY = FlowField.WINDOW_CELLS * DirectionUtils.DIRECTION_COUNT + 1;

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
            }

            queue.Dispose();
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

                Storage.WriteDirection(slot, local, bestDirection);
            }
        }

        private struct CellNode : IComparable<CellNode>
        {
            public int LocalIndex;
            public int Cost;

            public int CompareTo(CellNode other) => Cost.CompareTo(other.Cost);
        }
    }
}
