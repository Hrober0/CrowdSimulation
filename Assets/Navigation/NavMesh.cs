using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DelaunayTriangulation;
using HCore.Extensions;
using HCore.Shapes;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public class NavMesh : MonoBehaviour
    {
        private const float CELL_SIZE = 3f;
        private const float CELL_MULTIPLIER = 1f / CELL_SIZE;

        [SerializeField] private List<Transform> _borderPoints;

        private NativeList<NavNode> _nodes;
        private readonly Stack<int> _freeNodesIndexes = new();
        
        // cell position to node index
        private NativeParallelMultiHashMap<int2, int> _nodesPositionLookup;

        // edge to node index (if edge is common it points to one of two nodes)
        private readonly Dictionary<EdgeKey, int> _nodesEdgeLookup = new();
        
        private readonly DelaunayTriangulation.DelaunayTriangulation _triangulation = new();
        
        private void Start()
        {
            _nodes = new(Allocator.Persistent);
            _nodesPositionLookup = new(1000, Allocator.Persistent);

            _ = Init();
        }

        private void OnDestroy()
        {
            _nodes.Dispose();
            _nodesPositionLookup.Dispose();
        }

        private async Awaitable WaitForClick()
        {
            do
            {
                await Awaitable.NextFrameAsync();
            } while (!Input.GetKeyDown(KeyCode.Space));
        }
        private async Awaitable Init()
        {
            var startPoints = new List<Vector2>();
            foreach (var pointTransform in _borderPoints)
            {
                startPoints.Add(pointTransform.position.To2D());
            }
            
            _triangulation.Triangulate(startPoints);
            var result = new List<Triangle2D>();
            _triangulation.GetTrianglesDiscardingHoles(result); 
            foreach (var triangle in result)
            {
                AddNode(triangle.p0, triangle.p1, triangle.p2, 0);
            }

            await Add(new float2(1, 1), new float2(3, 1), new float2(3, 3));
            await Add(new float2(10, 10), new float2(8, 8), new float2(10, 8));
        }

        public async Awaitable Add(float2 a, float2 b, float2 c)
        {
            await WaitForClick();

            int2 min = ChunkPosition(math.min(math.min(a, b), c));
            int2 max = ChunkPosition(math.max(math.max(a, b), c));

            List<NavNode> removedNodes = await RemoveNodes(min, max);

            HashSet<Vector2> freeSpaceBorderPoints = HullEdges.GetHullEdgesPointsUnordered(removedNodes);


            var objectBorderPoints = new List<Vector2>()
            {
                a,
                b,
                c,
            };
            
            var constrains = new List<List<Vector2>>()
            {
                objectBorderPoints,
            };

            var pointsList = new List<Vector2>(freeSpaceBorderPoints);
            _triangulation.Triangulate(pointsList, constrainedEdges: constrains);
            var fill = new List<Triangle2D>();
            _triangulation.GetTrianglesDiscardingHoles(fill);
            foreach (var triangle in fill)
            {
                await WaitForClick();
                AddNode(triangle.p0, triangle.p1, triangle.p2, 0);
            }
            
            await WaitForClick();
            AddNode(a, b, c, 1);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int2 ChunkPosition(float2 worldPos) => (int2)(worldPos * CELL_MULTIPLIER);
       
        private static IEnumerable<int2> GetCellsFromTriangle(float2 a, float2 b, float2 c)
        {
            int2 min = ChunkPosition(math.min(math.min(a, b), c));
            int2 max = ChunkPosition(math.max(math.max(a, b), c));
            return GetCellsFromMinMax(min, max);
        }
        private static IEnumerable<int2> GetCellsFromMinMax(int2 min, int2 max)
        {
            for (int x = min.x; x <= max.x; x++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    yield return new int2(x, y);
                }
            }
        }


        private async Awaitable<List<NavNode>> RemoveNodes(int2 min, int2 max)
        {
            var removedNodes = new List<NavNode>();
            foreach (var cell in GetCellsFromMinMax(min, max))
            {
                foreach (var nodeIndex in _nodesPositionLookup.GetValuesForKey(cell))
                {
                    var node = _nodes[nodeIndex];

                    if (!node.IsEmpty)
                    {
                        continue;
                    }

                    Debug.Log($"Removing node: {nodeIndex} {node}");
                    node.DrawBorder(Color.red, 1);
                    // DebugCell(cell);

                    _nodes[nodeIndex] = NavNode.Empty;
                    _freeNodesIndexes.Push(nodeIndex);
                    removedNodes.Add(node);

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
                    foreach (var nodeCell in GetCellsFromTriangle(node.CornerA, node.CornerB, node.CornerC))
                    {
                        _nodesPositionLookup.Remove(nodeCell, nodeIndex);
                    }
                    
                    await WaitForClick();
                }
            }

            return removedNodes;
        }

        private void AddNode(float2 a, float2 b, float2 c, int configIndex)
        {
            if (!_freeNodesIndexes.TryPop(out int newIndex))
            {
                newIndex = _nodes.Length;
            }

            int2 min = ChunkPosition(math.min(math.min(a, b), c));
            int2 max = ChunkPosition(math.max(math.max(a, b), c));

            // fix position lookup capacity
            var newNumberOfLookupEntries = (max.x - min.x + 1) * (max.y - min.y + 1) + _nodesPositionLookup.Count();
            if (newNumberOfLookupEntries >= _nodesPositionLookup.Capacity)
            {
                _nodesPositionLookup.Capacity = newNumberOfLookupEntries;
            }

            // add to position lookup
            foreach (var cell in GetCellsFromMinMax(min, max))
            {
                // DebugCell(cell);
                Debug.Log($"Adding lookup node: {cell.x}, {cell.y} in {min} {max}");
                _nodesPositionLookup.Add(cell, newIndex);
            }

            var newNode = new NavNode(
                a,
                b,
                c,
                connectionAB: TryConnect(a, b, newIndex),
                connectionAC: TryConnect(a, c, newIndex),
                connectionBC: TryConnect(b, c, newIndex),
                configIndex
            );

            // Add to array
            Debug.Log($"Add new node to index: {newIndex} / {_nodes.Length}");
            newNode.DrawBorder(Color.green, 1);
            if (newIndex < _nodes.Length)
            {
                _nodes[newIndex] = newNode;
            }
            else
            {
                _nodes.Add(newNode);
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
            _nodesEdgeLookup[edge] = newIndex;
            return NavNode.NULL_INDEX;
        }

        private void SetConnectionWithEdge(int nodeIndex, EdgeKey edge, int targetIndex)
        {
            NavNode node = _nodes[nodeIndex];

            int connectionAB = node.ConnectionAB;
            int connectionAC = node.ConnectionAC;
            int connectionBC = node.ConnectionBC;

            if (IsSameEdge(edge, node.CornerA, node.CornerB))
            {
                connectionAB = targetIndex;
            }
            else if (IsSameEdge(edge, node.CornerA, node.CornerC))
            {
                connectionAC = targetIndex;
            }
            else if (IsSameEdge(edge, node.CornerB, node.CornerC))
            {
                connectionBC = targetIndex;
            }
            else
            {
                Debug.LogWarning($"Edge {edge} not found in node {nodeIndex}");
                return;
            }

            // Debug.Log($"Connect {nodeIndex} with {targetIndex} on {edge}");

            _nodes[nodeIndex] = new NavNode(
                node.CornerA,
                node.CornerB,
                node.CornerC,
                connectionAB,
                connectionAC,
                connectionBC,
                node.ConfigIndex
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSameEdge(EdgeKey edge, float2 a, float2 b)
        {
            var sorted = new EdgeKey(a, b);
            return edge.Equals(sorted);
        }

        private static void DebugCell(int2 cell)
        {
            var s = (float2)cell * CELL_SIZE;
            new Rectangle(s, CELL_SIZE * Vector2.one).DrawBorder(Color.magenta, 2);
        }

        private void OnDrawGizmos()
        {
            if (_nodes.IsCreated)
            {
                
                foreach (var node in _nodes)
                {
                    if (!node.IsEmpty)
                    {
                        continue;
                    }

                    Gizmos.color = node.ConfigIndex == 1 ? Color.red : Color.white;
                    node.DrawBorderGizmos();
                }

                Gizmos.color = Color.yellow;
                foreach (var node in _nodes)
                {
                    if (!node.IsEmpty)
                    {
                        continue;
                    }

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
            else
            {
                Gizmos.color = Color.red;
                var startPoints = new List<Vector2>();
                foreach (var pointTransform in _borderPoints)
                {
                    startPoints.Add(pointTransform.position.To2D());
                }
                IOutline.DrawBorderGizmos(startPoints);
            }
        }
    }
}