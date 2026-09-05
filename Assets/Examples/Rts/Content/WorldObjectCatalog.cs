using GridNav;
using Rts;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// What a world object looks like and what it costs to walk through, per <see cref="ObjectKind"/>.
    ///
    /// The same bargain as <see cref="ItemCatalog"/>: the simulation says only that a cell holds an object of
    /// some kind and what it costs to walk through, and the game layer decides that a tree is a green circle.
    /// Nothing in `Rts` knows a colour, and adding a kind of rock never touches it.
    /// </summary>
    public static class WorldObjectCatalog
    {
        /// <summary>
        /// A tree is expensive, not impassable (design §14 step 9).
        ///
        /// Crossing one costs <c>NavCost.STEP + 60 = 70</c> against a two-cell detour at around 20, so a lone
        /// tree is always walked around and a thick wood is cut through only when going round is some seven
        /// cells worse. That is the feel wanted, but it is not why the number is under
        /// <see cref="CellData.BLOCKED"/>: at 255 a stand of trees can seal a cell inside it, and a planting
        /// zone that has grown up walls its own planter out with nothing to report.
        ///
        /// It has to stay low enough that a plausible stack stays passable too. Four at 60 is 240 and still
        /// crossable; a fifth would tip the cell over the threshold, and then felling one bumps
        /// `PassabilityVersion` and re-scans the chunk's gates for every swing of an axe.
        /// </summary>
        public const ushort TREE_COST = 60;

        /// <summary>
        /// Walkable, but not free: agents step round a seam when stepping round is easy, and over it when it
        /// is not.
        ///
        /// Crossing costs 18 against 10 for open ground, so one cell of sidestep is worth it and a detour of
        /// two is not. That is what "reluctant" has to mean here - a seam is not an obstacle, and a number
        /// big enough to make traffic take the long way round a scattering of pebbles would be a wall in
        /// everything but name.
        ///
        /// It is not zero, and the cost of that is worth stating: at zero a seam bumped no version at any
        /// point in its life, so it never invalidated a flow field. At 8 it bumps `CostVersion` twice - when
        /// it appears and when it is mined out - and the fields whose window covers it rebuild. Twice in a
        /// seam's whole life, against traffic that no longer tramples straight through the ore field.
        /// </summary>
        public const ushort ORE_COST = 8;

        public static ushort Cost(ObjectKind kind) => kind switch
        {
            ObjectKind.Tree => TREE_COST,
            ObjectKind.Ore => ORE_COST,

            // A boulder is scenery that happens to be in the way, and nothing in the game removes one. It is
            // the last thing on the map that is still a wall.
            ObjectKind.Rock => CellData.BLOCKED,
            _ => CellData.BLOCKED,
        };

        public static Color Colour(ObjectKind kind) => kind switch
        {
            ObjectKind.Tree => new Color(0.28f, 0.62f, 0.32f),
            ObjectKind.Rock => new Color(0.55f, 0.55f, 0.58f),
            ObjectKind.Ore => new Color(0.72f, 0.45f, 0.30f),
            _ => new Color(0.7f, 0.7f, 0.7f),
        };

        /// <summary>
        /// Diameter, in cells. Under one on purpose: an object stands on exactly one cell, and drawing it
        /// edge to edge would hide which one. Ore is smaller again, because it is a thing agents walk over
        /// rather than round, and a marker the size of a cell reads as something that stops them.
        /// </summary>
        public static float Size(ObjectKind kind) => kind switch
        {
            ObjectKind.Tree => 0.8f,
            ObjectKind.Rock => 0.7f,
            ObjectKind.Ore => 0.45f,
            _ => 0.6f,
        };
    }
}
