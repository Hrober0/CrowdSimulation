using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HCore.Extensions;
using UnityEngine;

namespace HCore.Shapes
{
    public struct Polygon : IShape
    {
        public Vector2 Min { get; }
        public Vector2 Max { get; }
        public Vector2 Center { get; }

        public Triangle[] Triangles { get; }

        public Polygon(IEnumerable<Vector2> points)
        {
            var pointsArray = points.ToArray();

            if (pointsArray.Length < 3)
            {
                Debug.LogError("Polygon cant be made from less then 3 points!");
                Center = Vector2.zero;
                Min = Vector2.zero;
                Max = Vector2.zero;
                Triangles = Array.Empty<Triangle>();
            }
            else
            {
                Triangles = Triangulate(pointsArray).ToArray();
                Min = Triangles[0].Min;
                Max = Triangles[0].Max;
                for (int i = 1; i < Triangles.Length; i++)
                {
                    Min = Vector2.Min(Min, Triangles[i].Min);
                    Max = Vector2.Max(Max, Triangles[i].Max);
                }
                Center = (Min + Max) * 0.5f;
            }
        }
        public Polygon(ref Polygon other, Vector2 offset)
        {
            Min = other.Min + offset;
            Max = other.Max + offset;
            Center = other.Center + offset;
            Triangles = new Triangle[other.Triangles.Length];
            for (int i = 0; i < Triangles.Length; i++)
            {
                Triangles[i] = new Triangle(ref other.Triangles[i], offset);
            }
        }

        public readonly override string ToString() => $"Polygon (Min: {Min}, Max: {Max} Rects: {Triangles.Length})";

        public IShape UpdatePosition(Vector2 newMin) => new Polygon(ref this, newMin - Min);

        public readonly bool Contains(Vector2 point)
        {
            for (int i = 0; i < Triangles.Length; i++)
            {
                if (Triangles[i].Contains(point))
                    return true;
            }
            return false;
        }

        public readonly Vector2 GetPointClosestTo(Vector2 point)
        {
            if (Triangles[0].Contains(point))
                return point;

            var closestPoint = Triangles[0].GetPointClosestTo(point);
            var minSqrDist = (closestPoint - point).sqrMagnitude;
            for (int i = 1; i < Triangles.Length; i++)
            {
                if (Triangles[i].Contains(point))
                    return point;

                var cPoint = Triangles[i].GetPointClosestTo(point);
                var sqrDist = (cPoint - point).sqrMagnitude;
                if (sqrDist < minSqrDist)
                {
                    closestPoint = cPoint;
                    minSqrDist = sqrDist;
                }
            }
            return closestPoint;
        }

        public readonly IEnumerable<Vector2> GetBorderPoints()
        {
            var lines = new List<Line>();
            foreach (var triangle in Triangles)
            {
                foreach (var line in triangle.BorderLines)
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

            if (lines.Count == 0)
            {
                //Debug.LogWarning("No lines");
                yield break;
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
                    //Debug.Log($"{line} not added, duplicated {lines[index]} at {index}");
                    lines.RemoveAt(index);
                }
            }
        }
        public readonly IEnumerable<Line> BorderLines
        {
            get
            {
                Vector2? lp = null;
                Vector2 startPoint = Vector2.zero;
                foreach (var point in GetBorderPoints())
                {
                    if (lp.HasValue)
                        yield return new Line(lp.Value, point);
                    else
                        startPoint = point;
                    lp = point;
                }
                if (lp.HasValue)
                    yield return new Line(lp.Value, startPoint);
            }
        }


        public static IEnumerable<Triangle> Triangulate(Vector2[] polygon)
        {
            var indices = new List<int>();
            for (int i = 0; i < polygon.Length; i++)
            {
                indices.Add(i);
            }

            int tries = 1000 * polygon.Length;

            int index = 0;
            while (indices.Count > 3)
            {
                int prev = (index - 1 + indices.Count) % indices.Count;
                int next = (index + 1) % indices.Count;

                Vector2 p = polygon[indices[index]];
                Vector2 pPrev = polygon[indices[prev]];
                Vector2 pNext = polygon[indices[next]];

                if (IsEar(p, pPrev, pNext, polygon))
                {
                    yield return new Triangle(pPrev, p, pNext);
                    indices.RemoveAt(index);
                    index = prev;
                }
                else
                {
                    index = next;

                    tries--;
                    if (tries < 0)
                    {
                        Debug.LogWarning("Triangulation error");
                        break;
                    }
                }
            }

            yield return new Triangle(polygon[indices[0]], polygon[indices[1]], polygon[indices[2]]);
        }
        private static bool IsEar(Vector2 p, Vector2 prev, Vector2 next, IEnumerable<Vector2> polygon)
        {
            if (Area(prev, p, next) > 0)
            {
                foreach (var q in polygon)
                {
                    if (q != p && q != prev && q != next && IsInsideTriangle(q, prev, p, next))
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }
        private static bool IsInsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var total = Area(a, b, c);
            var p1 = Area(p, a, b);
            var p2 = Area(p, b, c);
            var p3 = Area(p, c, a);

            return Mathf.Abs(total - (p1 + p2 + p3)) < 1e-6;
        }
        private static float Area(Vector2 a, Vector2 b, Vector2 c)
        {
            return Mathf.Abs((a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y)) * 0.5f);
        }
    }
}