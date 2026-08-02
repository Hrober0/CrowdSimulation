using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Simulation space (float2) to cell space (int2). One cell is exactly one unit (design §3), so this is
    /// thin on purpose - it exists so that no caller ever open-codes the rounding rule and gets it half right.
    /// </summary>
    public static class GridCoords
    {
        public static int2 CellOf(float2 position) => (int2)math.floor(position);

        /// <summary>Flow fields steer to cell centres (§3), so this is the point agents are sent to.</summary>
        public static float2 CellCenter(int2 cell) => new(cell.x + 0.5f, cell.y + 0.5f);

        public static float2 CellMin(int2 cell) => new(cell.x, cell.y);

        public static float2 CellMax(int2 cell) => new(cell.x + 1f, cell.y + 1f);

        public static int2 Neighbour(int2 cell, Direction direction) => cell + DirectionUtils.Offset(direction);
    }
}
