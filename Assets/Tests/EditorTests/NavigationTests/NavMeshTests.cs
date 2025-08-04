using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HCore.Extensions;
using Navigation;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using HCore.Shapes;
using Unity.Collections;
using Triangle = Navigation.Triangle;

namespace Tests.EditorTests.NavigationTests
{
    public class NavMeshTests
    {
        private NavMesh _navMesh;
        private readonly List<float2> _basePoints =  new()
        {
            new(0, 0),
            new(10, 0),
            new(5, 10)
        };

        private bool debug = true;

        [SetUp]
        public void SetUp()
        {
            _navMesh = new NavMesh(_basePoints);
        }

        [TearDown]
        public void TearDown()
        {
            _navMesh.Dispose();
        }

        #region Obstacle merging

        [Test]
        public void EnsureValidTriangulation_ShouldNotChange_WhenNoIntersection()
        {
            var nodes = new List<NavMesh.AddNodeRequest>
            {
                new() { Triangle = new(new(0, 0), new (2, 0), new (1, 2)), ObstacleIndex = 1 },
                new() { Triangle = new(new(2, 0), new (4, 0), new (3, 2)), ObstacleIndex = 2 },
                new() { Triangle = new(new(2, 0), new (3, 2), new (1, 2)), ObstacleIndex = 3 },
            };
            
            var result = new List<NavMesh.AddNodeRequest>(nodes);
            NavMesh.EnsureValidTriangulation(result);
            
            DrawOffset(nodes[0].Triangle, Color.green);
            DrawOffset(nodes[1].Triangle, Color.green);
            DrawOffset(nodes[2].Triangle, Color.red);
            Draw(result);
            
            result.Should().BeEquivalentTo(nodes);
        }
        
        [Test]
        public void EnsureValidTriangulation_ShouldMergeOverlappingTriangles()
        {
            //    Y
            //    ▲       (1,2)  (2,2)
            //  2 |        *    * 
            //    |       / \  / \
            //    |      /   \/   \
            //    |     /    /\    \
            //    |    /    /  \    \
            //  0 |   *----*----*-----*
            //    |  (0,0)    (2,0)   (3,0)
            //    |
            //    |
            //    └──────────────────────────────▶ X
            //       0     1     2     3
            
            var nodes = new List<NavMesh.AddNodeRequest>
            {
                new() { Triangle = new(new(0, 0), new (2, 0), new (1, 2)), ObstacleIndex = 1 },
                new() { Triangle = new(new(1, 0), new (3, 0), new (2, 2)), ObstacleIndex = 2 },
            };
            
            var result = new List<NavMesh.AddNodeRequest>(nodes);
            NavMesh.EnsureValidTriangulation(result);
            
            DrawOffset(nodes[0].Triangle, Color.green);
            DrawOffset(nodes[1].Triangle, Color.red);
            Draw(result);
            
            result.Should().HaveCount(5);
            result.Where(n => n.ObstacleIndex == 1).Should().HaveCountGreaterOrEqualTo(2);
            result.Where(n => n.ObstacleIndex == 2).Should().HaveCountGreaterOrEqualTo(2);
        }
        
        [Test]
        public void EnsureValidTriangulation_ShouldMergeOverlappingTriangles_MultipleTimes()
        {                               
            //    Y                    /
            //    ▲      (1,2) (2,2)  /      |
            //  2 |        *    *    /       |
            //    |       / \  / \  /        |
            //    |      /   \/   \/         |
            //    |     /    /\ *--\---------*
            //    |    /    /  \    \
            //  0 |   *----*----*-----*
            //    |  (0,0)    (2,0)   (3,0)
            //    └──────────────────────────────▶ X
            //       0     1     2     3     4
            
            var nodes = new List<NavMesh.AddNodeRequest>
            {
                new() { Triangle = new(new(0, 0), new (2, 0), new (1, 2)), ObstacleIndex = 1 },
                new() { Triangle = new(new(1, 0), new (3, 0), new (2, 2)), ObstacleIndex = 2 },
                new() { Triangle = new(new(2, 1), new (4, 1), new (4, 4)), ObstacleIndex = 3 },
            };
            
            var result = new List<NavMesh.AddNodeRequest>(nodes);
            NavMesh.EnsureValidTriangulation(result);
            
            DrawOffset(nodes[0].Triangle, Color.green);
            DrawOffset(nodes[1].Triangle, Color.red);
            DrawOffset(nodes[2].Triangle, Color.magenta);
            Draw(result);
            
            result.Should().HaveCount(11);
            result.Where(n => n.ObstacleIndex == 1).Should().HaveCountGreaterOrEqualTo(2);
            result.Where(n => n.ObstacleIndex == 2).Should().HaveCountGreaterOrEqualTo(4);
            result.Where(n => n.ObstacleIndex == 3).Should().HaveCountGreaterOrEqualTo(2);
        }

        #endregion
        
        [Test]
        public void InitializesWithBasicTriangle()
        {
            var nodes = _navMesh.Nodes;
            nodes.Length.Should().Be(1);
        }

        [Test]
        public void TryGetNodeIndex_ReturnsTrueForPointInsideMesh()
        {
            float2 pointInside = new float2(5, 1);
            bool found = _navMesh.TryGetNodeIndex(pointInside, out int index);

            found.Should().BeTrue();
            index.Should().Be(0);
        }

        [Test]
        public void TryGetNodeIndex_ReturnsFalseForPointOutsideMesh()
        {
            float2 pointOutside = new float2(100, 100);
            bool found = _navMesh.TryGetNodeIndex(pointOutside, out int index);

            found.Should().BeFalse();
            index.Should().Be(NavNode.NULL_INDEX);
        }

        [Test]
        public void AddObstacle_RemovesOriginalTriangle()
        {
            // Arrange
            List<Triangle> obstacleParts = new()
            {
                new Triangle(
                    new float2(2, 1),
                    new float2(4, 1),
                    new float2(3, 3)
                )
            };

            // Act
            int obstacleId = _navMesh.AddObstacle(obstacleParts);

            Draw(_navMesh.Nodes);
            
            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(3, 2), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].ConfigIndex.Should().Be(obstacleId);
            _navMesh.Nodes.Where(node => node.ConfigIndex == obstacleId).Should().HaveCount(1);
            _navMesh.GetActiveNodes.Should().HaveCount(7);
        }
        
        [Test]
        public void AddObstacle_ShouldAddComplexObstacle()
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
            List<Triangle> obstacleParts = new()
            {
                new(new(6, 2), new(8, 2), new(7, 4)),
                new(new(6, 2), new(7, 1), new(8, 2))
            };

            // Act
            int obstacleId = _navMesh.AddObstacle(obstacleParts);

            Draw(_navMesh.Nodes);
            
            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 2), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].ConfigIndex.Should().Be(obstacleId);
            _navMesh.Nodes.Where(node => node.ConfigIndex == obstacleId).Should().HaveCount(2);
            _navMesh.GetActiveNodes.Should().HaveCount(9);
        }
        
        [Test]
        public void AddObstacle_ShouldAddObstacleWithPointOnTheNavEdge()
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
            List<Triangle> obstacleParts = new()
            {
                new(new(6, 2), new(7, 0), new(8, 2))
            };

            // Act
            int obstacleId = _navMesh.AddObstacle(obstacleParts);

            Draw(_navMesh.Nodes);
            
            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 1), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].ConfigIndex.Should().Be(obstacleId);
            _navMesh.Nodes.Where(node => node.ConfigIndex == obstacleId).Should().HaveCount(1);
            _navMesh.GetActiveNodes.Should().HaveCount(6);
        }

        [Test]
        public void AddObstacle_ShouldAddObstacleWithEdgesOutsideNavArea()
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
            List<Triangle> obstacleParts = new()
            {
                new(new(6, 2), new(7, -2), new(8, 2))
            };

            // Act
            int obstacleId = _navMesh.AddObstacle(obstacleParts);

            Draw(_navMesh.Nodes);
            
            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 1), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].ConfigIndex.Should().Be(obstacleId);
            _navMesh.GetActiveNodes.Should().HaveCount(6);
        }
        
        [Test]
        public void AddObstacle_ShouldAddObstacleWithEdgesOutsideNavArea_AndContainNavAreaVertex()
        {
            //    Y
            //    ▲
            // 12 |            * (5,12)
            //    |           / \
            // 10 |      ----/-*-\----- <- edge vertex
            //    |         /     \
            //  8 |  (4,8) *-------* (6,8)
            //    └───────────────────▶ X
            //           4     5     6
            
            // Arrange
            List<Triangle> obstacleParts = new()
            {
                new(new(4, 8), new(6, 8), new(5, 12))
            };

            // Act
            int obstacleId = _navMesh.AddObstacle(obstacleParts);

            Draw(_navMesh.Nodes);
            
            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(5, 10), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].ConfigIndex.Should().Be(obstacleId);
            _navMesh.GetActiveNodes.Should().HaveCount(5);
        }

        // TODO: test for valid border
        
        [Test]
        public void RemoveObstacle_RevertsObstacleRegion()
        {
            // Arrange
            var obstacleParts = new List<Triangle>
            {
                new Triangle(
                    new float2(2, 1),
                    new float2(4, 1),
                    new float2(3, 3)
                )
            };
            int obstacleId = _navMesh.AddObstacle(obstacleParts);

            // Act
            _navMesh.RemoveObstacle(obstacleId);

            Draw(_navMesh.Nodes);
            
            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(3, 2), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].ConfigIndex.Should().Be(NavNode.NULL_INDEX);
            _navMesh.GetActiveNodes.Should().HaveCount(1);
        }
        
        // TODO: fix
        /*[Test]
        public void AddAndRemoveMultipleObstacles_ShouldUpdateNavMeshCorrectly()
        {
            // Arrange: two separated obstacles
            
            //    Y
            //    ▲
            //  4 |                                         * (7,4)
            //    |                                        / \
            //  3 |       * (2,3)                         /   \
            //    |      / \                             /     \
            //  2 |     /   \                     (6,2) *-------* (8,2)
            //    |    /     \                           \     /
            //  1 |   *-------*                           \   /
            //    |  (1,1)    (3,1)                        \ /
            //  0 |                                         * (7,0)
            //    |
            //    └────────────────────────────────────────────────────▶ X
            //          1     2     3     4     5     6     7     8
            
            var obstacle1 = new List<Triangle>
            {
                new(new(1, 1), new(3, 1), new(2, 3))
            };
            
            var obstacle2 = new List<Triangle>
            {
                new(new(6, 2), new(8, 2), new(7, 4)),
                new(new(6, 2), new(7, 0), new(8, 2))
            };

            // Act: add both obstacles
            int id1 = _navMesh.AddObstacle(obstacle1);
            int id2 = _navMesh.AddObstacle(obstacle2);
            
            id1.Should().Be(0);
            id2.Should().Be(1);

            // Assert: check a point inside each obstacle returns the correct ID
            _navMesh.TryGetNodeIndex(new float2(2, 2), out int index1).Should().BeTrue();
            _navMesh.Nodes[index1].ConfigIndex.Should().Be(id1);

            _navMesh.TryGetNodeIndex(new float2(7, 1), out int index2).Should().BeTrue();
            _navMesh.Nodes[index2].ConfigIndex.Should().Be(id2);

            // Act: remove the second obstacle
            _navMesh.RemoveObstacle(id2);

            // Assert:
            // - id2 area is restored (no obstacle there)
            // - id1 area is untouched
            _navMesh.TryGetNodeIndex(new float2(7, 1), out int restoredIndex).Should().BeTrue();
            _navMesh.Nodes[restoredIndex].ConfigIndex.Should().Be(NavNode.NULL_INDEX);

            _navMesh.TryGetNodeIndex(new float2(2, 2), out int stillBlockedIndex).Should().BeTrue();
            _navMesh.Nodes[stillBlockedIndex].ConfigIndex.Should().Be(id1);
            
            _navMesh.GetActiveNodes.Should().HaveCount();
        }
        */

        #region Debug utils

        private void DebugTrianglesList(List<Triangle> triangles)
        {
            if (!debug)
            {
                return;
            }
            
            var s = "";
            foreach (var triangle in triangles)
            {
                s += $"\nnew Triangle(new {triangle.A}, new {triangle.B}, new {triangle.C}),";   
            }
            Debug.Log(s);
        }
        private void Draw(Triangle tr, Color color)
        {
            if (!debug)
            {
                return;
            }
            
            new Triangle(tr.A, tr.B, tr.C).DrawBorder(color, 5);
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
        
        private void Draw(NativeArray<NavNode> nodes)
        {
            if (!debug)
            {
                return;
            }
            
            foreach (var node in nodes)
            {
                new Triangle(node.Triangle.A, node.Triangle.B, node.Triangle.C).DrawBorder(Color.white, 3);
                node.Center.To3D().DrawPoint(node.ConfigIndex >= 0 ? Color.red : Color.white, 3, .1f);
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
                node.Triangle.GetCenter.To3D().DrawPoint(node.ObstacleIndex >= 0 ? Color.red : Color.white, 3, .1f);
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

        #endregion
    }
}