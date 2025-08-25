using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HCore.Extensions;
using Navigation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using HCore.Shapes;
using Unity.Jobs;
using Triangle = Navigation.Triangle;

namespace Tests.EditorTests.NavigationTests
{
    public class NavigationModificationTests
    {
        private bool debug = true;

        // [Test]
        // public void EnsureValidTriangulation_ShouldNotChange_WhenNoIntersection()
        // {
        //     var nodes = new List<NavMesh.AddNodeRequest>
        //     {
        //         new() { Triangle = new(new(0, 0), new(2, 0), new(1, 2)), ObstacleId = 0 },
        //         new() { Triangle = new(new(2, 0), new(4, 0), new(3, 2)), ObstacleId = 1 },
        //         new() { Triangle = new(new(2, 0), new(3, 2), new(1, 2)), ObstacleId = 2 },
        //     };
        //
        //     var result = new List<NavMesh.AddNodeRequest>(nodes);
        //     _navMesh.EnsureValidTriangulation(result);
        //
        //     DrawOffset(nodes[0].Triangle, Color.green);
        //     DrawOffset(nodes[1].Triangle, Color.green);
        //     DrawOffset(nodes[2].Triangle, Color.red);
        //     Draw(result);
        //
        //     result.Should().BeEquivalentTo(nodes);
        // }
        //
        // [Test]
        // public void EnsureValidTriangulation_ShouldMergeOverlappingTriangles()
        // {
        //     //    Y
        //     //    ▲       (1,2)  (2,2)
        //     //  2 |        *    * 
        //     //    |       / \  / \
        //     //    |      /   \/   \
        //     //    |     /    /\    \
        //     //    |    /    /  \    \
        //     //  0 |   *----*----*-----*
        //     //    |  (0,0)    (2,0)   (3,0)
        //     //    |
        //     //    |
        //     //    └──────────────────────────────▶ X
        //     //       0     1     2     3
        //
        //     var nodes = new List<NavMesh.AddNodeRequest>
        //     {
        //         new() { Triangle = new(new(0, 0), new(2, 0), new(1, 2)), ObstacleId = RegisterEmptyObstacle() },
        //         new() { Triangle = new(new(1, 0), new(3, 0), new(2, 2)), ObstacleId = RegisterEmptyObstacle() },
        //     };
        //
        //     var result = new List<NavMesh.AddNodeRequest>(nodes);
        //     _navMesh.EnsureValidTriangulation(result);
        //
        //     DrawOffset(nodes[0].Triangle, Color.green);
        //     DrawOffset(nodes[1].Triangle, Color.red);
        //     Draw(result);
        //
        //     result.Should().HaveCount(5);
        //     result.Where(n => n.ObstacleId == nodes[0].ObstacleId).Should().HaveCount(2);
        //     result.Where(n => n.ObstacleId == nodes[1].ObstacleId).Should().HaveCount(2);
        //
        //     var merged = result.Where(n => n.ObstacleId != nodes[0].ObstacleId && n.ObstacleId != nodes[1].ObstacleId).ToArray();
        //     merged.Should().HaveCount(1);
        //     _navMesh.GetObstacleIndexes(merged.First().ObstacleId).Should().Contain(
        //         _navMesh.GetObstacleIndexes(nodes[0].ObstacleId)
        //                 .Union(_navMesh.GetObstacleIndexes(nodes[1].ObstacleId)));
        // }
        //
        // [Test]
        // public void EnsureValidTriangulation_ShouldMergeOverlappingTriangles_MultipleTimes()
        // {
        //     //    Y                    /
        //     //    ▲      (1,2) (2,2)  /      |
        //     //  2 |        *    *    /       |
        //     //    |       / \  / \  /        |
        //     //    |      /   \/   \/         |
        //     //    |     /    /\ *--\---------*
        //     //    |    /    /  \    \
        //     //  0 |   *----*----*-----*
        //     //    |  (0,0)    (2,0)   (3,0)
        //     //    └──────────────────────────────▶ X
        //     //       0     1     2     3     4
        //
        //     var nodes = new List<NavMesh.AddNodeRequest>
        //     {
        //         new() { Triangle = new(new(0, 0), new(2, 0), new(1, 2)), ObstacleId = RegisterEmptyObstacle() },
        //         new() { Triangle = new(new(1, 0), new(3, 0), new(2, 2)), ObstacleId = RegisterEmptyObstacle() },
        //         new() { Triangle = new(new(2, 1), new(4, 1), new(4, 4)), ObstacleId = RegisterEmptyObstacle() },
        //     };
        //
        //     var result = new List<NavMesh.AddNodeRequest>(nodes);
        //     _navMesh.EnsureValidTriangulation(result);
        //
        //     DrawOffset(nodes[0].Triangle, Color.green);
        //     DrawOffset(nodes[1].Triangle, Color.red);
        //     DrawOffset(nodes[2].Triangle, Color.magenta);
        //     Draw(result);
        //
        //     result.Should().HaveCount(11);
        //     result.Where(n => n.ObstacleId == nodes[0].ObstacleId).Should().HaveCount(2);
        //     result.Where(n => n.ObstacleId == nodes[1].ObstacleId).Should().HaveCount(5);
        //     result.Where(n => n.ObstacleId == nodes[2].ObstacleId).Should().HaveCount(2);
        //
        //     var merged = result.Where(n =>
        //                            n.ObstacleId != nodes[0].ObstacleId && n.ObstacleId != nodes[1].ObstacleId &&
        //                            n.ObstacleId != nodes[2].ObstacleId)
        //                        .ToArray();
        //     merged.Should().HaveCount(2);
        //     _navMesh.GetObstacleIndexes(merged[0].ObstacleId).Should().HaveCount(2);
        //     _navMesh.GetObstacleIndexes(merged[1].ObstacleId).Should().HaveCount(2);
        // }

        // [Test]
        // public void Merge_ShouldReduceTwoCollinearTriangles_ToOneBiggerTriangle()
        // {
        //     // Arrange: two triangles sharing edge (2,0)-(1,1)
        //     var tris = new List<NavMesh.AddNodeRequest>()
        //     {
        //         new()
        //         {
        //             Triangle = new(new(0, 0), new(2, 0), new(1, 1)),
        //             ObstacleId = 1
        //         },
        //         new()
        //         {
        //             Triangle = new(new(1, 1), new(2, 2), new(2, 0)),
        //             ObstacleId = 1
        //         },
        //     };
        //
        //     DrawOffset(tris[0].Triangle, Color.green);
        //     DrawOffset(tris[1].Triangle, Color.red);
        //     
        //     // Act
        //     NavMesh.TryMergeTriangles(tris);
        //     
        //     Draw(tris);
        //     
        //     // Assert: should now be only one triangle
        //     tris.Should().HaveCount(1);
        //     var result = tris[0];
        //     
        //     var verts = new HashSet<float2>(result.Triangle.Vertices);
        //     verts.Should().BeEquivalentTo(new[]
        //     {
        //         new float2(0, 0),
        //         new float2(2, 0),
        //         new float2(2, 2)
        //     });
        // }
        //
        // [Test]
        // public void Merge_ShouldNotMergeTwoCollinearTriangles_WhenHaveDifferentId()
        // {
        //     // Arrange: two triangles sharing edge (2,0)-(1,1)
        //     var tris = new List<NavMesh.AddNodeRequest>()
        //     {
        //         new()
        //         {
        //             Triangle = new(new(0, 0), new(2, 0), new(1, 1)),
        //             ObstacleId = 1
        //         },
        //         new()
        //         {
        //             Triangle = new(new(1, 1), new(2, 2), new(2, 0)),
        //             ObstacleId = 2
        //         },
        //     };
        //
        //     DrawOffset(tris[0].Triangle, Color.green);
        //     DrawOffset(tris[1].Triangle, Color.red);
        //     
        //     // Act
        //     NavMesh.TryMergeTriangles(tris);
        //     
        //     Draw(tris);
        //     
        //     // Assert
        //     tris.Should().HaveCount(2);
        // }
        //
        // [Test]
        // public void Merge_ShouldMergeMultipleCollinearTriangles()
        // {
        //     // Arrange: 3 small collinear triangles forming a big one
        //     var tris = new List<NavMesh.AddNodeRequest>()
        //     {
        //         new()
        //         {
        //             Triangle = new(new float2(0, 0), new float2(4, 0), new float2(2, 2)),
        //             ObstacleId = 1
        //         },
        //         new()
        //         {
        //             Triangle = new(new float2(2, 2), new float2(4, 2), new float2(4, 0)),
        //             ObstacleId = 1
        //         },
        //         new()
        //         {
        //             Triangle = new(new(2, 2), new(4, 2), new(3, 3)),
        //             ObstacleId = 1
        //         },
        //         new()
        //         {
        //             Triangle = new(new(3, 3), new(4, 4), new(4, 2)),
        //             ObstacleId = 1
        //         },
        //     };
        //
        //     DrawOffset(tris[0].Triangle, Color.green);
        //     DrawOffset(tris[1].Triangle, Color.red);
        //     DrawOffset(tris[2].Triangle, Color.magenta);
        //     DrawOffset(tris[3].Triangle, Color.blue);
        //     
        //     // Act
        //     NavMesh.TryMergeTriangles(tris);
        //
        //     Draw(tris);
        //     
        //     // Assert: should collapse into one large triangle
        //     tris.Should().HaveCount(1);
        // }
        //
        // [Test]
        // public void Merge_ShouldNotChange_WhenNotCollinear()
        // {
        //     // Arrange: two triangles sharing edge but not collinear
        //     var tris = new List<NavMesh.AddNodeRequest>()
        //     {
        //         new()
        //         {
        //             Triangle = new(new float2(0, 0), new float2(1, 0), new float2(0, 1)),
        //             ObstacleId = 1
        //         },
        //         new()
        //         {
        //             Triangle = new(new float2(0, 1), new float2(1, 0), new float2(1, 1)),
        //             ObstacleId = 1
        //         },
        //     };
        //
        //     DrawOffset(tris[0].Triangle, Color.green);
        //     DrawOffset(tris[1].Triangle, Color.red);
        //     
        //     // Act
        //     NavMesh.TryMergeTriangles(tris);
        //
        //     Draw(tris);
        //     
        //     // Assert: still 2 triangles
        //     tris.Should().HaveCount(2);
        //     tris.Should().ContainEquivalentOf(tris[0]);
        //     tris.Should().ContainEquivalentOf(tris[1]);
        // }
        //
        // [Test]
        // public void Merge_ShouldMergeNotCollinearMultipleTriangles()
        // {
        //     // Arrange: 3 small collinear triangles forming a big one
        //     var tris = new List<NavMesh.AddNodeRequest>()
        //     {
        //         new()
        //         {
        //             Triangle = new(new float2(0, 0), new float2(1, 0), new float2(0, 1)),
        //             ObstacleId = 1
        //         },
        //         new()
        //         {
        //             Triangle = new(new float2(0, 1), new float2(1, 0), new float2(2, 1)),
        //             ObstacleId = 1
        //         },
        //         new()
        //         {
        //             Triangle = new(new float2(2, 1), new float2(1, 0), new float2(3, 0)),
        //             ObstacleId = 1
        //         },
        //         new()
        //         {
        //             Triangle = new(new float2(0, 1), new float2(2, 1), new float2(0, 3)),
        //             ObstacleId = 1
        //         },
        //     };
        //
        //     DrawOffset(tris[0].Triangle, Color.green);
        //     DrawOffset(tris[1].Triangle, Color.red);
        //     DrawOffset(tris[2].Triangle, Color.magenta);
        //     DrawOffset(tris[3].Triangle, Color.blue);
        //     
        //     // Act
        //     NavMesh.TryMergeTriangles(tris);
        //
        //     Draw(tris);
        //     
        //     // Assert: should collapse into one large triangle
        //     tris.Should().HaveCount(1);
        // }

        private NavMesh _navMesh;
        private NavObstacles _navObstacles;

        [SetUp]
        public void SetUp()
        {
            //    Y
            //    ▲
            // 10 |     *─────────────────────────────*
            //  9 |     | \                           |  
            //  8 |     |    \                        | 
            //  7 |     |       \                     | 
            //  6 |     |          \                  | 
            //  5 |     |              \              | 
            //  4 |     |                  \          | 
            //  3 |     |                     \       | 
            //  2 |     |                         \   | 
            //  1 |     |                            \|
            //  0 |     *─────────────────────────────*
            //    |
            //    └─────────────────────────────────────▶ X
            //          0  1  2  3  4  5  6  7  8  9 10

            _navMesh = new NavMesh(1);
            _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(10, 0), new(0, 10)));
            _navMesh.AddNode(NodeFromTriangle(new(0, 10), new(10, 10), new(10, 0)));

            _navObstacles = new NavObstacles(1);
        }

        [TearDown]
        public void TearDown()
        {
            _navMesh.Dispose();
            _navObstacles.Dispose();
        }

        [Test]
        public void AddObstacle_ShouldRemoveOriginalTriangle()
        {
            // Arrange
            int obstacleId = _navObstacles.AddObstacle(new float2(2, 1), new float2(4, 1), new float2(3, 3));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(3, 2), out int index);
            found.Should().BeTrue();
            // _navMesh.Nodes[index].CombinedId.Should().Be(obstacleId);
            // _navMesh.Nodes.Where(node => node.CombinedId == obstacleId).Should().HaveCount(1);
            _navMesh.GetActiveNodes.Should().HaveCount(8);
        }

        [Test]
        public void AddObstacle_ShouldCreateCorrectNodes_ForComplexObstacle()
        {
            //    Y
            //    ▲
            //  4 |            * (7,4)
            //    |           / \
            //  3 |          /   \
            //    |         /     \
            //  2 |  (6,2) *       * (8,2)
            //    |         \     /
            //    |          \   /
            //    |           \ /
            //  1 |            * (7,1)
            //    └───────────────────▶ X
            //           6     7     8

            // Arrange
            int obstacleId = _navObstacles.AddObstacle(new(6, 2), new(7, 4), new(8, 2), new(7, 1));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 2), out int index);
            found.Should().BeTrue();
            // _navMesh.Nodes[index].CombinedId.Should().Be(obstacleId);
            // _navMesh.Nodes.Where(node => node.CombinedId == obstacleId).Should().HaveCount(2);
            _navMesh.GetActiveNodes.Should().HaveCount(10);
        }

        [Test]
        public void AddObstacle_ShouldCreateCorrectNodes_ForObstaclesWithCommonEdge()
        {
            //    Y
            //    ▲
            //  4 |            * (7,4)
            //    |           / \
            //  3 |          /   \
            //    |         /     \
            //  2 |  (6,2) *-------* (8,2)
            //    |         \     /
            //    |          \   /
            //    |           \ /
            //  1 |            * (7,1)
            //    └───────────────────▶ X
            //           6     7     8

            // Arrange
            int obstacle1Id = _navObstacles.AddObstacle(new(6, 2), new(8, 2), new(7, 4));
            int obstacle2Id = _navObstacles.AddObstacle(new(6, 2), new(7, 1), new(8, 2));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 2), out int index);
            found.Should().BeTrue();
            // _navMesh.Nodes[index].CombinedId.Should().Be(obstacleId);
            // _navMesh.Nodes.Where(node => node.CombinedId == obstacleId).Should().HaveCount(2);
            _navMesh.GetActiveNodes.Should().HaveCount(10);
        }

        [Test]
        public void AddObstacle_ShouldCreateCorrectNodes_ForObstacleWithPointOnTheNavEdge()
        {
            //    Y
            //    ▲
            //  2 |  (6,2) *-------* (8,2)
            //    |         \     /
            //  1 |          \   /
            //    |           \ /
            //  0 | -----------*--------- <- nav area edge
            //    └───────────────────▶ X
            //           6     7     8

            // Arrange
            int obstacleId = _navObstacles.AddObstacle(new(6, 2), new(7, 0), new(8, 2));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 1), out int index);
            found.Should().BeTrue();
            // _navMesh.Nodes[index].CombinedId.Should().Be(obstacleId);
            // _navMesh.Nodes.Where(node => node.CombinedId == obstacleId).Should().HaveCount(1);
            _navMesh.GetActiveNodes.Should().HaveCount(7);
        }

        [Test]
        public void AddObstacle_ShouldCreateCorrectNodes_ForObstacleWithEdgeOnTheNavEdge()
        {
            //    Y
            //    ▲
            //  2 |      (7,2) *
            //    |           / \
            //  1 |          /   \
            //    |         /     \
            //  0 | -------*-------*----- <- nav area edge
            //    └───────────────────▶ X
            //             6   7   8

            // Arrange
            int obstacleId = _navObstacles.AddObstacle(new(6, 0), new(8, 0), new(7, 2));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 1), out int index);
            found.Should().BeTrue();
            // _navMesh.Nodes[index].CombinedId.Should().Be(obstacleId);
            // _navMesh.Nodes.Where(node => node.CombinedId == obstacleId).Should().HaveCount(1);
            _navMesh.GetActiveNodes.Should().HaveCount(6);
        }

        [Test]
        public void AddObstacle_ShouldCreateCorrectNodes_ForObstacleWithEVertexOutsideNavArea()
        {
            //    Y
            //    ▲
            //  2 |  (6,2) *-------* (8,2)
            //    |         \     /
            //  0 |----------\---/-------- <- navigation area edge
            //    |           \ /
            // -2 |            * (7,-2)
            //    └───────────────────▶ X
            //           6     7     8

            // Arrange
            int obstacleId = _navObstacles.AddObstacle(new(6, 2), new(7, -2), new(8, 2));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 1), out int index);
            found.Should().BeTrue();
            // _navMesh.Nodes[index].CombinedId.Should().Be(NavNode.NULL_INDEX);
            _navMesh.GetActiveNodes.Should().HaveCount(8);
        }

        [Test]
        public void AddObstacle_ShouldCreateCorrectNodes_ForObstacleWithEdgeOutsideNavArea()
        {
            //    Y
            //    ▲
            //  2 |            * (7,2)
            //    |           / \
            //  0 |----------/---\-------- <- navigation area edge
            //    |         /     \
            // -2 | (6,-2) *-------* (8,-2)
            //    └───────────────────▶ X
            //            6    7   8

            // Arrange
            int obstacleId = _navObstacles.AddObstacle(new(6, -2), new(8, -2), new(7, 2));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 1), out int index);
            found.Should().BeTrue();
            // _navMesh.Nodes[index].CombinedId.Should().Be(NavNode.NULL_INDEX);
            _navMesh.GetActiveNodes.Should().HaveCount(6);
        }

        [Test]
        public void AddObstacle__ShouldCreateCorrectNodes_ForObstacleWithEdgesOutsideNavArea_AndContainNavAreaVertex()
        {
            //    Y
            //    ▲
            //  3 |            |   * (4,3)
            //    |            | / |
            //  2 |            |/  |
            //    |           /|   |
            //  0 |          / *---|------ <- edge vertex
            //    |         /      |
            // -1 |(-4,-1) *-------* (4,-1)
            //    └───────────────────▶ X
            //            -4   0   4

            // Arrange
            int obstacleId = _navObstacles.AddObstacle(new(-4, -1), new(4, -1), new(4, 3));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);
            new Triangle(new(-4, -1), new(4, -1), new(4, 3)).DrawBorder(Color.red, 5);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(1, 1), out int index);
            found.Should().BeTrue();
            // _navMesh.Nodes[index].CombinedId.Should().Be(NavNode.NULL_INDEX);
            _navMesh.GetActiveNodes.Should().HaveCount(6);
        }

        [Test]
        public void AddObstacle__ShouldCreateCorrectNodes_ForObstacleWithEdgesOutsideNavArea_AndContainNavAreaVertex_AndVertOnEdge()
        {
            //    Y
            //    ▲            |
            //  2 |            * (0,4)
            //    |           /|\
            //  0 |          / *-\----- <- edge vertex
            //    |         /     \
            // -2 |(-2,-2) *-------* (4,-2)
            //    └───────────────────▶ X
            //            -2   0   4

            // Arrange
            int obstacleId = _navObstacles.AddObstacle(new(-2, -2), new(4, -2), new(0, 4));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);
            new Triangle(new(-2, -2), new(4, -2), new(0, 4)).DrawBorder(Color.red, 5);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(1, 1), out int index);
            found.Should().BeTrue();
            // _navMesh.Nodes[index].CombinedId.Should().Be(NavNode.NULL_INDEX);
            _navMesh.GetActiveNodes.Should().HaveCount(4);
        }

        [Test]
        public void RemoveObstacle_RevertsObstacleRegion()
        {
            // Arrange
            int obstacleId = _navObstacles.AddObstacle(new float2(2, 1), new float2(4, 1), new float2(3, 3));
            RunUpdate();

            // Act
            _navObstacles.RemoveObstacle(obstacleId);
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(3, 2), out int index);
            found.Should().BeTrue();
            // _navMesh.Nodes[index].CombinedId.Should().Be(NavNode.NULL_INDEX);
            _navMesh.GetActiveNodes.Should().HaveCount(2);
        }

        [Test]
        public void AddAndRemoveObstacle_RevertsObstacleRegion_KeepingValidBorder()
        {
            //    Y
            //    ▲
            // 10 |     *─────────────────────────────*
            //  9 |     | \                           | \
            //  8 |     |    \                        |  \
            //  7 |     |       \                     |   \
            //  6 |     |          \                  |    \
            //  5 |     |              \              |     \
            //  4 |     |                  \          |       \
            //  3 |     |                     \       |  t3    \
            //  2 |     |                         \   |         \
            //  1 |     |                            \|          \
            //  0 |     *─────────────────────────────*           \
            // -1 |     |                           /   -           \
            // -2 |     |                        /        --         \
            // -3 |     |         t1          /              -        \
            // -4 |     |                 /                    --      \
            // -5 |     |             /                           -     \
            // -6 |     |          /           t2         obst-->   --    \
            // -7 |     |       /                                     -    \
            // -8 |     |    /                                         --   \
            // -9 |     | /                                               -  \
            //-10 |     *─────────────────────────────────────────────────────*
            //    |
            //    └─────────────────────────────────────────────────────────────▶ X
            //          0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 18

            // Arrange
            _navMesh.AddNode(NodeFromTriangle(new(0, -10), new(10, 0), new(0, 0))); // t1
            _navMesh.AddNode(NodeFromTriangle(new(0, -10), new(18, -10), new(10, 0))); // t2
            _navMesh.AddNode(NodeFromTriangle(new(18, -10), new(10, 10), new(10, 0))); // t3
            
            // Act Add
            int obstacleId = _navObstacles.AddObstacle(new float2(15, -9), new float2(15, -4), new float2(17, -8)); // obst
            RunUpdate(new(15, -9), new(17, -4));
            
            Draw(_navMesh.Nodes, 15);
            
            // Assert Add
            {
                bool found = _navMesh.TryGetNodeIndex(new float2(16, -8), out int index);
                found.Should().BeTrue();
                // _navMesh.Nodes[index].CombinedId.Should().Be(obstacleId);
                _navMesh.GetActiveNodes.Should().HaveCount(11);
            }

            // Act Remove
            _navObstacles.RemoveObstacle(obstacleId);
            RunUpdate(new(15, -9), new(17, -4));

            Draw(_navMesh.Nodes);
            
            // Assert remove
            {
                bool found = _navMesh.TryGetNodeIndex(new float2(16, -8), out int index);
                found.Should().BeTrue();
                // navMesh.Nodes[index].CombinedId.Should().Be(NavNode.NULL_INDEX);
                _navMesh.GetActiveNodes.Should().HaveCount(5);
            }
        }

        [Test]
        public void AddAndRemoveMultipleObstacles_ShouldUpdateNavMeshCorrectly()
        {
            // Arrange: two separated obstacles

            //    Y
            //    ▲
            //  4 |                                         * (7,4)
            //    |                                        / \
            //  3 |       * (2,3)                         /   \
            //    |      / \                             /     \
            //  2 |     /   \                     (6,2) *       * (8,2)
            //    |    /     \                           \     /
            //  1 |   *-------*                           \   /
            //    |  (1,1)    (3,1)                        \ /
            //  0 |                                         * (7,0)
            //    |
            //    └────────────────────────────────────────────────────▶ X
            //          1     2     3     4     5     6     7     8

            // Act: add both obstacles
            int id1 = _navObstacles.AddObstacle(new(1, 1), new(3, 1), new(2, 3));
            int id2 = _navObstacles.AddObstacle(new(6, 2), new(7, 0), new(8, 2), new(7, 4));
            RunUpdate();
            
            Draw(_navMesh.Nodes, 15);
            
            id1.Should().Be(0);
            id2.Should().Be(1);

            // Assert: check a point inside each obstacle returns the correct ID
            _navMesh.TryGetNodeIndex(new float2(2, 2), out int index1).Should().BeTrue();
            // _navMesh.Nodes[index1].CombinedId.Should().Be(id1);

            _navMesh.TryGetNodeIndex(new float2(7, 1), out int index2).Should().BeTrue();
            // _navMesh.Nodes[index2].CombinedId.Should().Be(id2);

            // Act: remove the second obstacle
            _navObstacles.RemoveObstacle(id2);
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert:
            // - id2 area is restored (no obstacle there)
            // - id1 area is untouched
            _navMesh.TryGetNodeIndex(new float2(7, 1), out int restoredIndex).Should().BeTrue();
            // _navMesh.Nodes[restoredIndex].CombinedId.Should().Be(NavNode.NULL_INDEX);

            _navMesh.TryGetNodeIndex(new float2(2, 2), out int stillBlockedIndex).Should().BeTrue();
            // _navMesh.Nodes[stillBlockedIndex].CombinedId.Should().Be(id1);

            _navMesh.GetActiveNodes.Should().HaveCount(8);
        }

        #region Helpers

        private void Draw(List<Triangle> triangles)
        {
            if (!debug)
            {
                return;
            }

            foreach (var tr in triangles)
            {
                tr.DrawBorder(Color.white, 3);
                tr.GetCenter.To3D().DrawPoint(Color.white, 3, .1f);
            }
        }

        private void DrawOffset(Triangle tr, Color color)
        {
            if (!debug)
            {
                return;
            }

            float2 offset = new float2(10, 0);
            new Triangle(tr.A + offset, tr.B + offset, tr.C + offset).DrawBorder(color, 5);
        }

        private void Draw(NativeArray<NavNode> nodes, float offsetX) => Draw(nodes, new float2(offsetX, 0));
        private void Draw(NativeArray<NavNode> nodes, float2 offset = default)
        {
            if (!debug)
            {
                return;
            }

            foreach (var node in nodes)
            {
                new Triangle(node.Triangle.A + offset, node.Triangle.B + offset, node.Triangle.C + offset).DrawBorder(Color.white, 3);
                (node.Center + offset).To3D().DrawPoint(Color.red, 3, .1f);
            }
        }

        private void Draw(List<NavMesh.AddNodeRequest> nodes)
        {
            if (!debug)
            {
                return;
            }

            Debug.Log($"Nodes: {nodes.Count}");
            foreach (var node in nodes)
            {
                node.Triangle.DrawBorder(Color.white, 3);
                node.Triangle.GetCenter.To3D().DrawPoint(Color.red, 3, .1f);
                Debug.Log(node);
                Debug.Log($"Center {node.Triangle.GetCenter}");
            }

            foreach (var node in nodes)
            {
                Debug.DrawLine(node.Triangle.GetCenter.To3D(), node.Triangle.A.To3D(), Color.yellow, 3);
                Debug.DrawLine(node.Triangle.GetCenter.To3D(), node.Triangle.B.To3D(), Color.yellow, 3);
                Debug.DrawLine(node.Triangle.GetCenter.To3D(), node.Triangle.C.To3D(), Color.yellow, 3);
            }
        }

        private static NavMesh.AddNodeRequest NodeFromTriangle(float2 a, float2 b, float2 c) =>
            new NavMesh.AddNodeRequest { Triangle = new(a, b, c) };

        private void RunUpdate() => RunUpdate(new float2(-20, -20), new float2(20, 20));

        private void RunUpdate(float2 min, float2 max)
        {
            new NaveMeshUpdateJob
            {
                NavMesh = _navMesh,
                NavObstacles = _navObstacles,
                UpdateMin = min,
                UpdateMax = max,
            }.Run();
        }

        #endregion
    }
}