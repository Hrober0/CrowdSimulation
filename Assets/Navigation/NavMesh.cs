using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CustomNativeCollections;
using HCore.Extensions;
using HCore.Shapes;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public struct NavMesh<T> : IDisposable where T : unmanaged, INodeAttributes<T>
    {
        private NativeFixedList<NavNode<T>> _nodes;
        private NativeSpatialHash<int> _nodesPositionLookup; // cell position to node index

        public NativeArray<NavNode<T>> Nodes => _nodes.DirtyList.AsArray();
        public IEnumerable<NavNode<T>> GetActiveNodes => _nodes;
        public bool IsCreated => _nodesPositionLookup.IsCreated;

        public NavMesh(float chunkSize, int capacity = 1024, Allocator allocator = Allocator.Persistent)
        {
            _nodes = new(capacity, allocator);
            _nodesPositionLookup = new(capacity * 2, chunkSize, allocator);
        }

        public void Dispose()
        {
            _nodes.Dispose();
            _nodesPositionLookup.Dispose();
        }

        public bool TryGetNodeIndex(float2 position, out int nodeIndex)
        {
            using var indexes = new NativeList<int>(16, Allocator.Temp);
            _nodesPositionLookup.QueryPoint(position, indexes);
            foreach (var index in indexes)
            {
                NavNode<T> node = _nodes[index];
                if (Triangle.PointIn(position, node.CornerA, node.CornerB, node.CornerC))
                {
                    nodeIndex = index;
                    return true;
                }
            }

            nodeIndex = NavNode.NULL_INDEX;
            return false;
        }

        /// <summary>
        /// Add node to mesh and connect it to existing nodes
        /// </summary>
        public int AddNode(AddNodeRequest addAddNodeRequest)
        {
            // create node
            var newIndex = _nodes.FreeIndex;
            (float2 a, float2 b, float2 c) = addAddNodeRequest.Triangle;
            var newNode = new NavNode<T>(
                a,
                b,
                c,
                connectionAB: TryConnect(a, b, newIndex),
                connectionBC: TryConnect(b, c, newIndex),
                connectionCA: TryConnect(c, a, newIndex),
                addAddNodeRequest.Attributes
            );

            // add to array
            // Debug.Log($"Add new node to index: {newIndex} / {_nodes.Length}");
            // newNode.DrawBorder(Color.green, 1);
            _nodes.Add(newNode);

            // add to position lookup
            (float2 min, float2 max) = addAddNodeRequest.Triangle.GetBounds();
            _nodesPositionLookup.AddAABB(min, max, newIndex);

            return newIndex;
        }

        /// <summary>
        /// Removes and disconnects nodes on given area
        /// </summary>
        /// <param name="min">left bottom area point</param>
        /// <param name="max">right top area point</param>
        /// <param name="removedNodes"></param>
        /// <returns>Removed nodes</returns>
        public void RemoveNodes(float2 min, float2 max, NativeList<Triangle> removedNodes)
        {
            using var nodeIndexes = new NativeList<int>(128, Allocator.Temp);
            _nodesPositionLookup.QueryAABB(min, max, nodeIndexes);

            for (var index = 0; index < nodeIndexes.Length; index++)
            {
                int nodeIndex = nodeIndexes[index];
                NavNode<T> node = _nodes[nodeIndex];

                if (node.IsEmpty)
                {
                    continue;
                }

                // Debug.Log($"Removing node: {nodeIndex} {node}");
                // node.DrawBorder(Color.red, 1);
                // DebugCell(cell);

                _nodes[nodeIndex] = NavNode<T>.Empty;
                _nodes.RemoveAt(nodeIndex);

                Triangle nodeTr = node.Triangle;
                removedNodes.Add(nodeTr);

                // disconnect AB
                {
                    var edge = new EdgeKey(node.CornerA, node.CornerB);
                    if (node.ConnectionAB != NavNode.NULL_INDEX)
                    {
                        SetConnectionWithEdge(node.ConnectionAB, edge, NavNode.NULL_INDEX);
                    }
                }

                // disconnect AC
                {
                    var edge = new EdgeKey(node.CornerA, node.CornerC);
                    if (node.ConnectionCA != NavNode.NULL_INDEX)
                    {
                        SetConnectionWithEdge(node.ConnectionCA, edge, NavNode.NULL_INDEX);
                    }
                }

                // disconnect BC
                {
                    var edge = new EdgeKey(node.CornerB, node.CornerC);
                    if (node.ConnectionBC != NavNode.NULL_INDEX)
                    {
                        SetConnectionWithEdge(node.ConnectionBC, edge, NavNode.NULL_INDEX);
                    }
                }

                // remove from lookup
                _nodesPositionLookup.RemoveAABB(nodeTr.Min, nodeTr.Max, nodeIndex);
            }
        }

        /// <summary>
        /// Set connection in node with common edge
        /// </summary>
        /// <param name="a">edge start</param>
        /// <param name="b">edge end</param>
        /// <param name="newIndex"></param>
        /// <returns>Connected node index or -1 when common edge not found</returns>
        private int TryConnect(float2 a, float2 b, int newIndex)
        {
            var edge = new EdgeKey(a, b);
            
            using var nodeIndexes = new NativeList<int>(16, Allocator.Temp);
            _nodesPositionLookup.QueryAABB(a, b, nodeIndexes);

            foreach (var targetIndex in nodeIndexes)
            {
                if (TrySetConnectionWithEdge(targetIndex, edge, newIndex))
                {
                    return targetIndex;
                }
            }
            
            return NavNode.NULL_INDEX;
        }

        private bool TrySetConnectionWithEdge(int nodeIndex, EdgeKey edge, int targetIndex)
        {
            NavNode<T> node = _nodes[nodeIndex];

            if (IsSameEdge(edge, node.CornerA, node.CornerB))
            {
                node.ConnectionAB = targetIndex;
            }
            else if (IsSameEdge(edge, node.CornerA, node.CornerC))
            {
                node.ConnectionCA = targetIndex;
            }
            else if (IsSameEdge(edge, node.CornerB, node.CornerC))
            {
                node.ConnectionBC = targetIndex;
            }
            else
            {
                return false;
            }

            // Debug.Log($"Connect {nodeIndex} with {targetIndex} on {edge}");

            _nodes[nodeIndex] = node;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetConnectionWithEdge(int nodeIndex, EdgeKey edge, int targetIndex)
        {
            if (!TrySetConnectionWithEdge(nodeIndex, edge, targetIndex))
            {
                Debug.LogWarning($"Edge {edge} not found in node {nodeIndex}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSameEdge(EdgeKey edge, float2 a, float2 b)
        {
            var sorted = new EdgeKey(a, b);
            return edge.Equals(sorted);
        }

        #region Debug

        public void DrawNodes(bool drawCenter)
        {
            Gizmos.color = Color.white;
            foreach (var node in _nodes)
            {
                node.DrawBorderGizmos();
                if (drawCenter)
                {
                    node.Triangle.GetCenter.To3D().DrawPoint(Color.gray, duration: null, size: 0.1f);
                }
            }
        }

        public void DrawConnections()
        {
            Gizmos.color = Color.yellow;
            foreach (var node in _nodes)
            {
                foreach (var id in EnumExtensions.GetValues<NavNode.EdgeId>())
                {
                    var connectionIndex = node.GetConnectionIndex(id);
                    if (connectionIndex != NavNode.NULL_INDEX)
                    {
                        Gizmos.DrawLine(node.Center.To3D(), node.GetEdge(id).Center.To3D());
                    }
                }
            }
        }
        
        public string GetCapacityStats()
        {
            return $"nodes: {_nodes.Length} \nposLookup: {_nodesPositionLookup.Count}";
        }

        #endregion

        public readonly struct AddNodeRequest
        {
            public readonly Triangle Triangle;
            public readonly T Attributes;

            public AddNodeRequest(Triangle triangle, T attributes)
            {
                Triangle = triangle;
                Attributes = attributes;
            }

            public override string ToString() => $"AddNodeRequest: {Triangle}";
        }
    }
}