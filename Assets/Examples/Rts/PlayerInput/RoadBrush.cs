using System.Collections.Generic;
using GridNav;
using Unity.Mathematics;

namespace Examples.Rts
{
    public enum RoadBrushMode
    {
        /// <summary>A road agents may cross in any direction.</summary>
        TwoWay,

        /// <summary>A road that may not be walked back up. The direction is the direction of the drag.</summary>
        OneWay,

        /// <summary>Back to plain ground: no discount, no flags, no restrictions.</summary>
        Erase,
    }

    /// <summary>
    /// Painting roads onto the grid (design §3, §8). Pure logic - what to enqueue for a cell - so that the
    /// mouse handling on top of it stays about the mouse.
    ///
    /// A road is three things at once: cheaper to walk (§3's "fast lane" is a cost, not a special case in the
    /// pathfinder), <see cref="CellFlags.NoIdle"/> so nobody parks on it (§6), and optionally a direction.
    ///
    /// **The discount is a delta, like every other cost contribution.** The grid keeps an exact sum so that
    /// removing one of several things on a cell restores what was there before (§3); a road that *set* the
    /// cost would destroy whatever the terrain contributed and could never be undone. The <see cref="CellFlags.Road"/>
    /// flag is what makes the delta idempotent - a cell already carrying it is not discounted twice.
    /// </summary>
    public static class RoadBrush
    {
        /// <summary>
        /// Subtracted from a cell's cost when it becomes a road. Ground has to be laid down at least this
        /// expensive for a road to be worth anything, which the demo world does.
        /// </summary>
        public const int ROAD_DISCOUNT = 6;

        /// <summary>
        /// What painting one cell should enqueue. <paramref name="current"/> is the cell as it stands, which
        /// is what makes laying a road over an existing one adjust its direction rather than discount it
        /// again.
        /// </summary>
        public static void Paint(
            List<GridEdit> edits,
            int2 cell,
            CellData current,
            RoadBrushMode mode,
            Direction direction)
        {
            bool isRoad = current.Has(CellFlags.Road);

            if (mode == RoadBrushMode.Erase)
            {
                if (!isRoad)
                {
                    return;
                }

                edits.Add(GridEdit.CostDelta(cell, ROAD_DISCOUNT));
                edits.Add(GridEdit.RemoveFlags(cell, CellFlags.Road | CellFlags.NoIdle));
                edits.Add(GridEdit.SetExits(cell, DirectionUtils.ALL_EXITS));
                return;
            }

            if (!isRoad)
            {
                // Never below zero: the cost sum is unsigned, and a cheaper-than-free road is not a thing.
                edits.Add(GridEdit.CostDelta(cell, -math.min(ROAD_DISCOUNT, current.CostSum)));
                edits.Add(GridEdit.AddFlags(cell, CellFlags.Road | CellFlags.NoIdle));
            }

            edits.Add(GridEdit.SetExits(cell, ExitsFor(mode, direction)));
        }

        /// <summary>
        /// One-way forbids the reverse and nothing else. A mask permitting only the direction of travel would
        /// also forbid stepping off the road sideways, so agents could join a one-way road and never leave
        /// it - the feature meant to unjam corridors would strand everyone who used one.
        /// </summary>
        public static byte ExitsFor(RoadBrushMode mode, Direction direction) =>
            mode == RoadBrushMode.OneWay
                ? DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, DirectionUtils.Opposite(direction))
                : DirectionUtils.ALL_EXITS;
    }
}
