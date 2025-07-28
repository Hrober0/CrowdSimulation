using System.Collections.Generic;
using FluentAssertions;
using Navigation;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Tests.EditorTests.NavigationTests
{
    public class NavMeshTests
    {
        private NavMesh _navMesh;

        [SetUp]
        public void SetUp()
        {
            List<Vector2> basePoints = new()
            {
                new Vector2(0, 0),
                new Vector2(10, 0),
                new Vector2(5, 10)
            };

            _navMesh = new NavMesh(basePoints);
        }

        [TearDown]
        public void TearDown()
        {
            _navMesh.Dispose();
        }

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

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(3, 2), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].ConfigIndex.Should().Be(obstacleId);
        }

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

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(3, 2), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].ConfigIndex.Should().Be(NavNode.NULL_INDEX);
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
                new Triangle(new float2(1, 1), new float2(3, 1), new float2(2, 3))
            };
            
            var obstacle2 = new List<Triangle>
            {
                new Triangle(new float2(6, 2), new float2(8, 2), new float2(7, 4)),
                new Triangle(new float2(6, 2), new float2(7, 0), new float2(8, 2))
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
        }
    }
}