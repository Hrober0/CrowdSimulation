using UnityEngine;

namespace HCore.Extensions
{
    public static class VectorExtensions
    {
        public const float DEFAULT_Y = 0;
        public const float Y_2D_POSITION = 0;

        public static Vector3 To3D(this Vector2 vector2D) => new Vector3(vector2D.x, DEFAULT_Y, vector2D.y);
        public static Vector3 To3D(this Vector2 vector2D, float y) => new Vector3(vector2D.x, y, vector2D.y);

        public static Vector2 To2D(this Vector3 vector3D) => new Vector2(vector3D.x, vector3D.z);

        public static void DrawPoint(this Vector2 point, Color color, float? duration = null) => DrawPoint(point.To3D(Y_2D_POSITION), color, duration);
        public static void DrawPoint(this Vector3 point, Color color, float? duration = null)
        {
            if (duration != null)
            {
                Debug.DrawLine(point - Vector3.up,      point + Vector3.up,         color, duration.Value);
                Debug.DrawLine(point - Vector3.right,   point + Vector3.right,      color, duration.Value);
                Debug.DrawLine(point - Vector3.forward, point + Vector3.forward,    color, duration.Value);
            }
            else
            {
                Debug.DrawLine(point - Vector3.up,      point + Vector3.up,         color);
                Debug.DrawLine(point - Vector3.right,   point + Vector3.right,      color);
                Debug.DrawLine(point - Vector3.forward, point + Vector3.forward,    color);
            }
        }


        public static Vector2Int Floor(this Vector2 v) => new(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y));
        public static Vector2Int Round(this Vector2 v) => new(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y));

        public static Vector3Int Floor(this Vector3 v) => new(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z));
        public static Vector3Int Round(this Vector3 v) => new(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
    }
}