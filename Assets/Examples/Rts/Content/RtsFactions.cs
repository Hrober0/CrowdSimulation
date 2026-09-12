using Rts;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Which side is which, in this example (design §14 step 13).
    ///
    /// The same bargain as <see cref="ItemCatalog"/> and <see cref="WorldObjectCatalog"/>: `Rts` knows only
    /// that a <see cref="Faction"/> is a number and that two different numbers are a quarrel, and the game
    /// layer decides that zero is the side the mouse belongs to and that one wears red.
    ///
    /// Two sides here, but nothing below is written for two - the colour table falls back, and everything
    /// else is a plain id. A third faction is a row.
    /// </summary>
    public static class RtsFactions
    {
        /// <summary>The side the player builds for. Also what a building with no faction counts as.</summary>
        public const byte PLAYER = 0;

        public const byte RAIDERS = 1;

        public static string Name(byte id) => id switch
        {
            PLAYER => "Yours",
            RAIDERS => "Raiders",
            _ => $"Faction {id}",
        };

        /// <summary>
        /// A tint for the side, used to mark an agent's view. Deliberately not the same hue as any building:
        /// a faction colour has to be readable against whatever the building it is standing next to is.
        /// </summary>
        public static Color Colour(byte id) => id switch
        {
            PLAYER => new Color(0.35f, 0.70f, 0.95f),
            RAIDERS => new Color(0.90f, 0.30f, 0.30f),
            _ => new Color(0.85f, 0.75f, 0.35f),
        };
    }
}
