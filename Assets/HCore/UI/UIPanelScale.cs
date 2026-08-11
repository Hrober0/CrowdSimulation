using UnityEngine;
using UnityEngine.UIElements;

namespace HCore.UI
{
    /// <summary>
    /// Panel-level sizing: how big the UI is on screen, and where on screen a piece of it ended up.
    ///
    /// The scale is set from here rather than left to the PanelSettings asset. A panel that is built in code
    /// but reads its size from an asset field has its two halves in different places, and an asset that is
    /// fresh, regenerated, or edited outside the editor quietly falls back to a fixed pixel size - which is
    /// the same panel taking up half the window at 720p and a corner of it at 4K. Applying it on load costs
    /// nothing, is the same values every time, and means the code that draws the UI also decides its size.
    /// </summary>
    public static class UIPanelScale
    {
        /// <summary>
        /// The window size at which a px in <see cref="UIStyledElements"/> is a px on screen. Everything
        /// scales from here: a window half this size draws the UI at half size, twice this size at double.
        ///
        /// The sizes over there are written at double, against this 4K reference, rather than at their face
        /// value against a 1080p one. Same result on screen either way - this is the single number to change
        /// to resize the whole UI, and keeping the reference above the common resolutions means doing so
        /// scales the UI down from the sizes in the code rather than magnifying them.
        /// </summary>
        public const int REFERENCE_WIDTH = 3840;
        public const int REFERENCE_HEIGHT = 2160;

        /// <summary>
        /// How much of the scale comes from the window's width against its height. Half of each keeps the
        /// panel the same share of the window whichever of the two the player changes.
        /// </summary>
        private const float MATCH_WIDTH_TO_HEIGHT = 0.5f;

        /// <summary>
        /// Makes the document's panel scale with the window instead of being a fixed number of pixels.
        /// Call before building the UI, in <c>OnEnable</c>.
        /// </summary>
        public static void ScaleWithScreen(UIDocument document)
        {
            if (document != null)
            {
                ScaleWithScreen(document.panelSettings);
            }
        }

        public static void ScaleWithScreen(PanelSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = MATCH_WIDTH_TO_HEIGHT;
        }

        /// <summary>
        /// An element's rectangle in screen pixels, measured from the bottom left - the space
        /// <see cref="Camera.WorldToScreenPoint"/> answers in, so that the two can be compared.
        ///
        /// False until the element has been laid out, and once it has left the panel.
        /// </summary>
        public static bool TryGetScreenRect(VisualElement element, out Rect rect)
        {
            rect = default;

            IPanel panel = element?.panel;
            if (panel == null)
            {
                return false;
            }

            // An element's worldBound is in panel space, which is screen space divided by the panel scale.
            // The panel's own root covers the screen, so its height is that scale, whatever set it.
            float panelHeight = panel.visualTree.worldBound.height;
            if (panelHeight <= 0f)
            {
                return false;
            }

            Rect bound = element.worldBound;
            if (bound.width <= 0f || bound.height <= 0f)
            {
                return false;
            }

            float toScreen = Screen.height / panelHeight;

            // Panel space counts y downwards from the top of the screen, screen space upwards from the bottom.
            rect = new Rect(
                bound.x * toScreen,
                Screen.height - bound.yMax * toScreen,
                bound.width * toScreen,
                bound.height * toScreen);
            return true;
        }
    }
}
