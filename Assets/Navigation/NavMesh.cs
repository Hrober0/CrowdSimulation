using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using andywiecko.BurstTriangulator;
using CustomNativeCollections;
using HCore.Extensions;
using HCore.Shapes;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public struct NavMesh : IDisposable
    {
        public NativeFixedList<NavNode> _nodes;
        public NativeSpatialHash<int> _nodesPositionLookup; // cell position to node index

        // Edge to node index (if edge is common it points to one of two nodes)
        public NativeHashMap<EdgeKey, int> _nodesEdgeLookup;

        public NativeArray<NavNode> Nodes => _nodes.DirtyList.AsArray();
        public IEnumerable<NavNode> GetActiveNodes => _nodes;
        public bool IsCreated => _nodesPositionLookup.IsCreated;
        
        public NavMesh(float cellSize, int nodesInitialCapacity = 1024)
        {
            _nodes = new(nodesInitialCapacity, Allocator.Persistent);
            _nodesPositionLookup = new(nodesInitialCapacity * 2, cellSize, Allocator.Persistent);
            _nodesEdgeLookup = new(nodesInitialCapacity * 3, Allocator.Persistent);
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
                NavNode node = _nodes[index];
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
            var newNode = new NavNode(
                a,
                b,
                c,
                connectionAB: TryConnect(a, b, newIndex),
                connectionAC: TryConnect(a, c, newIndex),
                connectionBC: TryConnect(b, c, newIndex)
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
            using var indexes = new NativeList<int>(128, Allocator.Temp);
            _nodesPositionLookup.QueryAABB(min, max, indexes);
            
            foreach (var nodeIndex in indexes)
            {
                NavNode node = _nodes[nodeIndex];

                if (node.IsEmpty)
                {
                    continue;
                }

                // Debug.Log($"Removing node: {nodeIndex} {node}");
                // node.DrawBorder(Color.red, 1);
                // DebugCell(cell);

                _nodes[nodeIndex] = NavNode.Empty;
                _nodes.RemoveAt(nodeIndex);
                
                Triangle nodeTr = node.Triangle;
                removedNodes.Add(nodeTr);

                // disconnect AB
                {
                    var edge = new EdgeKey(node.CornerA, node.CornerB);
                    if (node.ConnectionAB == NavNode.NULL_INDEX)
                    {
                        // this edge has to be removed from lookup
                        _nodesEdgeLookup.Remove(edge);
                    }
                    else
                    {
                        // other node contains this edge so it need to make sure that lookup point to that node
                        _nodesEdgeLookup[edge] = node.ConnectionAB;
                        SetConnectionWithEdge(node.ConnectionAB, edge, NavNode.NULL_INDEX);
                    }
                }

                // disconnect AC
                {
                    var edge = new EdgeKey(node.CornerA, node.CornerC);
                    if (node.ConnectionAC == NavNode.NULL_INDEX)
                    {
                        // this edge has to be removed from lookup
                        _nodesEdgeLookup.Remove(edge);
                    }
                    else
                    {
                        // other node contains this edge so it need to make sure that lookup point to that node
                        _nodesEdgeLookup[edge] = node.ConnectionAC;
                        SetConnectionWithEdge(node.ConnectionAC, edge, NavNode.NULL_INDEX);
                    }
                }

                // disconnect BC
                {
                    var edge = new EdgeKey(node.CornerB, node.CornerC);
                    if (node.ConnectionBC == NavNode.NULL_INDEX)
                    {
                        // this edge has to be removed from lookup
                        _nodesEdgeLookup.Remove(edge);
                    }
                    else
                    {
                        // other node contains this edge so it need to make sure that lookup point to that node
                        _nodesEdgeLookup[edge] = node.ConnectionBC;
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
            if (_nodesEdgeLookup.TryGetValue(edge, out int otherIndex))
            {
                SetConnectionWithEdge(otherIndex, edge, newIndex);
                return otherIndex;
            }

            // Debug.Log($"Not found edge: {edge} for {newIndex}");
            if (_nodesEdgeLookup.Capacity <= _nodesEdgeLookup.Count + 1)
            {
                _nodesEdgeLookup.Capacity += 128;
            }
            _nodesEdgeLookup[edge] = newIndex;
            return NavNode.NULL_INDEX;
        }

        private void SetConnectionWithEdge(int nodeIndex, EdgeKey edge, int targetIndex)
        {
            NavNode node = _nodes[nodeIndex];

            if (IsSameEdge(edge, node.CornerA, node.CornerB))
            {
                node.ConnectionAB = targetIndex;
            }
            else if (IsSameEdge(edge, node.CornerA, node.CornerC))
            {
                node.ConnectionAC = targetIndex;
            }
            else if (IsSameEdge(edge, node.CornerB, node.CornerC))
            {
                node.ConnectionBC = targetIndex;
            }
            else
            {
                Debug.LogWarning($"Edge {edge} not found in node {nodeIndex}");
                return;
            }

            // Debug.Log($"Connect {nodeIndex} with {targetIndex} on {edge}");

            _nodes[nodeIndex] = node;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSameEdge(EdgeKey edge, float2 a, float2 b)
        {
            var sorted = new EdgeKey(a, b);
            return edge.Equals(sorted);
        }

        #region Debug

        public void DrawNodes()
        {
            Gizmos.color = Color.white;
            foreach (var node in _nodes)
            {
                node.DrawBorderGizmos();
                node.Triangle.GetCenter.To3D().DrawPoint(Color.gray, duration: null, size: 0.1f);
            }
        }

        public void DrawConnections()
        {
            Gizmos.color = Color.yellow;
            foreach (var node in _nodes)
            {
                if (node.ConnectionAB != NavNode.NULL_INDEX)
                {
                    Gizmos.DrawLine(node.Center.To3D(), _nodes[node.ConnectionAB].Center.To3D());
                }

                if (node.ConnectionAC != NavNode.NULL_INDEX)
                {
                    Gizmos.DrawLine(node.Center.To3D(), _nodes[node.ConnectionAC].Center.To3D());
                }

                if (node.ConnectionBC != NavNode.NULL_INDEX)
                {
                    Gizmos.DrawLine(node.Center.To3D(), _nodes[node.ConnectionBC].Center.To3D());
                }
            }
        }

        #endregion

        public struct AddNodeRequest
        {
            public Triangle Triangle;

            public override string ToString() => $"AddNodeRequest: {Triangle}";
        }
    }
}