using System.Collections.Generic;
using FluentAssertions;
using Navigation;
using NUnit.Framework;
using Unity.Mathematics;
using Tests.TestsUtilities;

namespace Tests.EditorTests.NavigationTests
{
    public class GeometryUtilsTests
    {
        [Test]
        public void EdgesIntersect_DoNotIntersect_ShouldReturnFalse()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(1, 0);
            float2 b1 = new float2(0, 1);
            float2 b2 = new float2(1, 1);

            GeometryUtils.EdgesIntersect(a1, a2, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersect(a1, a2, b2, b1).Should().BeFalse();
            GeometryUtils.EdgesIntersect(a2, a1, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersect(a2, a1, b2, b1).Should().BeFalse();
        }

        [Test]
        public void EdgesIntersect_Intersect_ShouldReturnTrue()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(2, 2);
            float2 b1 = new float2(0, 2);
            float2 b2 = new float2(2, 0);

            GeometryUtils.EdgesIntersect(a1, a2, b1, b2).Should().BeTrue();
            GeometryUtils.EdgesIntersect(a1, a2, b2, b1).Should().BeTrue();
            GeometryUtils.EdgesIntersect(a2, a1, b1, b2).Should().BeTrue();
            GeometryUtils.EdgesIntersect(a2, a1, b2, b1).Should().BeTrue();
        }

        [Test]
        public void EdgesIntersect_TouchAtEndpoint_ShouldReturnFalse()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(1, 1);
            float2 b1 = new float2(1, 1);
            float2 b2 = new float2(2, 2);

            GeometryUtils.EdgesIntersect(a1, a2, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersect(a1, a2, b2, b1).Should().BeFalse();
            GeometryUtils.EdgesIntersect(a2, a1, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersect(a2, a1, b2, b1).Should().BeFalse();
        }

        [Test]
        public void EdgesIntersect_AreCollinearAndOverlapping_ShouldReturnFalse()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(2, 2);
            float2 b1 = new float2(1, 1);
            float2 b2 = new float2(3, 3);

            GeometryUtils.EdgesIntersect(a1, a2, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersect(a1, a2, b2, b1).Should().BeFalse();
            GeometryUtils.EdgesIntersect(a2, a1, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersect(a2, a1, b2, b1).Should().BeFalse();
        }

        [Test]
        public void EdgesIntersect_IntersectAtMiddle_ShouldReturnTrue()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(4, 4);
            float2 b1 = new float2(0, 4);
            float2 b2 = new float2(4, 0);

            GeometryUtils.EdgesIntersect(a1, a2, b1, b2).Should().BeTrue();
            GeometryUtils.EdgesIntersect(a1, a2, b2, b1).Should().BeTrue();
            GeometryUtils.EdgesIntersect(a2, a1, b1, b2).Should().BeTrue();
            GeometryUtils.EdgesIntersect(a2, a1, b2, b1).Should().BeTrue();
        }
        
        [Test]
        public void EdgesIntersectIncludeEnds_DoNotIntersect_ShouldReturnFalse()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(1, 0);
            float2 b1 = new float2(0, 1);
            float2 b2 = new float2(1, 1);

            GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b2, b1).Should().BeFalse();
            GeometryUtils.EdgesIntersectIncludeEnds(a2, a1, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersectIncludeEnds(a2, a1, b2, b1).Should().BeFalse();
        }

        [Test]
        public void EdgesIntersectIncludeEnds_Intersect_ShouldReturnTrue()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(2, 2);
            float2 b1 = new float2(0, 2);
            float2 b2 = new float2(2, 0);

            GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b1, b2).Should().BeTrue();
            GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b2, b1).Should().BeTrue();
            GeometryUtils.EdgesIntersectIncludeEnds(a2, a1, b1, b2).Should().BeTrue();
            GeometryUtils.EdgesIntersectIncludeEnds(a2, a1, b2, b1).Should().BeTrue();
        }

        [Test]
        public void EdgesIntersectIncludeEnds_TouchAtEndpoint_ShouldReturnTrue()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(1, 1);
            float2 b1 = new float2(1, 1);
            float2 b2 = new float2(2, 2);

            GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b2, b1).Should().BeFalse();
            GeometryUtils.EdgesIntersectIncludeEnds(a2, a1, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersectIncludeEnds(a2, a1, b2, b1).Should().BeFalse();
        }

        [Test]
        public void EdgesIntersectIncludeEnds_AreCollinearAndOverlapping_ShouldReturnTrue()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(2, 2);
            float2 b1 = new float2(1, 1);
            float2 b2 = new float2(3, 3);

            GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b2, b1).Should().BeFalse();
            GeometryUtils.EdgesIntersectIncludeEnds(a2, a1, b1, b2).Should().BeFalse();
            GeometryUtils.EdgesIntersectIncludeEnds(a2, a1, b2, b1).Should().BeFalse();
        }

        [Test]
        public void EdgesIntersectIncludeEnds_IntersectAtMiddle_ShouldReturnTrue()
        {
            float2 a1 = new float2(0, 0);
            float2 a2 = new float2(4, 4);
            float2 b1 = new float2(0, 4);
            float2 b2 = new float2(4, 0);

            GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b1, b2).Should().BeTrue();
            GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b2, b1).Should().BeTrue();
            GeometryUtils.EdgesIntersectIncludeEnds(a2, a1, b1, b2).Should().BeTrue();
            GeometryUtils.EdgesIntersectIncludeEnds(a2, a1, b2, b1).Should().BeTrue();
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

            Triangle.Intersect(t1, t2).Should().BeTrue();
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

            Triangle.Intersect(t1, t2).Should().BeFalse();
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

            Triangle.Intersect(t1, t2).Should().BeFalse(); // touching at point (1, 0)
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
        
        [Test]
        public void PolygonIntersection_ShouldReturnEmpty_WhenNoOverlap()
        {
            var t1 = new List<float2> { new(0, 0), new(1, 0), new(0, 1) };
            var t2 = new List<float2> { new(2, 2), new(3, 2), new(2, 3) };

            var result = Triangle.PolygonIntersection(t1, t2);

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

            var result = Triangle.PolygonIntersection(t1, t2);
            
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

            var result = Triangle.PolygonIntersection(t1, t2);
            
            // [float2(1f, 3f), float2(1f, 3f), float2(1f, 3f), float2(1f, 2f), float2(1,333333f, 2f), float2(1f, 3f)]
            // Debug.Log(result.Stringify());
            
            result.Count.Should().Be(3);
            result[0].Should().BeApproximately(new float2(1, 3));
            result[1].Should().BeApproximately(new float2(1, 2));
            result[2].Should().BeApproximately(new float2(1.3333333333333f, 2));
        }
    }
}