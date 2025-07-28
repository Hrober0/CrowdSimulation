using System.Collections.Generic;
using FluentAssertions;
using Navigation;
using NUnit.Framework;
using Tests.TestsUtilities;

namespace Tests.EditorTests.NavigationTests
{
    public class HullEdgesTests
    {
        [Test]
        public void HullEdges_CCWTriangle()
        {
            var triangles = new List<Triangle>
            {
                new Triangle(new (0, 0), new (1, 0), new (0.5f, 1))
            };
        
            var result = HullEdges.GetPointsCCW(triangles);
            result.Should().HaveCount(3);
            result[0].Should().BeApproximately(new (0, 0));
            result[1].Should().BeApproximately(new (1, 0));
            result[2].Should().BeApproximately(new (0.5f, 1));
        }
        
        [Test]
        public void HullEdges_CWTriangle()
        {
            var triangles = new List<Triangle>
            {
                new Triangle(new (0, 0), new (0.5f, 1), new (1, 0))
            };
        
            var result = HullEdges.GetPointsCCW(triangles);
            result.Should().HaveCount(3);
            result[0].Should().BeApproximately(new (1, 0));
            result[1].Should().BeApproximately(new (0.5f, 1));
            result[2].Should().BeApproximately(new (0, 0));
        }
        
        [Test]
        public void HullEdges_CCWSquare()
        {
            var triangles = new List<Triangle>
            {
                new Triangle(new (0, 0), new (1, 0), new (1, 1)),
                new Triangle(new (0, 0), new (1, 1), new (0, 1)),
            };
        
            var result = HullEdges.GetPointsCCW(triangles);
            result.Should().HaveCount(4);
            result[0].Should().BeApproximately(new (0, 0));
            result[1].Should().BeApproximately(new (1, 0));
            result[2].Should().BeApproximately(new (1, 1));
            result[3].Should().BeApproximately(new (0, 1));
        }
        
        [Test]
        public void HullEdges_CWSquare()
        {
            var triangles = new List<Triangle>
            {
                new Triangle(new (0, 0), new (1, 1), new (1, 0)),
                new Triangle(new (0, 0), new (0, 1), new (1, 1)),
            };
        
            var result = HullEdges.GetPointsCCW(triangles);
            result.Should().HaveCount(4);
            result[0].Should().BeApproximately(new (0, 0));
            result[1].Should().BeApproximately(new (1, 0));
            result[2].Should().BeApproximately(new (1, 1));
            result[3].Should().BeApproximately(new (0, 1));
        }
        
        [Test]
        public void HullEdges_3TriangleMesh()
        {
            var triangles = new List<Triangle>
            {
                new Triangle(new (0, 0), new (1, 0), new (0, 1)),
                new Triangle(new (1, 0), new (1, 1), new (0, 1)),
                new Triangle(new (1, 1), new (0, 1), new (1, 2)),
            };
        
            var result = HullEdges.GetPointsCCW(triangles);
        
            result.Should().HaveCount(5);
            result[0].Should().BeApproximately(new (0, 0));
            result[1].Should().BeApproximately(new (1, 0));
            result[2].Should().BeApproximately(new (1, 1));
            result[3].Should().BeApproximately(new (1, 2));
            result[4].Should().BeApproximately(new (0, 1));
        }
    }
}