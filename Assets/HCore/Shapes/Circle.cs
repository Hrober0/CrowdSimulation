using System;
using System.Collections.Generic;
using UnityEngine;

namespace HCore.Shapes
{
    public struct Circle : IEquatable<Circle>, IShape
    {
        private const int STROKES = 8;
        private const float ANGLE_INC = 360f / STROKES;

        public Circle(float centerX, float centerY, float radius) : this(new Vector2(centerX, centerY), radius) { }
        public Circle(Vector2 center, float radius)
        {
            Center = center;
            Radius = radius;
            SquerRadius = radius * radius;
            Min = center - Vector2.one * radius;
            Max = center + Vector2.one * radius;
        }
        public Circle(ref Circle other, Vector2 offset)
        {
            Center = other.Center + offset;
            Radius = other.Radius;
            Min = other.Min + offset;
            Max = other.Max + offset;
            SquerRadius = other.SquerRadius;
        }

        public IShape UpdatePosition(Vector2 newMin) => new Circle(ref this, newMin - Min);

        public Vector2 Center { get; }
        public Vector2 Min { get; }
        public Vector2 Max { get; }

        public float Radius { get; }

        private float SquerRadius { get; }

        public readonly override string ToString() => $"Circle (c:{Center}, r: {Radius})";

        public readonly bool Equals(Circle other)
            => Mathf.Approximately(Center.x, other.Center.x)
            && Mathf.Approximately(Center.y, other.Center.y)
            && Mathf.Approximately(Radius, other.Radius);

        public readonly bool Contains(Vector2 point) => Vector2.SqrMagnitude(point - Center) <= SquerRadius;

        public readonly Vector2 GetPointClosestTo(Vector2 point)
        {
            float vX = point.x - Center.x;
            float vY = point.y - Center.y;
            float sqrDist = vX * vX + vY * vY;
            if (sqrDist <= SquerRadius)
                return point;
            float mul = Radius / Mathf.Sqrt(sqrDist);
            return new Vector2(Center.x + vX * mul, Center.y + vY * mul);
        }

        public readonly IEnumerable<Vector2> GetBorderPoints()
        {
            float angle = 0;
            for (int i = 0; i < STROKES; i++, angle -= ANGLE_INC)
            {
                float rads = Mathf.Deg2Rad * angle;
                float x = Mathf.Cos(rads) * Radius;
                float y = Mathf.Sin(rads) * Radius;
                yield return new(Center.x + x, Center.y + y);
            }
        }
        public readonly IEnumerable<Line> BorderLines
        {
            get
            {
                float angle = ANGLE_INC;
                Vector2 lastPoint = new(Center.x, Center.y + Radius);
                for (int i = 0; i < STROKES; i++, angle += ANGLE_INC)
                {
                    float rads = Mathf.Deg2Rad * angle;
                    float x = Mathf.Cos(rads) * Radius;
                    float y = Mathf.Sin(rads) * Radius;
                    Vector2 p = new(Center.x + x, Center.y + y);
                    yield return new Line(lastPoint, p);
                    lastPoint = p;
                }
            }
        }
    }
}