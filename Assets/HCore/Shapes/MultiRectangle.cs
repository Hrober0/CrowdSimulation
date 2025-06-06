using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HCore.Extensions;
using UnityEngine;

namespace HCore.Shapes
{
    public struct MultiRectangle : IShape
    {
        public MultiRectangle(IEnumerable<Rectangle> subRectangles)
        {
            SubRectangles = Rectangle.Merge(subRectangles).ToArray();

            if (SubRectangles.Length == 0)
            {
                Debug.LogWarning("Created multi rectangle with no rectangles");
            }
            
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            List<Line> lines = new();
            for (int i = 0; i < SubRectangles.Length; i++)
            {
                ref var rect = ref SubRectangles[i];
                lines.AddRange(rect.BorderLines);
                if (rect.Min.x <  minX) minX = rect.Min.x;
                if (rect.Min.y < minY) minY = rect.Min.y;
                if (rect.Max.x > maxX) maxX = rect.Max.x;
                if (rect.Max.y > maxY) maxY = rect.Max.y;
            }
            Min = new Vector2(minX, minY);
            Max = new Vector2(maxX, maxY);
            Center = (Max + Min) * 0.5f;
        }
        public MultiRectangle(ref MultiRectangle other, Vector2 offset)
        {
            Min = other.Min + offset;
            Max = other.Max + offset;
            Center = other.Center + offset;
            SubRectangles = new Rectangle[other.SubRectangles.Length];
            for (int i = 0; i < other.SubRectangles.Length; i++)
            {
                SubRectangles[i] = new Rectangle(ref other.SubRectangles[i], offset);
            }
        }

        public Rectangle[] SubRectangles { get; }

        public Vector2 Min { get; }
        public Vector2 Max { get; }
        public Vector2 Center { get; }

        public readonly override string ToString() => $"MultiRectangle (Min: {Min}, Max: {Max} Rects: {SubRectangles.Length})";

        public IShape UpdatePosition(Vector2 newMin) => new MultiRectangle(ref this, newMin - Min);

        public readonly bool Contains(Vector2 point)
        {
            for (int i = 0; i < SubRectangles.Length; i++)
                if (SubRectangles[i].Contains(point))
                    return true;
            return false;
        }

        public readonly IEnumerable<Vector2> GetBorderPoints()
        {
            var lines = new List<Line>();
            foreach (var rect in SubRectangles)
            {
                foreach (var line in rect.BorderLines)
                {
                    AddOrRemoveDuplicatedLine(line);
                }
            }

            var points = new HashSet<Vector2>();
            foreach (var line in lines)
            {
                points.Add(line.RPoint);
                points.Add(line.LPoint);
            }

            foreach (var point in points)
            {
                for (int ii = 0; ii < lines.Count; ii++)
                {
                    var line = lines[ii];
                    if (line.Contains(point) && line.RPoint != point && line.LPoint != point)
                    {
                        lines.RemoveAt(ii);
                        AddOrRemoveDuplicatedLine(new Line(line.LPoint, point));
                        AddOrRemoveDuplicatedLine(new Line(line.RPoint, point));
                        ii--;
                    }
                }
            }


            var l = lines[0];
            yield return l.LPoint;
            var cPoint = l.RPoint;
            
            var n = lines.Count;
            for (int i = 1; i < n; i++)
            {
                var ii = 1;
                for (; ii < lines.Count; ii++)
                {
                    var sl = lines[ii];
                    if (sl.RPoint == cPoint)
                    {
                        l = sl;
                        yield return cPoint;
                        cPoint = sl.LPoint;
                        lines.RemoveAt(ii);
                        goto SKIP;
                    }
                    if (sl.LPoint == cPoint)
                    {
                        l = sl;
                        yield return cPoint;
                        cPoint = sl.RPoint;
                        lines.RemoveAt(ii);
                        goto SKIP;
                    }
                }
                Debug.LogError($"No match for point {cPoint} in {lines.ElementsString()}");
                break;

            SKIP:;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int LineIndex(Line line)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Equals(ref line))
                    {
                        return i;
                    }
                }
                return -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void AddOrRemoveDuplicatedLine(Line line)
            {
                var index = LineIndex(line);
                if (index == -1)
                {
                    lines.Add(line);
                }
                else
                {
                    //Debug.Log($"{line} not added, duplicated at{index}");
                    lines.RemoveAt(index);
                }
            }
        }
        public readonly IEnumerable<Line> BorderLines
        {
            get
            {
                foreach (var rect in SubRectangles)
                {
                    foreach (var line in rect.BorderLines)
                    {
                        yield return line;
                    }
                }
            }
        }

        public readonly Vector2 GetPointClosestTo(Vector2 point)
        {
            Vector2 closePoint = SubRectangles[0].GetPointClosestTo(point);
            float squerDist = Vector2.SqrMagnitude(point - closePoint);
            for (int i = 1; i < SubRectangles.Length; i++)
            {
                Vector2 newClosePoint = SubRectangles[0].GetPointClosestTo(point);
                float newSquerDist = Vector2.SqrMagnitude(point - newClosePoint);
                if (newSquerDist < squerDist)
                {
                    squerDist = newSquerDist;
                    closePoint = newClosePoint;
                }
            }
            return closePoint;
        }

        /// <summary>
        /// Try minimalize number of rectangles to one, and return Rectangle when it was succesful
        /// otherwise return MultiRectangles
        /// </summary>
        public static IShape CreateMultiOrSampleRectangle(IEnumerable<Rectangle> rectangles)
        {
            var multiRectangle = new MultiRectangle(rectangles);
            return multiRectangle.SubRectangles.Length > 1 ? multiRectangle : new Rectangle(multiRectangle.Min, multiRectangle.Size());
        }
    }
}