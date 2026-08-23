using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// One queued change to the grid. Nothing writes it directly: a change is described here, enqueued in
    /// <see cref="GridEditQueue"/>, and applied by GridApplySystem (design §13.2, invariant 1).
    ///
    /// Most operations touch one cell. A link touches two, which is why there is a second cell field rather
    /// than a pair of half-edits - a link that arrived in two pieces could be applied in two different frames,
    /// and half a bridge is a hole in the map that no consumer could recognise as unfinished.
    /// </summary>
    public readonly struct GridEdit
    {
        public enum OpType : byte
        {
            /// <summary>Add <see cref="Value"/> to the cell's cost sum. Negative removes.</summary>
            CostDelta,

            /// <summary>Set the flag bits in <see cref="Value"/>.</summary>
            AddFlags,

            /// <summary>Clear the flag bits in <see cref="Value"/>.</summary>
            RemoveFlags,

            /// <summary>Replace the exit mask with <see cref="Value"/>.</summary>
            SetExits,

            /// <summary>Connect <see cref="Cell"/> to <see cref="Other"/> one way, for <see cref="Value"/>.</summary>
            AddLink,

            /// <summary>Take that connection back out.</summary>
            RemoveLink,
        }

        public readonly int2 Cell;

        /// <summary>The far end of a link. Unused by every other operation.</summary>
        public readonly int2 Other;

        public readonly OpType Op;
        public readonly int Value;

        private GridEdit(int2 cell, int2 other, OpType op, int value)
        {
            Cell = cell;
            Other = other;
            Op = op;
            Value = value;
        }

        /// <summary>
        /// Adding an object is <c>+cost</c> and removing it is <c>-cost</c>; the sum stays exact, which is why
        /// chopping one of three trees on a cell reopens it correctly (§3).
        /// </summary>
        public static GridEdit CostDelta(int2 cell, int delta) => new(cell, default, OpType.CostDelta, delta);

        public static GridEdit AddFlags(int2 cell, CellFlags flags) =>
            new(cell, default, OpType.AddFlags, (int)flags);

        public static GridEdit RemoveFlags(int2 cell, CellFlags flags) =>
            new(cell, default, OpType.RemoveFlags, (int)flags);

        public static GridEdit SetExits(int2 cell, byte exits) => new(cell, default, OpType.SetExits, exits);

        /// <summary>
        /// A one-way crossing from <paramref name="from"/> to <paramref name="to"/>. Both cells must be
        /// walkable - they are where an agent stands before and after, not part of what it crosses.
        /// </summary>
        public static GridEdit AddLink(int2 from, int2 to, ushort cost) =>
            new(from, to, OpType.AddLink, cost);

        public static GridEdit RemoveLink(int2 from, int2 to) => new(from, to, OpType.RemoveLink, 0);

        public override string ToString() =>
            Op is OpType.AddLink or OpType.RemoveLink
                ? $"GridEdit({Op} {Cell} -> {Other}, cost {Value})"
                : $"GridEdit({Op} {Value} at {Cell})";
    }
}
