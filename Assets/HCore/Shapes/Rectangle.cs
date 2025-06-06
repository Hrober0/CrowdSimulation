using System;
using System.Collections.Generic;
using UnityEngine;

namespace HCore.Shapes
{
    public struct Rectangle : IEquatable<Rectangle>, IShape
    {
        public static readonly Rectangle Empty;

        public Rectangle(float left, float bottom, float width, float height) : this(new(left, bottom), new(width, height)) { }
        public Rectangle(Vector2 min, Vector2 size)
        {
            Min = min;
            Max = min + size;
            Center = (Max + Min) * 0.5f;
        }
        public Rectangle(ref Rectangle other, Vector2 offset)
        {
            Min = other.Min + offset;
            Max = other.Max + offset;
            Center = other.Center + offset;
        }

        public static Rectangle CreateByCenterSize(Vector2 center, float radius)
        {
            var offset = Vector2.one * radius;
            return new Rectangle(center - offset, offset + offset);
        }
        public static Rectangle CreateByMinMax(Vector2 v1, Vector2 v2)
        {
            var min = Vector2.Min(v1, v2);
            var max = Vector2.Max(v1, v2);
            return new Rectangle(min, max - min);
        }

        public IShape UpdatePosition(Vector2 newMin) => new Rectangle(ref this, newMin - Min);

        public Vector2 Min { get; }
        public Vector2 Max { get; }
        public Vector2 Center { get; }

        public readonly float Width => Max.x - Min.x;
        public readonly float Height => Max.y - Min.y;
        public readonly Vector2 Size => Max - Min;

        public readonly bool IsEmpty => Equals(Empty);

        public readonly override string ToString() => $"Rectangle ({Min}, {Max})";

        public readonly bool Equals(Rectangle other)
            => Mathf.Approximately(Min.x, other.Min.x)
            && Mathf.Approximately(Min.y, other.Min.y)
            && Mathf.Approximately(Max.x, other.Max.x)
            && Mathf.Approximately(Max.y, other.Max.y);

        public readonly bool Contains(Vector2 point) => point.x >= Min.x && point.x <= Max.x && point.y >= Min.y && point.y <= Max.y;
        public readonly bool Contains(IShape other)
        {
            return other.Max.x <= Max.x
                && other.Min.x >= Min.x
                && other.Max.y <= Max.y
                && other.Min.y >= Min.y;
        }

        /// <summary>
        /// Creates a rectangle representing the intersection of this and the specified rectangle.
        /// </summary>
        /// <param name="other">The rectangle to intersect.</param>
        /// <returns>
        /// A <see cref="Rectangle"/> value which represents the overlapped area of this and <c>other</c>.
        /// </returns>
        public readonly Rectangle Intersect(Rectangle other)
        {
            var left = Mathf.Max(Min.x, other.Min.x);
            var bot = Mathf.Max(Min.y, other.Min.y);
            var right = Mathf.Min(Max.x, other.Max.x);
            var top = Mathf.Min(Max.y, other.Max.y);

            return right > left && top > bot ? new Rectangle(left, bot, right - left, top - bot) : Empty;
        }
        public readonly bool TryIntersect(Rectangle other, out Rectangle intersection, float epsilon = 0)
        {
            var left = Mathf.Max(Min.x, other.Min.x);
            var bot = Mathf.Max(Min.y, other.Min.y);
            var right = Mathf.Min(Max.x, other.Max.x);
            var top = Mathf.Min(Max.y, other.Max.y);

            var intersect = right - left > epsilon && top - bot > epsilon;
            intersection = intersect ? new Rectangle(left, bot, right - left, top - bot) : Empty;
            return intersect;
        }

        public readonly Vector2 GetPointClosestTo(Vector2 point)
        {
            float closestX = point.x;
            float closestY = point.y;

            if (closestX < Min.x)
                closestX = Min.x;
            else if (closestX > Max.x)
                closestX = Max.x;

            if (closestY < Min.y)
                closestY = Min.y;
            else if (closestY > Max.y)
                closestY = Max.y;

            return new Vector2(closestX, closestY);
        }

        public readonly IEnumerable<Vector2> GetBorderPoints()
        {
            yield return Min;
            yield return new(Max.x, Min.y);
            yield return Max;
            yield return new(Min.x, Max.y);
        }
        public readonly IEnumerable<Line> BorderLines
        {
            get
            {
                yield return new Line(Min, new Vector2(Max.x, Min.y));
                yield return new Line(new Vector2(Max.x, Min.y), Max);
                yield return new Line(Max, new Vector2(Min.x, Max.y));
                yield return new Line(new Vector2(Min.x, Max.y), Min);
            }
        }

        public readonly IEnumerable<Rectangle> Subtract(Rectangle subtrahend)
        {
            //-------------------------
            //|          A            |
            //|-----------------------|
            //|  B  |   hole    |  C  |
            //|-----------------------|
            //|          D            |
            //-------------------------

            if (!subtrahend.TryIntersect(this, out subtrahend))
            {
                yield return this;
                yield break;
            }

            //A
            var heightA = this.Max.y - subtrahend.Max.y;
            if (heightA > 0)
                yield return new Rectangle(
                    this.Min.x,
                    subtrahend.Max.y,
                    this.Width,
                    heightA);

            //B
            var widthB = subtrahend.Min.x - this.Min.x;
            if (widthB > 0)
                yield return new Rectangle(
                    this.Min.x,
                    subtrahend.Min.y,
                    widthB,
                    subtrahend.Height);

            //C
            var widthC = this.Max.x - subtrahend.Max.x;
            if (widthC > 0)
                yield return new Rectangle(
                    subtrahend.Max.x,
                    subtrahend.Min.y,
                    widthC,
                    subtrahend.Height);

            //D
            var heightD = subtrahend.Min.y - this.Min.y;
            if (heightD > 0)
                yield return new Rectangle(
                    this.Min.x,
                    this.Min.y,
                    this.Width,
                    heightD);
        }

        public static bool TryMerge(Rectangle r1, Rectangle r2, out Rectangle merged, float epsilon = 0.1f)
        {
            if (r1.Min.y == r2.Min.y && r1.Max.y == r2.Max.y)
            {
                if (r1.Max.x - r2.Min.x < epsilon)
                {
                    // r1 to right
                    merged = new Rectangle(r1.Min.x, r1.Min.y, r2.Max.x - r1.Min.x, r1.Height);
                    return true;
                }
                else if (r1.Min.x - r2.Max.x < epsilon)
                {
                    // r1 to left
                    merged = new Rectangle(r2.Min.x, r2.Min.y, r1.Max.x - r2.Min.x, r2.Height);
                    return true;
                }
            }
            else if (r1.Min.x == r2.Min.x && r1.Max.x == r2.Max.x)
            {
                if (r1.Max.y - r2.Min.y < epsilon)
                {
                    // r1 to top
                    merged = new Rectangle(r1.Min.x, r1.Min.y, r1.Width, r2.Max.y - r1.Min.y);
                    return true;
                }
                else if (r1.Min.y - r2.Max.y < epsilon)
                {
                    // r1 to bottom
                    merged = new Rectangle(r2.Min.x, r2.Min.y, r2.Width, r1.Max.y - r2.Min.y);
                    return true;
                }
            }

            merged = Empty;
            return false;
        }
        public static List<Rectangle> Merge(IEnumerable<Rectangle> inputRectangles)
        {
            var rectsToMerge = new List<Rectangle>();
            foreach (var item in inputRectangles)
                if (!rectsToMerge.Contains(item))
                    rectsToMerge.Add(item);
            while (true)
            {
                bool wasAnyMerge = false;
                var removedRects = new List<Rectangle>();
                while (rectsToMerge.Count > 0)
                {
                    var areaRect = rectsToMerge[0];
                    rectsToMerge.RemoveAt(0);

                    bool merged = false;
                    foreach (var secondAreRect in rectsToMerge)
                    {
                        if (TryMerge(areaRect, secondAreRect, out var mergedRect))
                        {
                            rectsToMerge.Remove(secondAreRect);
                            rectsToMerge.Add(mergedRect);
                            merged = true;
                            wasAnyMerge = true;
                            break;
                        }
                    }
                    if (!merged)
                        removedRects.Add(areaRect);
                }

                if (!wasAnyMerge)
                    return removedRects;

                rectsToMerge.AddRange(removedRects);
            }
        }
    }

    public static class RectangleExtensions
    {
        public static Rectangle CreateRectangleBorder(this IShape shape) => Rectangle.CreateByMinMax(shape.Min, shape.Max);
        public static Rectangle CreateRectangleBorder(this IShape shape, float border)
        {
            var size = shape.Max - shape.Min;
            return new Rectangle(shape.Min.x - border, shape.Min.y - border, size.x + border * 2, size.y + border * 2);
        }
    }
}