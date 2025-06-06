using UnityEngine;

namespace HCore.UI
{
    public static class UIColors
    {
        #region unity colors

        public static Color EditorButton { get; } = new Color(0.4f, 0.4f, 0.4f);
        public static Color EditorBackground { get; } = new Color(0.22f, 0.22f, 0.22f);
        public static Color EditorContent { get; } = new Color(0.25f, 0.25f, 0.25f);
        public static Color EditorBorder { get; } = new Color(0.14f, 0.14f, 0.14f);

        #endregion

        public static Color TextWhite { get; } = new Color32(193, 193, 193, 255);
        public static Color TextRed { get; } = new Color32(190, 105, 105, 255);
    }
}