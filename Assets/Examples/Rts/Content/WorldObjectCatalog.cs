using Rts;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// What a world object looks like, per <see cref="ObjectKind"/>.
    ///
    /// The same bargain as <see cref="ItemCatalog"/>: the simulation says only that a cell holds an object of
    /// some kind and what it costs to walk through, and the game layer decides that a tree is a green circle.
    /// Nothing in `Rts` knows a colour, and adding a kind of rock never touches it.
    /// </summary>
    public static class WorldObjectCatalog
    {
        public static Color Colour(ObjectKind kind) => kind switch
        {
            ObjectKind.Tree => new Color(0.28f, 0.62f, 0.32f),
            ObjectKind.Rock => new Color(0.55f, 0.55f, 0.58f),
            _ => new Color(0.7f, 0.7f, 0.7f),
        };

        /// <summary>
        /// Diameter, in cells. Under one on purpose: a tree fills its cell for the pathfinder, which is what
        /// its cost says, but drawing it edge to edge would hide the fact that it stands on exactly one cell.
        /// </summary>
        public static float Size(ObjectKind kind) => kind switch
        {
            ObjectKind.Tree => 0.8f,
            ObjectKind.Rock => 0.7f,
            _ => 0.6f,
        };
    }
}
