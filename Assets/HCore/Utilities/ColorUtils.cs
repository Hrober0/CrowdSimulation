using UnityEngine;

namespace HCore
{
    public class ColorUtils
    {
        private static readonly Color[] _palette =
        {
            // Reds
            new Color(1f, 0.4f, 0.4f),   // light red
            new Color(1f, 0f, 0f),       // red
            new Color(0.6f, 0f, 0f),     // dark red

            // Oranges
            new Color(1f, 0.7f, 0.3f),   // light orange
            new Color(1f, 0.4f, 0f),     // orange
            new Color(0.6f, 0.3f, 0f),   // dark orange

            // Yellows
            new Color(1f, 1f, 0.2f),     // light yellow
            new Color(0.6f, 0.6f, 0f),   // yellow
            new Color(0.3f, 0.3f, 0f),   // dark yellow

            // Greens
            new Color(0.4f, 1f, 0.4f),   // light green
            new Color(0f, 0.7f, 0f),     // green
            new Color(0f, 0.2f, 0f),     // dark green

            // Cyans / Teals
            new Color(0.4f, 1f, 1f),     // light cyan
            new Color(0f, 0.6f, 1f),     // cyan
            new Color(0f, 0.4f, 0.4f),   // teal

            // Blues
            new Color(0.4f, 0.4f, 1f),   // light blue
            new Color(0f, 0f, 1f),       // blue
            new Color(0f, 0f, 0.4f),     // dark blue

            // Purples / Violets
            new Color(0.8f, 0.4f, 1f),   // light violet
            new Color(.6f, 0f, .8f),     // violet
            new Color(0.4f, 0f, 0.4f),   // dark violet

            // Magentas / Pinks
            new Color(1f, 0.4f, 1f),     // pink
            new Color(.7f, 0f, .7f),     // magenta
            new Color(.4f, 0f, .4f),     // dark magenta
        };

        public static Color GetColor(int index)
        {
            if (index < 0) index = -index;
            return _palette[index % _palette.Length];
        }
    }
}