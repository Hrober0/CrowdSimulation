using System.Collections.Generic;
using FluentAssertions;
using Navigation;
using NUnit.Framework;
using Tests.TestsUtilities;
using Unity.Mathematics;

namespace Tests.EditorTests.NavigationTests
{
    public class PolygonUtilsTests
    {
        [Test]
        public void HullEdges_CCWTriangle()
        {
            var triangles = new List<Triangle>
            {
                new Triangle(new (0, 0), new (1, 0), new (0.5f, 1))
            };
        
            var result = PolygonUtils.GetPointsCCW(triangles);
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
        
            var result = PolygonUtils.GetPointsCCW(triangles);
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
        
            var result = PolygonUtils.GetPointsCCW(triangles);
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
        
            var result = PolygonUtils.GetPointsCCW(triangles);
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
        
            var result = PolygonUtils.GetPointsCCW(triangles);
        
            result.Should().HaveCount(5);
            result[0].Should().BeApproximately(new (0, 0));
            result[1].Should().BeApproximately(new (1, 0));
            result[2].Should().BeApproximately(new (1, 1));
            result[3].Should().BeApproximately(new (1, 2));
            result[4].Should().BeApproximately(new (0, 1));
        }
        
        [Test]
        public void PolygonIntersection_ShouldReturnEmpty_WhenNoOverlap()
        {
            var t1 = new List<float2> { new(0, 0), new(1, 0), new(0, 1) };
            var t2 = new List<float2> { new(2, 2), new(3, 2), new(2, 3) };

            var result = PolygonUtils.PolygonIntersection(t1, t2);

            result.Count.Should().Be(0);
        }

        [Test]
        public void PolygonIntersection_ShouldClipOverlap()
        {
            //     Y
            //     ▲
            //   2 |     *     *
            //     |     \   / \ 
            // 1.5 |      \ /   \
            //     |       /     \
            //   1 |      / \     \ *
            //     |     /   \    /\
            // 0.5 |    /     *     \
            //     |   /             \
            //   0 |    *------------*                 
            //     └──────────────────────────▶ X
            //          0     1     2

            
            var t1 = new List<float2> { new(0, 0), new(2, 0), new(1, 2) };
            var t2 = new List<float2> { new(1, 0.5f), new(2, 1), new(0.5f, 2) };

            var result = PolygonUtils.PolygonIntersection(t1, t2);
            
            // [float2(1,166667f, 0f), float2(2f, 0f), float2(1f, 2f), float2(0,7f, 1,4f)]
            // Debug.Log(result.Stringify());
            
            result.Count.Should().Be(5);
            result.Should().ContainInOrder(
                new float2(0.875f, 1.75f),
                new float2(0.7f, 1.4f),
                new float2(1f, 0.5f),
                new float2(1.6f, 0.8f), 
                new float2(1.25f, 1.5f)
                );
        }
        
        [Test]
        public void PolygonIntersection_ShouldClipOverlap2()
        {
            //     (1,4) *
            //           | \      
            //     (1,3) *  \
            //          /|\  \
            //         / | \  \
            //        /  *--\--* (3,2)
            //       /       \
            //      *─────────*
            //      (0,0)   (2,0)

            
            var t1 = new List<float2> { new(0, 0), new(2, 0), new(1, 3) };
            var t2 = new List<float2> { new(1, 2), new(3, 2), new(1, 3) };

            var result = PolygonUtils.PolygonIntersection(t1, t2);
            
            // [float2(1f, 3f), float2(1f, 3f), float2(1f, 3f), float2(1f, 2f), float2(1,333333f, 2f), float2(1f, 3f)]
            // Debug.Log(result.Stringify());
            
            result.Count.Should().Be(3);
            result[0].Should().BeApproximately(new float2(1, 3));
            result[1].Should().BeApproximately(new float2(1, 2));
            result[2].Should().BeApproximately(new float2(1.3333333333333f, 2));
        }
    }
}