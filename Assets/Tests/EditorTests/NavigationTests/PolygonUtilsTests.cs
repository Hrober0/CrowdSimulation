using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FluentAssertions;
using HCore.Extensions;
using Navigation;
using NUnit.Framework;
using Tests.TestsUtilities;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Color = UnityEngine.Color;

namespace Tests.EditorTests.NavigationTests
{
    public class PolygonUtilsTests
    {
        private bool debug = true;

        [Test]
        public void GetEdgesUnordered_ShouldReturn3Edges_FromSingleTriangle()
        {
            // Arrange
            using var triangles = new NativeList<Triangle>(Allocator.Temp)
            {
                new(new(0, 0), new(1, 0), new(0, 1)),
            };

            // Act
            using var edges = new NativeList<EdgeKey>(Allocator.Temp);
            PolygonUtils.GetEdgesUnordered(triangles, edges);

            // Assert
            edges.Length.Should().Be(3);
            edges.AsArray().Should().Contain(new EdgeKey(new(0, 0), new(1, 0)));
            edges.AsArray().Should().Contain(new EdgeKey(new(1, 0), new(0, 1)));
            edges.AsArray().Should().Contain(new EdgeKey(new(0, 1), new(0, 0)));
        }

        [Test]
        public void GetEdgesUnordered_ShouldReturn4_FromTwoAdjacentTriangles()
        {
            // Arrange: with common edge (1,0)-(0,1)
            using var triangles = new NativeList<Triangle>(Allocator.Temp)
            {
                new(new(0, 0), new(1, 0), new(0, 1)),
                new(new(1, 0), new(1, 1), new(0, 1)),
            };

            // Act
            using var edges = new NativeList<EdgeKey>(Allocator.Temp);
            PolygonUtils.GetEdgesUnordered(triangles, edges);

            // Assert
            edges.Length.Should().Be(4);
            edges.AsArray().Should().Contain(new EdgeKey(new(0, 0), new(1, 0)));
            edges.AsArray().Should().Contain(new EdgeKey(new(0, 1), new(0, 0)));
            edges.AsArray().Should().Contain(new EdgeKey(new(1, 0), new(1, 1)));
            edges.AsArray().Should().Contain(new EdgeKey(new(0, 1), new(1, 1)));
        }

        [Test]
        public void GetEdgesUnordered_ShouldReturnEmptyEdges_FromEmptyList()
        {
            // Arrange
            using var triangles = new NativeList<Triangle>(Allocator.Temp);

            // Act
            using var edges = new NativeList<EdgeKey>(Allocator.Temp);
            PolygonUtils.GetEdgesUnordered(triangles, edges);

            // Assert
            edges.Length.Should().Be(0);
        }

        [Test]
        public void GetPointsCCW_ShouldReturnSamePoints_ForOneEdge()
        {
            // Arrange only one edge
            using var edges = new NativeList<EdgeKey>(Allocator.Temp)
            {
                new(new(0, 0), new(1, 0)),
            };

            // Act
            using var points = new NativeList<float2>(Allocator.Temp);
            PolygonUtils.GetPointsCCW(in edges, points).Should().BeTrue();

            // Assert
            points.Length.Should().Be(2);
            points[0].Should().BeApproximately(new(0, 0));
            points[1].Should().BeApproximately(new(1, 0));
        }

        [Test]
        public void GetPointsCCW_ShouldReturnFalse_ForNotClosedLoop()
        {
            // Arrange not connected edges
            using var edges = new NativeList<EdgeKey>(Allocator.Temp)
            {
                new(new(0, 0), new(1, 0)),
                new(new(1, 0), new(2, 1)),
                new(new(2, 1), new(3, 1)),
            };

            // Act
            using var points = new NativeList<float2>(Allocator.Temp);
            PolygonUtils.GetPointsCCW(in edges, points).Should().BeFalse();

            // Assert
            points.AsArray().Should().ContainInOrderLooped(new(0, 0), new(1, 0), new(2, 1), new(3, 1));
        }

        [Test]
        public void GetPointsCCW_ShouldReturnCCWPoints_FromCCWTriangle()
        {
            // Arrange CCW triangle
            using var edges = new NativeList<EdgeKey>(Allocator.Temp)
            {
                new(new(0, 0), new(1, 0)),
                new(new(1, 0), new(0.5f, 1)),
                new(new(0.5f, 1), new(0, 0)),
            };

            // Act
            using var points = new NativeList<float2>(Allocator.Temp);
            PolygonUtils.GetPointsCCW(in edges, points).Should().BeTrue();

            // Assert
            points.AsArray().Should().ContainInOrderLooped(new(0, 0), new(1, 0), new(.5f, 1));
        }

        [Test]
        public void GetPointsCCW_ShouldReturnCCWPoints_FromCWTriangle()
        {
            // Arrange CW triangle
            using var edges = new NativeList<EdgeKey>(Allocator.Temp)
            {
                new(new(0, 0), new(1, 0)),
                new(new(0.5f, 1), new(0, 0)),
                new(new(1, 0), new(0.5f, 1)),
            };

            // Act
            using var points = new NativeList<float2>(Allocator.Temp);
            PolygonUtils.GetPointsCCW(in edges, points).Should().BeTrue();

            // Assert
            points.AsArray().Should().ContainInOrderLooped(new(1, 0), new(0.5f, 1), new(0, 0));
        }

        [Test]
        public void GetPointsCCW_ShouldReturnCCWPoints_FromCCWSquare()
        {
            // Arrange CWW squarer
            using var edges = new NativeList<EdgeKey>(Allocator.Temp)
            {
                new(new(0, 0), new(1, 0)),
                new(new(1, 0), new(1, 1)),
                new(new(1, 1), new(0, 1)),
                new(new(0, 1), new(0, 0)),
            };

            // Act
            using var points = new NativeList<float2>(Allocator.Temp);
            PolygonUtils.GetPointsCCW(in edges, points).Should().BeTrue();

            // Assert
            points.AsArray().Should().ContainInOrderLooped(new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }

        [Test]
        public void GetPointsCCW_ShouldReturnCCWPoints_FromCWSquare()
        {
            // Arrange CW square
            using var edges = new NativeList<EdgeKey>(Allocator.Temp)
            {
                new(new(0, 0), new(0, 1)),
                new(new(0, 1), new(1, 1)),
                new(new(1, 1), new(1, 0)),
                new(new(1, 0), new(0, 0)),
            };

            // Act
            using var points = new NativeList<float2>(Allocator.Temp);
            PolygonUtils.GetPointsCCW(in edges, points).Should().BeTrue();

            // Assert
            points.AsArray().Should().ContainInOrderLooped(new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }

        [Test]
        public void GetPointsCCW_ShouldAddPointsToListWithElement()
        {
            // Arrange CW square
            using var edges = new NativeList<EdgeKey>(Allocator.Temp)
            {
                new(new(0, 0), new(0, 1)),
                new(new(0, 1), new(1, 1)),
                new(new(1, 1), new(1, 0)),
                new(new(1, 0), new(0, 0)),
            };

            // Act
            using var points = new NativeList<float2>(Allocator.Temp)
            {
                new(-1, 1),
                new(-1, 2),
                new(-1, 3),
            };
            PolygonUtils.GetPointsCCW(in edges, points).Should().BeTrue();

            // Assert
            points.AsArray().Should().ContainInOrderLooped(new(-1, 1), new(-1, 2), new(-1, 3), new(1, 0), new(1, 1), new(0, 1), new(0, 0));
        }

        [Test]
        public void ReduceEdges_ShouldPreserveSquare()
        {
            using var points = new NativeList<float2>(Allocator.Temp)
            {
                new float2(0, 0),
                new float2(1, 0),
                new float2(1, 1),
                new float2(0, 1)
            };
            using var edges = new NativeList<Edge>(Allocator.Temp);

            PolygonUtils.ReduceEdges(points, edges);

            edges.Length.Should().Be(4);
            edges.AsArray().Should_ContainKey(new(new(0, 0), new(1, 0)));
            edges.AsArray().Should_ContainKey(new(new(1, 0), new(1, 1)));
            edges.AsArray().Should_ContainKey(new(new(1, 1), new(0, 1)));
            edges.AsArray().Should_ContainKey(new(new(0, 1), new(0, 0)));
        }

        [Test]
        public void ReduceEdges_ShouldRemoveCollinearPointOnSquareSide()
        {
            using var points = new NativeList<float2>(Allocator.Temp)
            {
                new(0, 0),
                new(0.5f, 0), // collinear, redundant
                new(1, 0),
                new(1, 1),
                new(0, 1)
            };
            using var edges = new NativeList<Edge>(Allocator.Temp);

            PolygonUtils.ReduceEdges(points, edges);

            edges.Length.Should().Be(4, "collinear point should be removed");
            edges.AsArray().Should_ContainKey(new(new(0, 0), new(1, 0)));
        }

        [Test]
        public void ReduceEdges_ShouldRemoveMultipleCollinearPoints()
        {
            using var points = new NativeList<float2>(Allocator.Temp)
            {
                new(0, 0),
                new(0.25f, 0),
                new(0.5f, 0),
                new(0.75f, 0),
                new(1, 0),
                new(1, 1),
                new(0, 1)
            };
            using var edges = new NativeList<Edge>(Allocator.Temp);

            PolygonUtils.ReduceEdges(points, edges);

            edges.Length.Should().Be(4, "all redundant collinear points should be removed");
            edges.AsArray().Should_ContainKey(new(new(0, 0), new(1, 0)));
        }

        [Test]
        public void ReduceEdges_ShouldReturnEmpty_WhenLessThan3Points()
        {
            using var points = new NativeList<float2>(Allocator.Temp)
            {
                new(0, 0),
                new(1, 0)
            };
            using var edges = new NativeList<Edge>(Allocator.Temp);

            PolygonUtils.ReduceEdges(points, edges);

            edges.Length.Should().Be(0);
        }

        [Test]
        public void CutIntersectingEdges_ShouldSplitIntoThree_WhenTwoIntersectionsOnSameEdge()
        {
            // Arrange
            var edges = new List<Edge>
            {
                new Edge(new(0, 0), new(10, 0)), // horizontal line
                new Edge(new(3, -1), new(3, 1)), // vertical cross at (3,0)
                new Edge(new(7, -1), new(7, 1)), // vertical cross at (7,0)
            };

            // Act
            var result = ToNative(edges);
            PolygonUtils.CutIntersectingEdges(result);

            Draw(result);

            // Assert
            // EXPECTED: horizontal line should be split into 3 subedges
            result.AsArray().Should_ContainKey(new EdgeKey(new(0, 0), new(3, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(3, 0), new(7, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(10, 0), new(7, 0)));

            // Actual: with your current implementation → only one split survives
            result.AsArray().Should().HaveCount(7, "because the current algorithm overwrites one split with another");
        }

        [Test]
        public void CutIntersectingEdges_ShouldHandleThreeIntersectionsOnSameEdge()
        {
            var edges = new List<Edge>
            {
                new Edge(new(0, 0), new(10, 0)), // horizontal
                new Edge(new(2, -1), new(2, 1)), // vertical at 2
                new Edge(new(5, -1), new(5, 1)), // vertical at 5
                new Edge(new(8, -1), new(8, 1)), // vertical at 8
            };

            using var result = ToNative(edges);
            PolygonUtils.CutIntersectingEdges(result);

            Draw(result);

            // Horizontal should be split into 4 pieces
            result.AsArray().Should_ContainKey(new EdgeKey(new(10, 0), new(8, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(5, 0), new(8, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(2, 0), new(5, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(0, 0), new(2, 0)));

            // So total should be 4 + 2 + 2 + 2 = 10 edges
            result.AsArray().Should().HaveCount(10);
        }

        [Test]
        public void CutIntersectingEdges_ShouldHandleMultipleIntersectionInOnePoint()
        {
            var edges = new List<Edge>
            {
                new Edge(new(0, 0), new(10, 0)), // horizontal
                new Edge(new(5, -1), new(5, 1)), // vertical at 5
                new Edge(new(4, -1), new(6, 1)), // / at 5
                new Edge(new(6, -1), new(4, 1)), // \ at 5
            };

            using var result = ToNative(edges);
            PolygonUtils.CutIntersectingEdges(result);

            Draw(result);

            // Horizontal should be split into 4 pieces
            result.AsArray().Should_ContainKey(new EdgeKey(new(5, 0), new(0, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(10, 0), new(5, 0)));

            result.AsArray().Should_ContainKey(new EdgeKey(new(5, -1), new(5, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(5, 0), new(5, 1)));

            result.AsArray().Should_ContainKey(new EdgeKey(new(4, -1), new(5, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(5, 0), new(6, 1)));

            result.AsArray().Should_ContainKey(new EdgeKey(new(6, -1), new(5, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(5, 0), new(4, 1)));

            result.AsArray().Should().HaveCount(8);
        }

        [Test]
        public void CutIntersectingEdges_ShouldCutEdge_WhenOnlyEdgeIsIntersected()
        {
            // Arrange
            var edges = new List<Edge>
            {
                new Edge(new(0, 0), new(10, 0)), // horizontal line
                new Edge(new(5, -1), new(5, 0)), // vertical cross at (3,0)
            };

            // Act
            using var result = ToNative(edges);
            PolygonUtils.CutIntersectingEdges(result);

            Draw(result);

            // Assert
            result.AsArray().Should_ContainKey(new EdgeKey(new(0, 0), new(5, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(5, 0), new(10, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(5, -1), new(5, 0)));

            result.AsArray().Should().HaveCount(3);
        }

        [Test]
        public void CutIntersectingEdges_ShouldCutOverlappingPartially()
        {
            // Arrange
            var edges = new List<Edge>
            {
                new Edge(new(0, 0), new(10, 0)), // horizontal line
                new Edge(new(5, 0), new(15, 0)), // horizontal line ont the right 
            };

            // Act
            using var result = ToNative(edges);
            PolygonUtils.CutIntersectingEdges(result);

            Draw(result);

            // Assert
            result.AsArray().Should_ContainKey(new EdgeKey(new(0, 0), new(5, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(5, 0), new(10, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(10, 0), new(15, 0)));

            result.AsArray().Should().HaveCount(3);
        }

        [Test]
        public void CutIntersectingEdges_ShouldCutOverlappingCompletely_WhenEdgeIsInside()
        {
            // Arrange
            var edges = new List<Edge>
            {
                new Edge(new(0, 0), new(10, 0)), // horizontal line
                new Edge(new(3, 0), new(7, 0)), // overlapping 
            };

            // Act
            using var result = ToNative(edges);
            PolygonUtils.CutIntersectingEdges(result);

            Draw(result);

            // Assert
            result.AsArray().Should_ContainKey(new EdgeKey(new(0, 0), new(3, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(3, 0), new(7, 0)));
            result.AsArray().Should_ContainKey(new EdgeKey(new(7, 0), new(10, 0)));

            result.AsArray().Should().HaveCount(3);
        }

        [Test]
        public void CutIntersectingEdges_ShouldCutOverlappingCompletely()
        {
            // Arrange
            var edges = new List<Edge>
            {
                new Edge(new(0, 0), new(10, 0)), // horizontal line
                new Edge(new(0, 0), new(10, 0)), // overlapping 
            };

            // Act
            using var result = ToNative(edges);
            PolygonUtils.CutIntersectingEdges(result);

            Draw(result);

            // Assert
            result.AsArray().Should_ContainKey(new EdgeKey(new(0, 0), new(10, 0)));

            result.AsArray().Should().HaveCount(1);
        }

        [Test]
        public void CutIntersectingEdges_ShouldNotDetectedCutWithLowTolerance()
        {
            // Arrange
            var edges = new List<Edge>
            {
                new Edge(new(0, 0), new(0.000550436613f, 4.59566927f)),
                new Edge(new(0.000403523445f, 3.36886024f), new(0.0248594582f, 3.33806086f)),
            };

            // Act
            using var result = ToNative(edges);
            PolygonUtils.CutIntersectingEdges(result, tolerance: 0);

            Draw(result);

            // Assert
            result.AsArray().Should().HaveCount(2);
        }

        [Test]
        public void CutIntersectingEdges_ShouldDetectedCutWithHighTolerance()
        {
            // Arrange
            var edges = new List<Edge>
            {
                new Edge(new(0, 0), new(0.000550436613f, 4.59566927f)),
                new Edge(new(0.000403523445f, 3.36886024f), new(0.0248594582f, 3.33806086f)),
            };

            // Act
            using var result = ToNative(edges);
            PolygonUtils.CutIntersectingEdges(result, tolerance: 0.001f);

            Draw(result);

            // Assert
            result.AsArray().Should().HaveCount(3);
        }

        // [Test]
        // public void PolygonIntersection_ShouldReturnEmpty_WhenNoOverlap()
        // {
        //     var t1 = new List<float2> { new(0, 0), new(1, 0), new(0, 1) };
        //     var t2 = new List<float2> { new(2, 2), new(3, 2), new(2, 3) };
        //
        //     var result = PolygonUtils.PolygonIntersection(t1, t2);
        //
        //     result.Count.Should().Be(0);
        // }
        //
        // [Test]
        // public void PolygonIntersection_ShouldClipOverlap()
        // {
        //     //     Y
        //     //     ▲
        //     //   2 |     *     *
        //     //     |     \   / \ 
        //     // 1.5 |      \ /   \
        //     //     |       /     \
        //     //   1 |      / \     \ *
        //     //     |     /   \    /\
        //     // 0.5 |    /     *     \
        //     //     |   /             \
        //     //   0 |    *------------*                 
        //     //     └──────────────────────────▶ X
        //     //          0     1     2
        //
        //
        //     var t1 = new List<float2> { new(0, 0), new(2, 0), new(1, 2) };
        //     var t2 = new List<float2> { new(1, 0.5f), new(2, 1), new(0.5f, 2) };
        //
        //     var result = PolygonUtils.PolygonIntersection(t1, t2);
        //
        //     // [float2(1,166667f, 0f), float2(2f, 0f), float2(1f, 2f), float2(0,7f, 1,4f)]
        //     // Debug.Log(result.Stringify());
        //
        //     result.Count.Should().Be(5);
        //     result.Should().ContainInOrder(
        //         new(0.875f, 1.75f),
        //         new(0.7f, 1.4f),
        //         new(1f, 0.5f),
        //         new(1.6f, 0.8f),
        //         new(1.25f, 1.5f)
        //     );
        // }
        //
        // [Test]
        // public void PolygonIntersection_ShouldClipOverlap2()
        // {
        //     //     (1,4) *
        //     //           | \      
        //     //     (1,3) *  \
        //     //          /|\  \
        //     //         / | \  \
        //     //        /  *--\--* (3,2)
        //     //       /       \
        //     //      *─────────*
        //     //      (0,0)   (2,0)
        //
        //
        //     var t1 = new List<float2> { new(0, 0), new(2, 0), new(1, 3) };
        //     var t2 = new List<float2> { new(1, 2), new(3, 2), new(1, 3) };
        //
        //     var result = PolygonUtils.PolygonIntersection(t1, t2);
        //
        //     // [float2(1f, 3f), float2(1f, 3f), float2(1f, 3f), float2(1f, 2f), float2(1,333333f, 2f), float2(1f, 3f)]
        //     // Debug.Log(result.Stringify());
        //
        //     result.Count.Should().Be(3);
        //     result[0].Should().BeApproximately(new(1, 3));
        //     result[1].Should().BeApproximately(new(1, 2));
        //     result[2].Should().BeApproximately(new(1.3333333333333f, 2));
        // }

        #region Helpers

        private void Draw(in NativeList<Edge> edges)
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

        private NativeList<T> ToNative<T>(List<T> list) where T : unmanaged
        {
            var result = new NativeList<T>(list.Count, Allocator.Temp);
            for (int i = 0; i < list.Count; i++)
            {
                result.Add(list[i]);
            }

            return result;
        }

        #endregion
    }
}