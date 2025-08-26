using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HCore;
using HCore.Extensions;
using Navigation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using HCore.Shapes;
using Tests.TestsUtilities;
using Unity.Jobs;
using Triangle = Navigation.Triangle;

namespace Tests.EditorTests.NavigationTests
{
    public class NavigationModificationTests
    {
        private bool debug = true;

        private NavMesh<IdAttribute> _navMesh;
        private NavObstacles<IdAttribute> _navObstacles;

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

            _navMesh = new(1);
            _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(10, 0), new(0, 10)));
            _navMesh.AddNode(NodeFromTriangle(new(0, 10), new(10, 10), new(10, 0)));

            _navObstacles = new(1);
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
            _navObstacles.AddObstacle(new(13), new float2(2, 1), new float2(4, 1), new float2(3, 3));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(3, 2), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].Attributes.GetIds().Should().ContainOnly(13);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(13)).Should().HaveCount(1);
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
            _navObstacles.AddObstacle(new(1), new(6, 2), new(7, 4), new(8, 2), new(7, 1));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 2), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].Attributes.GetIds().Should().ContainOnly(1);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(1)).Should().HaveCount(2);
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
            //    |         /  t1 \
            //  2 |  (6,2) *-------* (8,2)
            //    |         \  t2 /
            //    |          \   /
            //    |           \ /
            //  1 |            * (7,1)
            //    └───────────────────▶ X
            //           6     7     8

            // Arrange
            _navObstacles.AddObstacle(new(1), new(6, 2), new(8, 2), new(7, 4));
            _navObstacles.AddObstacle(new(2), new(6, 2), new(7, 1), new(8, 2));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.TryGetNodeIndex(new float2(7, 3), out int index).Should().BeTrue();
            ;
            _navMesh.Nodes[index].Attributes.GetIds().Should().ContainOnly(1);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(1)).Should().HaveCount(1);

            _navMesh.TryGetNodeIndex(new float2(7, 2), out int index2).Should().BeTrue();
            ;
            _navMesh.Nodes[index2].Attributes.GetIds().Should().ContainOnly(2);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(2)).Should().HaveCount(1);

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
            _navObstacles.AddObstacle(new(1), new(6, 2), new(7, 0), new(8, 2));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 1), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].Attributes.GetIds().Should().ContainOnly(1);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(1)).Should().HaveCount(1);
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
            int obstacleId = _navObstacles.AddObstacle(new(1), new(6, 0), new(8, 0), new(7, 2));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 1), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].Attributes.GetIds().Should().ContainOnly(1);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(1)).Should().HaveCount(1);
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
            _navObstacles.AddObstacle(new(1), new(6, 2), new(7, -2), new(8, 2));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 1), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].Attributes.GetIds().Should().ContainOnly(1);
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
            _navObstacles.AddObstacle(new(1), new(6, -2), new(8, -2), new(7, 2));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(7, 1), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].Attributes.GetIds().Should().ContainOnly(1);
            _navMesh.GetActiveNodes.Should().HaveCount(6);
            _navMesh.TryGetNodeIndex(new float2(7, -1), out int _).Should().BeFalse();
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
            _navObstacles.AddObstacle(new(1), new(-4, -1), new(4, -1), new(4, 3));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);
            new Triangle(new(-4, -1), new(4, -1), new(4, 3)).DrawBorder(Color.red, 5);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(1, 1), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].Attributes.GetIds().Should().ContainOnly(1);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(1)).Should().HaveCount(2);
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
            _navObstacles.AddObstacle(new(1), new(-2, -2), new(4, -2), new(0, 4));

            // Act
            RunUpdate();

            Draw(_navMesh.Nodes);
            new Triangle(new(-2, -2), new(4, -2), new(0, 4)).DrawBorder(Color.red, 5);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(1, 1), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].Attributes.GetIds().Should().ContainOnly(1);
            _navMesh.GetActiveNodes.Should().HaveCount(4);
        }

        [Test]
        public void RemoveObstacle_RevertsObstacleRegion()
        {
            // Arrange
            int obstacleId = _navObstacles.AddObstacle(new(1), new float2(2, 1), new float2(4, 1), new float2(3, 3));
            RunUpdate();

            // Act
            _navObstacles.RemoveObstacle(obstacleId);
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            bool found = _navMesh.TryGetNodeIndex(new float2(3, 2), out int index);
            found.Should().BeTrue();
            _navMesh.Nodes[index].Attributes.Entries.Should().Be(0);
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
            int obstacleId = _navObstacles.AddObstacle(new(1), new float2(15, -9), new float2(15, -4), new float2(17, -8)); // obst
            RunUpdate(new(15, -9), new(17, -4));

            Draw(_navMesh.Nodes, 15);

            // Assert Add
            {
                bool found = _navMesh.TryGetNodeIndex(new float2(16, -8), out int index);
                found.Should().BeTrue();
                _navMesh.Nodes[index].Attributes.GetIds().Should().ContainOnly(1);
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
                _navMesh.Nodes[index].Attributes.Entries.Should().Be(0);
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
            int id1 = _navObstacles.AddObstacle(new(1), new(1, 1), new(3, 1), new(2, 3));
            int id2 = _navObstacles.AddObstacle(new(2), new(6, 2), new(7, 0), new(8, 2), new(7, 4));
            RunUpdate();

            Draw(_navMesh.Nodes, 15);

            // Assert: check a point inside each obstacle returns the correct ID
            _navMesh.TryGetNodeIndex(new float2(2, 2), out int index1).Should().BeTrue();
            _navMesh.Nodes[index1].Attributes.GetIds().Should().ContainOnly(1);

            _navMesh.TryGetNodeIndex(new float2(7, 1), out int index2).Should().BeTrue();
            _navMesh.Nodes[index2].Attributes.GetIds().Should().ContainOnly(2);

            // Act: remove the second obstacle
            _navObstacles.RemoveObstacle(id2);
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert:
            // - id2 area is restored (no obstacle there)
            // - id1 area is untouched
            _navMesh.TryGetNodeIndex(new float2(7, 1), out int restoredIndex).Should().BeTrue();
            _navMesh.Nodes[restoredIndex].Attributes.Entries.Should().Be(0);

            _navMesh.TryGetNodeIndex(new float2(2, 2), out int stillBlockedIndex).Should().BeTrue();
            _navMesh.Nodes[stillBlockedIndex].Attributes.GetIds().Should().ContainOnly(1);

            _navMesh.GetActiveNodes.Should().HaveCount(8);
        }

        [Test]
        public void Update_ShouldMergeOverlappingObstacleAttributes()
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

            // Act
            _navObstacles.AddObstacle(new(1), new(0, 0), new(2, 0), new(1, 2));
            _navObstacles.AddObstacle(new(2), new(1, 0), new(3, 0), new(2, 2));
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(1)).Should().HaveCount(3);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(2)).Should().HaveCount(3);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(1) && node.Attributes.GetIds().Contains(2))
                    .Should().HaveCount(1);
            _navMesh.GetActiveNodes.Should().HaveCount(11);
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

            // Act
            _navObstacles.AddObstacle(new(1), new(0, 0), new(2, 0), new(1, 2));
            _navObstacles.AddObstacle(new(2), new(1, 0), new(3, 0), new(2, 2));
            _navObstacles.AddObstacle(new(3), new(2, 1), new(4, 1), new(4, 4));
            RunUpdate();

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(1)).Should().HaveCount(3);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(2)).Should().HaveCount(7);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(3)).Should().HaveCount(3);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(1) && node.Attributes.GetIds().Contains(2))
                    .Should().HaveCount(1);
            _navMesh.Nodes.Where(node => node.Attributes.GetIds().Contains(2) && node.Attributes.GetIds().Contains(3))
                    .Should().HaveCount(1);
            _navMesh.GetActiveNodes.Should().HaveCount(21);
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

        private void Draw(NativeArray<NavNode<IdAttribute>> nodes, float offsetX) => Draw(nodes, new float2(offsetX, 0));

        private void Draw(NativeArray<NavNode<IdAttribute>> nodes, float2 offset = default)
        {
            if (!debug)
            {
                return;
            }

            foreach (var node in nodes)
            {
                new Triangle(node.Triangle.A + offset, node.Triangle.B + offset, node.Triangle.C + offset).DrawBorder(Color.white, 3);
                var color = node.Attributes.Entries > 0 ? ColorUtils.GetColor(node.Attributes.Entries) : Color.gray;
                (node.Center + offset).To3D().DrawPoint(color, 3, .1f);
            }
        }

        private static NavMesh<IdAttribute>.AddNodeRequest
            NodeFromTriangle(float2 a, float2 b, float2 c, IdAttribute attribute = default) =>
            new(new(a, b, c), attribute);

        private void RunUpdate() => RunUpdate(new float2(-20, -20), new float2(20, 20));

        private void RunUpdate(float2 min, float2 max)
        {
            new NaveMeshUpdateJob<IdAttribute>
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