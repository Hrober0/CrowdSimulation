using System.Collections.Generic;
using FluentAssertions;
using Navigation;
using NUnit.Framework;
using Unity.Mathematics;

namespace Tests.EditorTests.NavigationTests
{
    public class TriangleTests
    {
        [Test]
        public void TriangleEqual_ShouldReturnTrue()
        {
            Assert.AreEqual(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 0), new(0, 1), new(1, 1)));
        }

        [Test]
        public void TriangleEqual_ShouldReturnFalse()
        {
            Assert.AreNotEqual(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 0), new(0, 2), new(1, 1)));
        }

        [Test]
        public void TriangleFitting_ShouldReturnTrue()
        {
            Assert.IsTrue(Triangle.Fits(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 0), new(0, 1), new(1, 1))));

            Assert.IsTrue(Triangle.Fits(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 1), new(1, 1), new(0, 0))));

            Assert.IsTrue(Triangle.Fits(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 1), new(0, 0), new(1, 1))));
        }

        [Test]
        public void TriangleFitting_ShouldReturnFalse()
        {
            Assert.IsFalse(Triangle.Fits(
                new Triangle(new(0, 0), new(0, 1), new(1, 1)),
                new Triangle(new(0, 0), new(0, 2), new(1, 1))));
        }

        [Test]
        public void PointInsideTriangle_ShouldReturnTrue()
        {
            var a = new float2(0, 0);
            var b = new float2(1, 0);
            var c = new float2(0, 1);
            var p = new float2(0.25f, 0.25f);

            bool inside = Triangle.PointIn(p, a, b, c);
            inside.Should().BeTrue();
        }

        [Test]
        public void PointOutsideTriangle_ShouldReturnFalse()
        {
            var a = new float2(0, 0);
            var b = new float2(1, 0);
            var c = new float2(0, 1);
            var p = new float2(1f, 1f);

            bool inside = Triangle.PointIn(p, a, b, c);
            inside.Should().BeFalse();
        }

        [Test]
        public void PointOnEdge_ShouldReturnTrue()
        {
            var a = new float2(0, 0);
            var b = new float2(1, 0);
            var c = new float2(0, 1);
            var p = new float2(0.5f, 0f); // On edge AB

            bool inside = Triangle.PointIn(p, a, b, c);
            inside.Should().BeTrue();
        }

        [Test]
        public void TriangleArea2_CCW_IsPositive()
        {
            var a = new float2(0, 0);
            var b = new float2(1, 0);
            var c = new float2(1, 1);

            float area = Triangle.Area2(a, b, c);
            area.Should().BeGreaterThan(0f);
        }

        [Test]
        public void TriangleArea2_CW_IsNegative()
        {
            var a = new float2(0, 0);
            var b = new float2(2, 0);
            var c = new float2(1, -1);

            float area = Triangle.Area2(a, b, c);
            area.Should().BeLessThan(0f);
        }

        [Test]
        public void TriangleArea2_Colinear_IsZero()
        {
            var a = new float2(0, 0);
            var b = new float2(1, 1);
            var c = new float2(2, 2); // on the same line

            float area = Triangle.Area2(a, b, c);
            area.Should().BeInRange(-math.E, math.E);
        }

        #region Intersection

        [Test]
        public void Segments_DoNotIntersect_ShouldReturnFalse()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(1, 0);
            float2 b1 = new float2(0, 1);
            float2 b2 = new float2(1, 1);

            Triangle.EdgesIntersect(a1, a2, b1, b2).Should().BeFalse();
            Triangle.EdgesIntersect(a1, a2, b2, b1).Should().BeFalse();
            Triangle.EdgesIntersect(a2, a1, b1, b2).Should().BeFalse();
            Triangle.EdgesIntersect(a2, a1, b2, b1).Should().BeFalse();
        }

        [Test]
        public void Segments_Intersect_ShouldReturnTrue()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(2, 2);
            float2 b1 = new float2(0, 2);
            float2 b2 = new float2(2, 0);

            Triangle.EdgesIntersect(a1, a2, b1, b2).Should().BeTrue();
            Triangle.EdgesIntersect(a1, a2, b2, b1).Should().BeTrue();
            Triangle.EdgesIntersect(a2, a1, b1, b2).Should().BeTrue();
            Triangle.EdgesIntersect(a2, a1, b2, b1).Should().BeTrue();
        }

        [Test]
        public void Segments_TouchAtEndpoint_ShouldReturnFalse()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(1, 1);
            float2 b1 = new float2(1, 1);
            float2 b2 = new float2(2, 2);

            Triangle.EdgesIntersect(a1, a2, b1, b2).Should().BeFalse();
            Triangle.EdgesIntersect(a1, a2, b2, b1).Should().BeFalse();
            Triangle.EdgesIntersect(a2, a1, b1, b2).Should().BeFalse();
            Triangle.EdgesIntersect(a2, a1, b2, b1).Should().BeFalse();
        }

        [Test]
        public void Segments_AreCollinearAndOverlapping_ShouldReturnFalse()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(2, 2);
            float2 b1 = new float2(1, 1);
            float2 b2 = new float2(3, 3);

            Triangle.EdgesIntersect(a1, a2, b1, b2).Should().BeFalse();
            Triangle.EdgesIntersect(a1, a2, b2, b1).Should().BeFalse();
            Triangle.EdgesIntersect(a2, a1, b1, b2).Should().BeFalse();
            Triangle.EdgesIntersect(a2, a1, b2, b1).Should().BeFalse();
        }

        [Test]
        public void Segments_IntersectAtMiddle_ShouldReturnTrue()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(4, 4);
            float2 b1 = new float2(0, 4);
            float2 b2 = new float2(4, 0);

            Triangle.EdgesIntersect(a1, a2, b1, b2).Should().BeTrue();
            Triangle.EdgesIntersect(a1, a2, b2, b1).Should().BeTrue();
            Triangle.EdgesIntersect(a2, a1, b1, b2).Should().BeTrue();
            Triangle.EdgesIntersect(a2, a1, b2, b1).Should().BeTrue();
        }

        [Test]
        public void TrianglesIntersect_WhenOverlapping_ShouldReturnTrue()
        {
            var t1 = new Triangle(
                new float2(0, 0),
                new float2(1, 0),
                new float2(0, 1)
            );

            var t2 = new Triangle(
                new float2(0.2f, 0.2f),
                new float2(1.2f, 0.2f),
                new float2(0.2f, 1.2f)
            );

            Triangle.TrianglesIntersect(t1, t2).Should().BeTrue();
        }

        [Test]
        public void TrianglesIntersect_WhenSeparate_ShouldReturnFalse()
        {
            var t1 = new Triangle(
                new float2(0, 0),
                new float2(1, 0),
                new float2(0, 1)
            );

            var t2 = new Triangle(
                new float2(2, 2),
                new float2(3, 2),
                new float2(2, 3)
            );

            Triangle.TrianglesIntersect(t1, t2).Should().BeFalse();
        }

        [Test]
        public void TrianglesIntersect_WhenTouchingAtEdge_ShouldReturnFalse()
        {
            var t1 = new Triangle(
                new float2(0, 0),
                new float2(1, 0),
                new float2(0, 1)
            );

            var t2 = new Triangle(
                new float2(1, 0),
                new float2(2, 0),
                new float2(1, 1)
            );

            Triangle.TrianglesIntersect(t1, t2).Should().BeFalse(); // touching at point (1, 0)
        }

        [Test]
        public void AnyTrianglesIntersect_WhenSomeIntersect_ShouldReturnTrue()
        {
            var triangles = new List<Triangle>
            {
                new Triangle(new float2(0, 0), new float2(1, 0), new float2(0, 1)),
                new Triangle(new float2(2, 2), new float2(3, 2), new float2(2, 3)),
                new Triangle(new float2(0.1f, 0.1f), new float2(2f, 0.1f), new float2(0.5f, 1.1f)) // overlaps with first
            };

            Triangle.AnyTrianglesIntersect(triangles).Should().BeTrue();
        }

        [Test]
        public void AnyTrianglesIntersect_WhenAllSeparate_ShouldReturnFalse()
        {
            var triangles = new List<Triangle>
            {
                new Triangle(new float2(0, 0), new float2(1, 0), new float2(0, 1)),
                new Triangle(new float2(2, 2), new float2(3, 2), new float2(2, 3)),
                new Triangle(new float2(4, 4), new float2(5, 4), new float2(4, 5))
            };

            Triangle.AnyTrianglesIntersect(triangles).Should().BeFalse();
        }

        #endregion
    }
}