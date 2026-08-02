namespace GridNav
{
    /// <summary>
    /// A snapshot of one cell. The grid stores the three fields in separate arrays (SoA); this is what a
    /// caller gets when it wants all of them at once.
    /// </summary>
    public readonly struct CellData
    {
        /// <summary>
        /// A cell is impassable from this cost up. It is a threshold, not a saturation point - the stored sum
        /// keeps counting past it so that removing one of several blockers restores the exact previous cost (§3).
        /// </summary>
        public const ushort BLOCKED = 255;

        /// <summary>What a cell outside the map reads as: blocked, and no way in or out.</summary>
        public static CellData OutOfBounds => new(BLOCKED, CellFlags.None, DirectionUtils.NO_EXITS);

        /// <summary>Terrain base cost plus the cost of every object standing on the cell.</summary>
        public readonly ushort CostSum;

        public readonly CellFlags Flags;

        /// <summary>4-bit allowed-exit mask, one bit per <see cref="Direction"/>.</summary>
        public readonly byte Exits;

        public CellData(ushort costSum, CellFlags flags, byte exits)
        {
            CostSum = costSum;
            Flags = flags;
            Exits = exits;
        }

        public bool IsPassable => CostSum < BLOCKED;

        public bool Has(CellFlags flags) => (Flags & flags) != 0;

        /// <summary>Whether an agent standing here is allowed to step to the neighbour in that direction.</summary>
        public bool CanExit(Direction direction) => DirectionUtils.Allows(Exits, direction);

        public bool IsOneWay => Exits != DirectionUtils.ALL_EXITS;

        public override string ToString() => $"Cell(cost: {CostSum}, flags: {Flags}, exits: {Exits:X1})";
    }
}
