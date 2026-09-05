using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// The cells at a given Chebyshev distance from a centre, addressed by index.
    ///
    /// Searching outward one ring at a time is what makes "the nearest thing that will do" cost what it finds
    /// rather than what it might have found: the first ring holding an answer ends the search, and a building
    /// standing on what it wants pays for one cell instead of for its whole range.
    ///
    /// An index rather than an enumerator so it can be walked from a Burst-compiled job without allocating,
    /// and shared rather than written twice because the fiddly part - not visiting the four corners twice -
    /// is exactly the part that is easy to get subtly wrong in one copy and not the other.
    /// </summary>
    public static class CellRing
    {
        /// <summary>How many cells the ring holds. One at the centre, then eight per step out.</summary>
        public static int Count(int ring) => ring == 0 ? 1 : 8 * ring;

        /// <summary>
        /// The <paramref name="index"/>th cell of the ring: the top row, then the bottom, then the two sides
        /// with the corners left out because the rows already took them.
        /// </summary>
        public static int2 At(int2 centre, int ring, int index)
        {
            if (ring == 0)
            {
                return centre;
            }

            int row = 2 * ring + 1;

            if (index < row)
            {
                return centre + new int2(index - ring, ring);
            }

            if (index < 2 * row)
            {
                return centre + new int2(index - row - ring, -ring);
            }

            int side = index - 2 * row;
            int column = side < ring * 2 - 1 ? -ring : ring;
            int offset = side < ring * 2 - 1 ? side : side - (ring * 2 - 1);

            return centre + new int2(column, offset - ring + 1);
        }
    }
}
