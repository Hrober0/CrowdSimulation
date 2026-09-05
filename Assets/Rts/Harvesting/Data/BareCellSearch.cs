using GridNav;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Finding somewhere to plant (design §14 step 11).
    ///
    /// The mirror of <see cref="NodeSearch"/>, and it rings outward for the same reason - but it is looking
    /// for the absence of something rather than the presence of one, which is why it reads the grid rather
    /// than the cell map. <see cref="CellFlags.Object"/> is the whole question: a cell with anything standing
    /// on it is taken, and so is one that is part of a building, a road or a doorstep.
    /// </summary>
    public static class BareCellSearch
    {
        /// <summary>
        /// The nearest cell worth planting on: clear ground, reachable, and with nothing standing directly
        /// beside it.
        ///
        /// **Nearest, not farthest.** The plan called for farthest-first, on the reasoning that planting
        /// works back towards the building so nothing is ever sown across the route to what has not been sown
        /// yet. That reasoning belonged to a world where a tree was a wall; since step 9 it is not, so
        /// nothing can be sown across anything. Nearest-first is what is left, and it is cheaper - it rings
        /// out and stops at the first answer - and it keeps the walk short.
        /// </summary>
        public static bool TryFindNearest(in GridMap map, in Reachability reach, int2 from, int range,
                                          out int2 cell)
        {
            for (int ring = 0; ring <= range; ring++)
            {
                int cells = CellRing.Count(ring);
                for (int i = 0; i < cells; i++)
                {
                    int2 at = CellRing.At(from, ring, i);
                    if (IsPlantable(map, at) && reach.CanTry(at, from))
                    {
                        cell = at;
                        return true;
                    }
                }
            }

            cell = default;
            return false;
        }

        /// <summary>
        /// Clear ground with clear ground beside it.
        ///
        /// **The spacing is the point, not tidiness.** Without it a planter fills every cell it can reach and
        /// the result is a solid block: slow to cross, and a lumber camp working it has to walk through the
        /// whole thing to reach the middle. Refusing a cell with something already beside it leaves lanes
        /// through the grove by construction, and it is also what makes a planter *stop* - it runs out of
        /// places rather than running out of range.
        ///
        /// Only the four orthogonal neighbours, so plantings sit diagonally: half the ground covered, and a
        /// clear step available from every cell in the grove.
        /// </summary>
        private static bool IsPlantable(in GridMap map, int2 cell)
        {
            if (!map.InBounds(cell) || !map.IsPassable(cell) || map.GetFlags(cell) != CellFlags.None)
            {
                return false;
            }

            for (int d = 0; d < DirectionUtils.DIRECTION_COUNT; d++)
            {
                int2 neighbour = cell + DirectionUtils.Offset((Direction)d);
                if (map.InBounds(neighbour) && map.GetCell(neighbour).Has(CellFlags.Object))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
