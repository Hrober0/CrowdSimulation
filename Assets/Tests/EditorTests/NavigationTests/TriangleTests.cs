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

            bool inside = Triangle.IsPointIn(p, a, b, c);
            inside.Should().BeTrue();
        }
        
        [Test]
        public void PointOutsideTriangle_ShouldReturnFalse()
        {
            var a = new float2(0, 0);
            var b = new float2(1, 0);
            var c = new float2(0, 1);
            var p = new float2(1f, 1f);

            bool inside = Triangle.IsPointIn(p, a, b, c);
            inside.Should().BeFalse();
        }
        
        [Test]
        public void PointOnEdge_ShouldReturnTrue()
        {
            var a = new float2(0, 0);
            var b = new float2(1, 0);
            var c = new float2(0, 1);
            var p = new float2(0.5f, 0f); // On edge AB

            bool inside = Triangle.IsPointIn(p, a, b, c);
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
    }
}