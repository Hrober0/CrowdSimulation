using Rts;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// What the item ids of this example *mean* (design §7).
    ///
    /// <see cref="ItemId"/> is deliberately an opaque number in the simulation, so that adding an item never
    /// touches `Rts`. This is the other half of that bargain: the game layer says which number is bread. It
    /// is switch expressions over constants rather than a registry, so there is no state to initialise and
    /// nothing to get out of step with a save file.
    /// </summary>
    public static class ItemCatalog
    {
        public static ItemId Grain => new(1);

        public static ItemId Flour => new(2);

        public static ItemId Bread => new(3);

        public static ItemId Wood => new(4);

        public static ItemId Stone => new(5);

        public static ItemId Ore => new(6);

        /// <summary>Every item the example knows about, in display order.</summary>
        public static ItemId[] All => new[] { Grain, Flour, Bread, Wood, Stone, Ore };

        public static string Name(ItemId item) => item.Value switch
        {
            1 => "Grain",
            2 => "Flour",
            3 => "Bread",
            4 => "Wood",
            5 => "Stone",
            6 => "Ore",
            _ => "-",
        };

        public static Color Colour(ItemId item) => item.Value switch
        {
            1 => new Color(0.85f, 0.75f, 0.35f),
            2 => new Color(0.92f, 0.90f, 0.82f),
            3 => new Color(0.80f, 0.55f, 0.25f),
            4 => new Color(0.55f, 0.40f, 0.22f),
            5 => new Color(0.60f, 0.60f, 0.62f),
            6 => new Color(0.72f, 0.45f, 0.30f),
            _ => Color.grey,
        };
    }
}
