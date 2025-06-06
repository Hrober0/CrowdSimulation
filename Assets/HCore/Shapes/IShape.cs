using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace HCore.Shapes
{
    public interface IShape : IOutline
    {
        Vector2 Min { get; }
        Vector2 Max { get; }
        Vector2 Center { get; }

        IShape UpdatePosition(Vector2 newMin);

        bool Contains(Vector2 point);
        Vector2 GetPointClosestTo(Vector2 point);

        /// <summary>
        /// Lines in counterclock wise order
        /// </summary>
        IEnumerable<Line> BorderLines { get; }
        
        string ToString();
    }

    public static class ShapeExtensions
    {
        public static bool Intersects(this IShape shape, IShape other, bool checkRect = true)
        {
            if (checkRect && !shape.IntersectsRect(other))
                return false;

            if (other.Contains(shape.Center) || shape.Contains(other.Center))
                return true;

            foreach (var thisLine in shape.BorderLines)
            {
                foreach (var otherLine in other.BorderLines)
                {
                    if (thisLine.Intersects(otherLine))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IntersectsRect(this IShape shape, IShape other)
            => shape.Min.x <= other.Max.x && shape.Max.x >= other.Min.x
            && shape.Min.y <= other.Max.y && shape.Max.y >= other.Min.y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 Size(this IShape shape) => shape.Max - shape.Min;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrDistance(this IShape shape, Vector2 point) => (shape.GetPointClosestTo(point) - point).sqrMagnitude;

        public static Vector2 GetBorderPointClosestTo(this IShape shape, Vector2 point)
        {
            var borderLines = shape.BorderLines.GetEnumerator();
            if (borderLines.MoveNext())
            {
                var pos = borderLines.Current.GetPointClosestTo(point);
                var dist = (pos - point).sqrMagnitude;
                while (borderLines.MoveNext())
                {
                    var newPos = borderLines.Current.GetPointClosestTo(point);
                    var newDist = (newPos - point).sqrMagnitude;
                    if (newDist < dist)
                    {
                        pos = newPos;
                        dist = newDist;
                    }
                }
                return pos;
            }
            else
            {
                return point;
            }
        }
    }
}