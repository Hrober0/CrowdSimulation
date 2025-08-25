using System.Collections.Generic;
using FluentAssertions;
using HCore.Extensions;
using Navigation;
using NUnit.Framework;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using HCore.Shapes;
using Triangle = Navigation.Triangle;

namespace Tests.EditorTests.NavigationTests
{
    public class NavMeshTests
    {
        private bool _debug = true;

        private NavMesh _navMesh;

        [SetUp]
        public void SetUp()
        {
            //    Y
            //    ▲
            // 10 |                *     
            //  9 |              /   \ 
            //  8 |             /  C  \
            //  7 |           /         \
            //  6 |          /           \
            //  5 |        /               \
            //  4 |       /                 \
            //  3 |     /                     \
            //  2 |    /                       \
            //  1 |  /   A                   B   \
            //  0 | *─────────────────────────────*
            //    └───────────────────────────────▶ X
            //      0  1  2  3  4  5  6  7  8  9 10
            
            _navMesh = new NavMesh(1);
            _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(10, 0), new(5, 10)));
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
            var pointInside = new float2(5, 1);
            bool found = _navMesh.TryGetNodeIndex(pointInside, out int index);

            found.Should().BeTrue();
            index.Should().Be(0);
        }

        [Test]
        public void TryGetNodeIndex_ReturnsFalseForPointOutsideMesh()
        {
            var pointOutside = new float2(100, 100);
            bool found = _navMesh.TryGetNodeIndex(pointOutside, out int index);

            found.Should().BeFalse();
            index.Should().Be(NavNode.NULL_INDEX);
        }

        [Test]
        public void AddNode_ShouldConnectToExistingNodes_WhenHaveCommonEdge()
        {
            // Arrange
            _navMesh.TryGetNodeIndex(new float2(5, 5), out int connectId);

            // Act: Add triangle connected on (0,0)-(10,0) edge
            var newId = _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(10, 0), new(5, -5)));

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.GetActiveNodes.Should().HaveCount(2);
            var node = _navMesh.Nodes[newId];
            node.ConnectionAB.Should().Be(connectId);
            node.ConnectionCA.Should().Be(NavNode.NULL_INDEX);
            node.ConnectionBC.Should().Be(NavNode.NULL_INDEX);
        }

        [Test]
        public void AddNode_ShouldConnectToExistingNodes_WhenHaveCommonEdge_EvenInside()
        {
            // Arrange
            _navMesh.TryGetNodeIndex(new float2(5, 5), out int connectId);

            // Act: Add triangle connected on (0,0)-(10,0) edge
            var newId = _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(10, 0), new(5, 5)));

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.GetActiveNodes.Should().HaveCount(2);
            var node = _navMesh.Nodes[newId];
            node.ConnectionAB.Should().Be(connectId);
            node.ConnectionCA.Should().Be(NavNode.NULL_INDEX);
            node.ConnectionBC.Should().Be(NavNode.NULL_INDEX);
        }

        [Test]
        public void AddNode_ShouldConnectToExistingNodes_WithEachSide()
        {
            // Arrange
            _navMesh.TryGetNodeIndex(new float2(5, 5), out int connectId);

            // Act
            var newIds = new List<int>()
            {
                _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(10, 0), new(5, -5))), // down by AB
                _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(5, 10), new(-5, 5))), // left by AB 
                _navMesh.AddNode(NodeFromTriangle(new(10, 0), new(5, 10), new(15, 5))), // right by AB
            };

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.GetActiveNodes.Should().HaveCount(4);
            foreach (var id in newIds)
            {
                var node = _navMesh.Nodes[id];
                node.ConnectionAB.Should().Be(connectId);
                node.ConnectionCA.Should().Be(NavNode.NULL_INDEX);
                node.ConnectionBC.Should().Be(NavNode.NULL_INDEX);
            }
        }

        [Test]
        public void AddNode_ShouldConnectToExistingNodes_ByEachEdge()
        {
            // Arrange
            _navMesh.TryGetNodeIndex(new float2(5, 5), out int connectId);

            // Act
            var newIds = new List<int>()
            {
                _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(10, 0), new(5, -5))), // down by AB
                _navMesh.AddNode(NodeFromTriangle(new(5, 10), new(15, 5), new(10, 0))), // right by AC
                _navMesh.AddNode(NodeFromTriangle(new(-5, 5), new(0, 0), new(5, 10))), // left by BC 
            };

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.GetActiveNodes.Should().HaveCount(4);
            {
                var node = _navMesh.Nodes[newIds[0]];
                node.ConnectionAB.Should().Be(connectId);
                node.ConnectionCA.Should().Be(NavNode.NULL_INDEX);
                node.ConnectionBC.Should().Be(NavNode.NULL_INDEX);
            }
            {
                var node = _navMesh.Nodes[newIds[1]];
                node.ConnectionAB.Should().Be(NavNode.NULL_INDEX);
                node.ConnectionCA.Should().Be(connectId);
                node.ConnectionBC.Should().Be(NavNode.NULL_INDEX);
            }
            {
                var node = _navMesh.Nodes[newIds[2]];
                node.ConnectionAB.Should().Be(NavNode.NULL_INDEX);
                node.ConnectionCA.Should().Be(NavNode.NULL_INDEX);
                node.ConnectionBC.Should().Be(connectId);
            }
        }
        
        [Test]
        public void AddNode_ShouldConnectTwoExistingNodes()
        {
            // Arrange
            _navMesh.TryGetNodeIndex(new float2(5, 5), out int connectId);

            var topId = _navMesh.AddNode(NodeFromTriangle(new(5, 10), new(15, 5), new(10, 15))); // top

            DrawOffset(_navMesh.Nodes);
            
            // Act
            var rightId = _navMesh.AddNode(NodeFromTriangle(new(5, 10), new(15, 5), new(10, 0))); // right

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.GetActiveNodes.Should().HaveCount(3);
            {
                var node = _navMesh.Nodes[connectId];
                node.ConnectionAB.Should().Be(NavNode.NULL_INDEX);
                node.ConnectionCA.Should().Be(NavNode.NULL_INDEX);
                node.ConnectionBC.Should().Be(rightId);
            }
            {
                var node = _navMesh.Nodes[rightId];
                node.ConnectionAB.Should().Be(topId);
                node.ConnectionCA.Should().Be(connectId);
                node.ConnectionBC.Should().Be(NavNode.NULL_INDEX);
            }
            {
                var node = _navMesh.Nodes[topId];
                node.ConnectionAB.Should().Be(rightId);
                node.ConnectionCA.Should().Be(NavNode.NULL_INDEX);
                node.ConnectionBC.Should().Be(NavNode.NULL_INDEX);
            }
        }

        [Test]
        public void RemoveNode_ShouldRemoveSingle()
        {
            // Arrange
            _navMesh.TryGetNodeIndex(new float2(5, 5), out int connectId);
            var triangle = _navMesh.Nodes[connectId].Triangle;

            DrawOffset(_navMesh.Nodes);

            // Act
            using var result = new NativeList<Triangle>(16, Allocator.Temp);
            _navMesh.RemoveNodes(new(-10, -10), new(20, 20), result);

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.GetActiveNodes.Should().HaveCount(0);
            result.AsArray().Should().ContainSingle().Which.Should().Be(triangle);
        }

        [Test]
        public void RemoveNode_ShouldRemoveSingle_FromInside()
        {
            DrawOffset(_navMesh.Nodes);

            // Act
            using var result = new NativeList<Triangle>(16, Allocator.Temp);
            _navMesh.RemoveNodes(new(5, 5), new(5, 5), result);

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.GetActiveNodes.Should().HaveCount(0);
        }

        [Test]
        public void RemoveNode_ShouldRemoveAllNodes()
        {
            // Arrange
            var newIds = new List<int>()
            {
                _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(10, 0), new(5, -5))), // down by AB
                _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(5, 10), new(-5, 5))), // left by AB 
                _navMesh.AddNode(NodeFromTriangle(new(10, 0), new(5, 10), new(15, 5))), // right by AB
            };

            DrawOffset(_navMesh.Nodes);

            // Act
            using var result = new NativeList<Triangle>(16, Allocator.Temp);
            _navMesh.RemoveNodes(new(-10, -10), new(20, 20), result);

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.GetActiveNodes.Should().HaveCount(0);
            result.AsArray().Should().HaveCount(4);
        }

        [Test]
        public void RemoveNode_ShouldDisconnectOtherNodesFromIt()
        {
            // Arrange
            _navMesh.TryGetNodeIndex(new float2(5, 5), out int connectId);
            var newIds = new List<int>()
            {
                _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(10, 0), new(5, -5))), // down by AB to AB
                _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(5, 10), new(-5, 5))), // left by AB to AC
                _navMesh.AddNode(NodeFromTriangle(new(10, 0), new(5, 10), new(15, 5))), // right by AB to BC
            };

            DrawOffset(_navMesh.Nodes);

            // Act: remove left node
            using var result = new NativeList<Triangle>(16, Allocator.Temp);
            _navMesh.RemoveNodes(new(-5, 5), new(-5, 2), result);

            Draw(_navMesh.Nodes);
            
            // Assert
            _navMesh.GetActiveNodes.Should().HaveCount(3);
            var centerNode = _navMesh.Nodes[connectId];
            Debug.Log(centerNode);
            centerNode.ConnectionAB.Should().Be(newIds[0]);
            centerNode.ConnectionCA.Should().Be(NavNode.NULL_INDEX);
            centerNode.ConnectionBC.Should().Be(newIds[2]);
        }

        [Test]
        public void RemoveNode_ShouldRemoveNodeAtGiveArea()
        {
            // Arrange
            var newIds = new List<int>()
            {
                _navMesh.AddNode(NodeFromTriangle(new(0, 0), new(10, 0), new(5, -5))), // down by AB
                _navMesh.AddNode(NodeFromTriangle(new(5, 10), new(15, 15), new(10, 0))), // right by AC
                _navMesh.AddNode(NodeFromTriangle(new(-5, 15), new(0, 0), new(5, 10))), // left by BC 
            };

            DrawOffset(_navMesh.Nodes);

            // Act: remove on connection (0,0)-(10,0)
            using var result = new NativeList<Triangle>(16, Allocator.Temp);
            _navMesh.RemoveNodes(new(-5, 12), new(20, 12), result);

            Draw(_navMesh.Nodes);

            // Assert
            _navMesh.GetActiveNodes.Should().HaveCount(2);
            result.AsArray().Should().HaveCount(2);
            result.AsArray().Should().Contain(new Triangle(new(5, 10), new(15, 15), new(10, 0)));
            result.AsArray().Should().Contain(new Triangle(new(-5, 15), new(0, 0), new(5, 10)));
        }

        private void Draw(NativeArray<NavNode> nodes)
        {
            if (!_debug)
            {
                return;
            }

            foreach (var node in nodes)
            {
                if (node.IsEmpty)
                {
                    continue;
                }

                node.Triangle.DrawBorder(Color.white, 3);
                node.Center.To3D().DrawPoint(Color.red, 3, .1f);
            }

            foreach (var node in nodes)
            {
                if (node.IsEmpty)
                {
                    continue;
                }
                
                foreach (var id in EnumExtensions.GetValues<NavNode.EdgeId>())
                {
                    var connectionIndex = node.GetConnectionIndex(id);
                    if (connectionIndex != NavNode.NULL_INDEX)
                    {
                        Debug.DrawLine(node.Center.To3D(), node.GetEdge(id).Center.To3D(), Color.yellow, 3);
                    }
                }
            }
        }

        private void DrawOffset(NativeArray<NavNode> nodes)
        {
            if (!_debug)
            {
                return;
            }

            var offset = new float2(30, 0);
            foreach (var node in nodes)
            {
                if (node.IsEmpty)
                {
                    continue;
                }

                new Triangle(node.Triangle.A + offset, node.Triangle.B + offset, node.Triangle.C + offset).DrawBorder(Color.white, 3);
                (node.Center + offset).To3D().DrawPoint(Color.red, 3, .1f);
            }
        }

        private static NavMesh.AddNodeRequest NodeFromTriangle(float2 a, float2 b, float2 c) =>
            new NavMesh.AddNodeRequest { Triangle = new(a, b, c) };
    }
}