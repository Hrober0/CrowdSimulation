using System;

namespace Navigation
{
    public readonly struct AStarNode : IComparable<AStarNode>
    {
        public readonly int Index;
        public readonly float GCost;
        public readonly float FCost;
        public readonly int CameFromIndex;

        public AStarNode(int index, float gCost, float fCost, int cameFromIndex)
        {
            Index = index;
            GCost = gCost;
            FCost = fCost;
            CameFromIndex = cameFromIndex;
        }

        public int CompareTo(AStarNode other) => FCost.CompareTo(other.FCost);
    }
}