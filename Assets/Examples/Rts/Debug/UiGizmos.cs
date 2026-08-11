using HCore;
using UnityEngine;

namespace Examples.Rts
{
    /// <summary>
    /// Keeps the debug gizmos out from under the panel.
    ///
    /// The editor draws gizmos after the camera and after the UI, so a cost heat map prints itself straight
    /// over the text the panel exists to show, and no sorting order on the panel changes that. The gizmos are
    /// the side that can move, so they ask where the panel is and skip that rectangle.
    ///
    /// Game view only. The panel is not drawn in the scene view, so culling there would cut a hole in the
    /// overlay to avoid something that is not on screen.
    /// </summary>
    public static class UiGizmos
    {
        /// <summary>
        /// The rectangle to leave clear, empty when there is nothing to avoid.
        ///
        /// Read it once per <c>OnDrawGizmos</c> and pass it to <see cref="Hides"/>: a grid window is
        /// thousands of cells, and each one asking the bus for the same rect would be thousands of queries a
        /// frame for an answer that cannot change inside a single draw.
        /// </summary>
        public static Rect ClearRect()
        {
            Camera camera = Camera.current;
            if (camera == null || camera.cameraType != CameraType.Game)
            {
                return Rect.zero;
            }

            return EventBus.InvokeWithResult<IUiScreenRectQuery, Rect>(q => q.UiScreenRect(), Rect.zero);
        }

        /// <summary>Whether a gizmo at this world position would land behind the panel.</summary>
        public static bool Hides(in Rect clearRect, Vector3 worldPosition)
        {
            if (clearRect.width <= 0f)
            {
                return false;
            }

            Camera camera = Camera.current;
            if (camera == null)
            {
                return false;
            }

            Vector3 screen = camera.WorldToScreenPoint(worldPosition);

            // Behind the camera projects to the same pixel as in front of it - a point there is not on
            // screen at all, so it is not something the panel is covering.
            return screen.z > 0f && clearRect.Contains(new Vector2(screen.x, screen.y));
        }

        /// <summary>Single-gizmo form, for a drawer with one position to place rather than a grid of them.</summary>
        public static bool Hides(Vector3 worldPosition) => Hides(ClearRect(), worldPosition);
    }
}
