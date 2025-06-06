using System.Collections.Generic;
using UnityEngine;

namespace HCore.Shapes
{
    public struct Triangle : IShape
    {
        private const float ONE_DIV_TREE = 0.3333f; 

        public Vector2 Min { get; }
        public Vector2 Max { get; }
        public Vector2 Center { get; }

        public Line BorderLines1 { get; }
        public Line BorderLines2 { get; }
        public Line BorderLines3 { get; }
        private bool CenterSide1 { get; }
        private bool CenterSide2 { get; }
        private bool CenterSide3 { get; }

        private Vector2 ThrdPoint { get; }

        public Triangle(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            Min = new Vector2(
                Mathf.Min(p1.x, p2.x, p3.x),
                Mathf.Min(p1.y, p2.y, p3.y)
                );
            Max = new Vector2(
                Mathf.Max(p1.x, p2.x, p3.x),
                Mathf.Max(p1.y, p2.y, p3.y)
                );
            Center = new Vector2(
                (p1.x + p2.x + p3.x) * ONE_DIV_TREE,
                (p1.y + p2.y + p3.y) * ONE_DIV_TREE
                );

            BorderLines1 = new Line(p1, p2);
            BorderLines2 = new Line(p2, p3);
            BorderLines3 = new Line(p3, p1);

            CenterSide1 = BorderLines1.Side(Center);
            CenterSide2 = BorderLines2.Side(Center);
            CenterSide3 = BorderLines3.Side(Center);
            
            ThrdPoint = p3;
        }
        public Triangle(ref Triangle other, Vector2 offset)
        {
            Min = other.Min + offset;
            Max = other.Max + offset;
            Center = other.Center + offset;

            BorderLines1 = new Line(other.BorderLines1, offset);
            BorderLines2 = new Line(other.BorderLines2, offset);
            BorderLines3 = new Line(other.BorderLines3, offset);

            CenterSide1 = other.CenterSide1;
            CenterSide2 = other.CenterSide2;
            CenterSide3 = other.CenterSide3;

            ThrdPoint = other.ThrdPoint + offset;
        }

        public readonly override string ToString() => $"Triangle ({BorderLines1.RPoint}, {BorderLines1.LPoint}, {ThrdPoint})";

        public IShape UpdatePosition(Vector2 newMin) => new Triangle(ref this, newMin - Min);

        public readonly bool Contains(Vector2 point)
        {
            return BorderLines1.Side(point) == CenterSide1
            && BorderLines2.Side(point) == CenterSide2
            && BorderLines3.Side(point) == CenterSide3;
        }

        public readonly Vector2 GetPointClosestTo(Vector2 point)
        {
            if (Contains(point))
                return point;

            return GetBorderPointClosestTo(point);
        }
        public readonly Vector2 GetBorderPointClosestTo(Vector2 point)
        {
            var closestPoint = BorderLines1.GetPointClosestTo(point);
            var minSqrDist = (closestPoint - point).sqrMagnitude;

            var cPoint = BorderLines2.GetPointClosestTo(point);
            var sqrDist = (cPoint - point).sqrMagnitude;
            if (sqrDist < minSqrDist)
            {
                closestPoint = cPoint;
                minSqrDist = sqrDist;
            }

            cPoint = BorderLines3.GetPointClosestTo(point);
            sqrDist = (cPoint - point).sqrMagnitude;
            if (sqrDist < minSqrDist)
            {
                closestPoint = cPoint;
            }

            return closestPoint;
        }

        public readonly IEnumerable<Vector2> GetBorderPoints()
        {
            yield return BorderLines1.LPoint;
            yield return BorderLines1.RPoint;
            yield return ThrdPoint;
        }
        public readonly IEnumerable<Line> BorderLines
        {
            get
            {
                yield return new Line(BorderLines1.LPoint, BorderLines1.RPoint);
                yield return new Line(BorderLines1.RPoint, ThrdPoint);
                yield return new Line(ThrdPoint, BorderLines1.LPoint);
            }
        }
    }
}