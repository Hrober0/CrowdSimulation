using System.Collections.Generic;
using UnityEngine;

namespace HCore.Shapes
{
    public interface IOutline
    {
        IEnumerable<Vector2> GetBorderPoints();

        public static void DrawBorderGizmos(IEnumerable<Vector2> points)
        {
            Vector2? lp = null;
            Vector2 startPoint = Vector2.zero;
            foreach (var point in points)
            {
                if (lp.HasValue)
                {
                    DrawLineGizmos(lp.Value, point);
                }
                else
                {
                    startPoint = point;
                }

                lp = point;
            }
            
            if (lp.HasValue)
            {
                DrawLineGizmos(lp.Value, startPoint);
            }
        }
        public static void DrawBorderGizmos(IOutline outline) => DrawBorderGizmos(outline.GetBorderPoints());
        public static void DrawLineGizmos(Vector2 start, Vector2 end)
        {
            Gizmos.DrawLine(start, end);
        }

        public static void DrawBorder(IEnumerable<Vector2> points, Color? color = null, float? duration = null)
        {
            Vector2? lp = null;
            Vector2 startPoint = Vector2.zero;
            foreach (var point in points)
            {
                if (lp.HasValue)
                {
                    DrawLine(lp.Value, point, color, duration);
                }
                else
                {
                    startPoint = point;
                }

                lp = point;
            }
            
            if (lp.HasValue)
            {
                DrawLine(lp.Value, startPoint, color, duration);
            }
        }
        public static void DrawBorder(IOutline outline, Color? color = null, float? duration = null) => DrawBorder(outline.GetBorderPoints(), color, duration);
        public static void DrawLine(Vector2 start, Vector2 end, Color? color = null, float? duration = null)
        {
            if (color != null && duration != null)
            {
                Debug.DrawLine(start, end, color.Value, duration.Value);
            }
            else if (color != null)
            {
                Debug.DrawLine(start, end, color.Value);
            }
            else if (duration != null)
            {
                Debug.DrawLine(start, end, Color.white, duration.Value);
            }
            else
            {
                Debug.DrawLine(start, end);
            }
        }
    }

    public static class OutlineExtensions
    {
        public static void DrawBorderGizmos(this IOutline outline) => IOutline.DrawBorderGizmos(outline);
        
        public static void DrawBorder(this IOutline outline, Color? color = null, float? duration = null) => IOutline.DrawBorder(outline, color, duration);
    }
}