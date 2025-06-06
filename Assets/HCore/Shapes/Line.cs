using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace HCore.Shapes
{
    public struct Line : IShape
    {
        public const float EPSILON = 0.001f;

        public Line(Vector2 p1, Vector2 p2) : this(p1.x, p1.y, p2.x, p2.y) { }
        public Line(float p1x, float p1y, float p2x, float p2y)
        {
            if (p1x == p2x)
            {
                if (p1y < p2y)
                {
                    LPoint = new(p2x, p2y);
                    RPoint = new(p1x, p1y);
                    p1x += EPSILON;
                }
                else
                {
                    LPoint = new(p1x, p1y);
                    RPoint = new(p2x, p2y);
                    p2x += EPSILON;
                }
            }
            else if (p1x < p2x)
            {
                LPoint = new(p1x, p1y);
                RPoint = new(p2x, p2y);
            }
            else
            {
                LPoint = new(p2x, p2y);
                RPoint = new(p1x, p1y);
            }
            A = (p2y - p1y) / (p2x - p1x);
            B = p1y - A * p1x;

            Min = new(LPoint.x, Mathf.Min(LPoint.y, RPoint.y));
            Max = new(RPoint.x, Mathf.Max(LPoint.y, RPoint.y));
            Center = Min + (Max - Min) * 0.5f;
        }
        public Line(Vector2 p, float a, float length)
        {
            A = a;
            B = p.y - A * p.x;
            if (a > 0)
            {
                LPoint = p;
                RPoint = p + new Vector2(1, A).normalized * length;
            }
            else
            {
                RPoint = p;
                LPoint = p + new Vector2(1, A).normalized * length;
            }

            Min = new(LPoint.x, Mathf.Min(LPoint.y, RPoint.y));
            Max = new(RPoint.x, Mathf.Max(LPoint.y, RPoint.y));
            Center = Min + (Max - Min) * 0.5f;
        }
        public Line(ref Line other, Vector2 offset)
        {
            Min = other.Min + offset;
            Max = other.Max + offset;
            Center = other.Center + offset;
            A = other.A;
            B = other.B;
            LPoint = other.LPoint + offset;
            RPoint = other.RPoint + offset;
        }
        public Line(Line other, Vector2 offset)
        {
            Min = other.Min + offset;
            Max = other.Max + offset;
            Center = other.Center + offset;
            A = other.A;
            B = other.B;
            LPoint = other.LPoint + offset;
            RPoint = other.RPoint + offset;
        }

        public Vector2 Min { get; }
        public Vector2 Max { get; }
        public Vector2 Center { get; }

        public Vector2 LPoint { get; }
        public Vector2 RPoint { get; }
        public float A { get; }
        public float B { get; }

        public readonly override string ToString() => $"Line ({LPoint}, {RPoint})";

        public IShape UpdatePosition(Vector2 newMin) => new Line(ref this, newMin - Min);

        public readonly bool Contains(Vector2 point) => Contains(point.x, point.y);
        public readonly bool Contains(float px, float py)
        {
            return px + EPSILON >= LPoint.x && px - EPSILON <= RPoint.x
                && (Mathf.Abs(CalcY(px) - py) < EPSILON || LPoint.x == RPoint.x && py >= Min.y && py <= Max.y);
        }

        public readonly Vector2 GetPointClosestTo(Vector2 point)
        {
            float x = A == 0
                ? point.x
                : (A * (point.y + point.x / A - B)) / (A * A + 1);

            if (x < LPoint.x)
                return LPoint;

            if (x > RPoint.x)
                return RPoint;

            return new(x, CalcY(x));
        }
        public readonly Vector2 GetPointClosestToNoLimits(Vector2 point)
        {
            float x = A == 0
                ? point.x
                : (A * (point.y + point.x / A - B)) / (A * A + 1);

            return new(x, CalcY(x));
        }

        public readonly bool Intersects(Line other)
        {
            var r = this.LPoint - this.RPoint;
            var s = other.LPoint - other.RPoint;
            var rxs = r.x * s.y - r.y * s.x;

            // Check if rxs is zero, meaning the lines are parallel or collinear
            if (math.abs(rxs) < 1e-6f)
            {
                //intersection = float2.zero;
                return false;
            }

            var p2p1 = other.RPoint - this.RPoint;
            float t = (p2p1.x * s.y - p2p1.y * s.x) / rxs;
            float u = (p2p1.x * r.y - p2p1.y * r.x) / rxs;

            // Check if the intersection lies on both line segments (0 <= t, u <= 1)
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
            {
                //intersection = p1 + t * r;
                return true;
            }

            //intersection = float2.zero;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly float CalcY(float x) => x * A + B;

        /// <summary>
        /// Returns true if point is on bottom side
        /// If line if vertical, return true when point is on right side
        /// </summary>
        public readonly bool Side(Vector2 point) => (point.x - LPoint.x) * (RPoint.y - LPoint.y) > (point.y - LPoint.y) * (RPoint.x - LPoint.x);

        public readonly bool Equals(ref Line other)
            => Mathf.Approximately(LPoint.x, other.LPoint.x)
            && Mathf.Approximately(LPoint.y, other.LPoint.y)
            && Mathf.Approximately(RPoint.x, other.RPoint.x)
            && Mathf.Approximately(RPoint.y, other.RPoint.y);

        public readonly IEnumerable<Vector2> GetBorderPoints()
        {
            yield return LPoint;
            yield return RPoint;
        }
        public readonly IEnumerable<Line> BorderLines
        {
            get
            {
                yield return this;
            }
        }
    }
}