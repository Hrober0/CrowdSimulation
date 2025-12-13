using UnityEngine;

namespace ComplexNavigation
{
    public static class MousePosition
    {
        private static Camera _camera;

        public static Vector2 GetMousePosition()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            return _camera?.ScreenToWorldPoint(Input.mousePosition) ?? Vector2.zero;
        }

        public static Vector2 GetScreenPositionFromWorld(Vector2 worldPosition)
        {
            return _camera.WorldToScreenPoint(worldPosition);
        }
    }
}