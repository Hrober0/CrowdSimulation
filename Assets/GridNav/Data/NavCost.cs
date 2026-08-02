namespace GridNav
{
    /// <summary>
    /// What it costs to move. Terrain cost is added on top of a fixed per-step cost, so that a road
    /// (terrain cost 0) is cheap but not free - without the step cost, a detour of a hundred road cells
    /// would tie with a single one, and every search would wander.
    /// </summary>
    public static class NavCost
    {
        /// <summary>Cost of one cardinal step onto a zero-cost cell.</summary>
        public const int STEP = 10;

        /// <summary>Cost of stepping onto a cell with this cost sum. Only call it for passable cells.</summary>
        public static int OfCell(ushort costSum) => STEP + costSum;
    }
}
