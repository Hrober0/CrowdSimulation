using System.Collections.Generic;
using DelaunayTriangulation;
using FluentAssertions;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditorTests.DelaunayTriangulationTests
{
    public class DelaunayTriangulationTests
    {
        [Test]
        public void RectangleSplit()
        {
            var points = new List<Vector2>()
            {
                new(0, 10), 
                new(10, 10), 
                new(10, 0),
                new(0, 0),
            };
            
            var triangulation = new DelaunayTriangulation.DelaunayTriangulation();
            triangulation.Triangulate(points);
            var result = new List<Triangle2D>();
            triangulation.GetTrianglesDiscardingHoles(result);

            // DebugResult(result);
            
            result.Should().BeEquivalentTo(new List<Triangle2D>()
                {
                    new(new(0, 10), new(0, 0), new(10, 0)),
                    new(new(0, 10), new(10, 0), new(10, 10))
                });
        }
        
        [Test]
        public void PointInRectangle()
        {
            var points = new List<Vector2>()
            {
                new(0, 10), 
                new(10, 10), 
                new(10, 0),
                new(0, 0),
                new(3, 5),
            };
            
            var triangulation = new DelaunayTriangulation.DelaunayTriangulation();
            triangulation.Triangulate(points);
            var result = new List<Triangle2D>();
            triangulation.GetTrianglesDiscardingHoles(result);

            // DebugResult(result);

            result.Should().BeEquivalentTo(new List<Triangle2D>()
            {
                new(new(3, 5), new(0, 0), new(10, 0)),
                new(new(10, 10), new(0, 10), new(3, 5)),
                new(new(10, 10), new(3, 5), new(10, 0)),
                new(new(0, 10), new(0, 0), new(3, 5)),
            });
        }
        
        [Test]
        public void TriangleHoleInRectangle()
        {
            var points = new List<Vector2>()
            {
                new(0, 10), 
                new(10, 10), 
                new(10, 0),
                new(0, 0),
            };

            var constrains = new List<List<Vector2>>()
            {
                new()
                {
                    new(3, 5),
                    new(6, 5),
                    new(5, 7),
                }
            };
            
            var triangulation = new DelaunayTriangulation.DelaunayTriangulation();
            triangulation.Triangulate(points, constrainedEdges: constrains);
            var result = new List<Triangle2D>();
            triangulation.GetTrianglesDiscardingHoles(result);
            
            // DebugResult(result);
            
            result.Should().BeEquivalentTo(new List<Triangle2D>()
            {
                new(new (3,5), new (0, 0), new (6, 5)),
                new(new (6,5), new (10, 0), new (10, 10)),
                new(new (3,5), new (0, 10), new (0, 0)),
                new(new (5,7), new (10, 10), new (0, 10)),
                new(new (6,5), new (0, 0), new (10, 0)),
                new(new (5,7), new (0, 10), new (3, 5)),
                new(new (5,7), new (6, 5), new (10, 10)),
            });
        }

        private static void DebugResult(List<Triangle2D> result)
        {
            for (var index = 0; index < result.Count; index++)
            {
                Triangle2D triangle = result[index];
                Debug.Log($"Assert.AreEqual(new Triangle2D(new ({triangle.p0.x},{triangle.p0.y}), new ({triangle.p1.x}, {triangle.p1.y}), new ({triangle.p2.x}, {triangle.p2.y})), result[{index}]);");
            }
        }
    }
}
