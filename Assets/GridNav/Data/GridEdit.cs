using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// One queued change to one cell. Nothing writes the grid directly: a change is described here, enqueued in
    /// <see cref="GridEditQueue"/>, and applied by GridApplySystem (design §13.2, invariant 1).
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
        }

        public readonly int2 Cell;
        public readonly OpType Op;
        public readonly int Value;

        private GridEdit(int2 cell, OpType op, int value)
        {
            Cell = cell;
            Op = op;
            Value = value;
        }

        /// <summary>
        /// Adding an object is <c>+cost</c> and removing it is <c>-cost</c>; the sum stays exact, which is why
        /// chopping one of three trees on a cell reopens it correctly (§3).
        /// </summary>
        public static GridEdit CostDelta(int2 cell, int delta) => new(cell, OpType.CostDelta, delta);

        public static GridEdit AddFlags(int2 cell, CellFlags flags) => new(cell, OpType.AddFlags, (int)flags);

        public static GridEdit RemoveFlags(int2 cell, CellFlags flags) => new(cell, OpType.RemoveFlags, (int)flags);

        public static GridEdit SetExits(int2 cell, byte exits) => new(cell, OpType.SetExits, exits);

        public override string ToString() => $"GridEdit({Op} {Value} at {Cell})";
    }
}
