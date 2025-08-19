using System.Collections.Generic;
using FluentAssertions;
using HCore.Extensions;
using Navigation;
using NUnit.Framework;
using Tests.TestsUtilities;
using Unity.Mathematics;
using UnityEngine;

namespace Tests.EditorTests.NavigationTests
{
    public class PolygonUtilsTests
    {
        private bool debug = true;
        
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
        
        [Test]
        public void CutIntersectingEdges_ShouldSplitIntoThree_WhenTwoIntersectionsOnSameEdge()
        {
            // Arrange
            var edges = new List<Edge>
            {
                new Edge(new float2(0,0), new float2(10,0)), // horizontal line
                new Edge(new float2(3,-1), new float2(3,1)), // vertical cross at (3,0)
                new Edge(new float2(7,-1), new float2(7,1)), // vertical cross at (7,0)
            };

            // Act
            var result = new List<Edge>(edges);
            PolygonUtils.CutIntersectingEdges(result);
            
            Draw(result);

            // Assert
            // EXPECTED: horizontal line should be split into 3 subedges
            result.ShouldContainKey(new EdgeKey(new float2(0,0), new float2(3,0)));
            result.ShouldContainKey(new EdgeKey(new float2(3,0), new float2(7,0)));
            result.ShouldContainKey(new EdgeKey(new float2(10,0), new float2(7,0)));

            // Actual: with your current implementation → only one split survives
            result.Should().HaveCount(7, "because the current algorithm overwrites one split with another");
        }
        
        
        [Test]
        public void CutIntersectingEdges_ShouldHandleThreeIntersectionsOnSameEdge()
        {
            var edges = new List<Edge>
            {
                new Edge(new float2(0,0), new float2(10,0)),  // horizontal
                new Edge(new float2(2,-1), new float2(2,1)),  // vertical at 2
                new Edge(new float2(5,-1), new float2(5,1)),  // vertical at 5
                new Edge(new float2(8,-1), new float2(8,1)),  // vertical at 8
            };

            var result = new List<Edge>(edges);
            PolygonUtils.CutIntersectingEdges(result);
            
            Draw(result);

            // Horizontal should be split into 4 pieces
            result.ShouldContainKey(new EdgeKey(new float2(10,0), new float2(8,0)));
            result.ShouldContainKey(new EdgeKey(new float2(5,0), new float2(8,0)));
            result.ShouldContainKey(new EdgeKey(new float2(2,0), new float2(5,0)));
            result.ShouldContainKey(new EdgeKey(new float2(0,0), new float2(2,0)));

            // So total should be 4 + 2 + 2 + 2 = 10 edges
            result.Count.Should().Be(10);
        }
        
        [Test]
        public void CutIntersectingEdges_ShouldHandleMultipleIntersectionInOnePoint()
        {
            var edges = new List<Edge>
            {
                new Edge(new float2(0,0), new float2(10,0)),  // horizontal
                new Edge(new float2(5,-1), new float2(5,1)),  // vertical at 5
                new Edge(new float2(4,-1), new float2(6,1)),  // / at 5
                new Edge(new float2(6,-1), new float2(4,1)),  // \ at 5
            };

            var result = new List<Edge>(edges);
            PolygonUtils.CutIntersectingEdges(result);
            
            Draw(result);

            // Horizontal should be split into 4 pieces
            result.ShouldContainKey(new EdgeKey(new float2(10,0), new float2(5,0)));
            result.ShouldContainKey(new EdgeKey(new float2(5,0), new float2(0,0)));
            
            result.ShouldContainKey(new EdgeKey(new float2(5,-1), new float2(5,0)));
            result.ShouldContainKey(new EdgeKey(new float2(5,0), new float2(5, 1)));
            
            result.ShouldContainKey(new EdgeKey(new float2(4,-1), new float2(5,0)));
            result.ShouldContainKey(new EdgeKey(new float2(5,0), new float2(6, 1)));
            
            result.ShouldContainKey(new EdgeKey(new float2(6,-1), new float2(5,0)));
            result.ShouldContainKey(new EdgeKey(new float2(5,0), new float2(4, 1)));
            
            result.Count.Should().Be(8);
        }

        [Test]
        public void CutIntersectingEdges_ShouldCutEdge_WhenOnlyEdgeIsIntersected()
        {
            // Arrange
            var edges = new List<Edge>
            {
                new Edge(new float2(0,0), new float2(10,0)), // horizontal line
                new Edge(new float2(5,-1), new float2(5,0)), // vertical cross at (3,0)
            };

            // Act
            var result = new List<Edge>(edges);
            PolygonUtils.CutIntersectingEdges(result);
            
            Draw(result);

            // Assert
            result.ShouldContainKey(new EdgeKey(new float2(0,0), new float2(5,0)));
            result.ShouldContainKey(new EdgeKey(new float2(5,0), new float2(10,0)));
            result.ShouldContainKey(new EdgeKey(new float2(5,-1), new float2(5,0)));
            
            result.Should().HaveCount(3);
        }
        
        private void Draw(List<Edge> edges)
        {
            if (!debug)
            {
                return;
            }
            
            foreach (var edge in edges)
            {
                Debug.DrawLine(edge.A.To3D(), edge.B.To3D(), Color.white, 5);
                edge.Center.To3D().DrawPoint(Color.red, 5, 0.1f);
            }
        }
    }
}