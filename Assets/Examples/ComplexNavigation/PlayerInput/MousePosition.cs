using UnityEngine;

namespace ComplexNavigation
{
    public static class MousePosition
    {
        public static Vector2 GetMousePosition()
        {
            return Camera.main.ScreenToWorldPoint(Input.mousePosition);
        }
    }
}
