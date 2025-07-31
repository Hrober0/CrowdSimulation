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
    public class NavMesh : IDisposable
    {
        private const float CELL_SIZE = 1f;
        private const float CELL_MULTIPLIER = 1f / CELL_SIZE;

        private const float MIN_TRIANGLE_AREA = 0.01f;

        private NativeFixedList<NavNode> _nodes;

        // cell position to node index
        private NativeParallelMultiHashMap<int2, int> _nodesPositionLookup;

        // edge to node index (if edge is common it points to one of two nodes)
        private readonly Dictionary<EdgeKey, int> _nodesEdgeLookup = new();

        private NativeFixedList<Obstacle> _obstacles;

        public NativeArray<NavNode> Nodes => _nodes.DirtyList.AsArray();

        public NavMesh(List<float2> startPoints)
        {
            _nodes = new(1000, Allocator.Persistent);
            _nodesPositionLookup = new(1000, Allocator.Persistent);

            _obstacles = new(1000, Allocator.Persistent);

            using var positions = new NativeArray<float2>(startPoints.ToArray(), Allocator.TempJob);
            using var triangulator = new Triangulator<float2>(Allocator.TempJob)
            {
                Input =
                {
                    Positions = positions,
                },
            };
            triangulator.Run();

            var triangles = triangulator.Output.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                AddNode(new()
                {
                    Triangle = new(
                        positions[triangles[i]],
                        positions[triangles[i + 1]],
                        positions[triangles[i + 2]]
                    ),
                    ObstacleIndex = AddNodeRequest.NO_OBSTACLE,
                });
            }
        }

        public void Dispose()
        {
            _nodes.Dispose();
            _nodesPositionLookup.Dispose();
            _obstacles.Dispose();
        }

        public int AddObstacle(List<Triangle> parts)
        {
            if (parts.Count == 0)
            {
                Debug.LogWarning($"{nameof(AddObstacle)}: No obstacles parts provided.");
                return -1;
            }

            if (Triangle.AnyTrianglesIntersect(parts))
            {
                Debug.LogWarning($"{nameof(AddObstacle)}: Obstacles parts are intersecting.");
                return -1;
            }

            var worldMin = new float2(float.MaxValue, float.MaxValue);
            var worldMax = new float2(float.MinValue, float.MinValue);
            foreach (var part in parts)
            {
                worldMin = math.min(math.min(part.A, part.B), math.min(part.C, worldMin));
                worldMax = math.max(math.max(part.A, part.B), math.max(part.C, worldMax));
            }

            int2 chunkMin = ChunkPosition(worldMin - new float2(0.1f, 0.1f));
            int2 chunkMax = ChunkPosition(worldMax + new float2(0.1f, 0.1f));

            int obstacleIndex = _obstacles.Add(new()
            {
                ChunkMin = chunkMin,
                ChunkMax = chunkMax,
            });

            List<AddNodeRequest> nodesToAdd = new();

            // Add new obstacle to nodes to add 
            foreach (Triangle part in parts)
            {
                nodesToAdd.Add(new()
                {
                    Triangle = part,
                    ObstacleIndex = obstacleIndex,
                });
            }

            List<NavNode> removedNodes = RemoveNodes(chunkMin, chunkMax);

            // Add existing but removed obstacle to nodes to add
            foreach (NavNode node in removedNodes)
            {
                // Add only if node is obstacle
                if (node.ConfigIndex >= 0)
                {
                    nodesToAdd.Add(new()
                    {
                        Triangle = CreateCCW(node.CornerA, node.CornerB, node.CornerC),
                        ObstacleIndex = node.ConfigIndex,
                    });
                }
            }

            EnsureValidTriangulation(nodesToAdd);

            AddAndFillEmptySpace(nodesToAdd, removedNodes);

            return obstacleIndex;
        }

        public void RemoveObstacle(int id)
        {
            Obstacle obstacle = _obstacles[id];
            _obstacles.RemoveAt(id);

            List<NavNode> removedNodes = RemoveNodes(obstacle.ChunkMin, obstacle.ChunkMax);

            List<AddNodeRequest> nodesToAdd = new();
            foreach (NavNode node in removedNodes)
            {
                if (node.ConfigIndex >= 0 && node.ConfigIndex != id)
                {
                    nodesToAdd.Add(new()
                    {
                        Triangle = node.Triangle,
                        ObstacleIndex = node.ConfigIndex,
                    });
                }
            }

            AddAndFillEmptySpace(nodesToAdd, removedNodes);
        }

        public bool TryGetNodeIndex(float2 position, out int nodeIndex)
        {
            int2 cell = ChunkPosition(position);
            foreach (var index in _nodesPositionLookup.GetValuesForKey(cell))
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

        public static void EnsureValidTriangulation(List<AddNodeRequest> nodes)
        {
            var tries = 1000;
            for (int i = 0; i < nodes.Count; i++)
            {
                AddNodeRequest t1 = nodes[i];
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    // Safety check
                    tries--;
                    if (tries == 0)
                    {
                        Debug.LogError($"{nameof(EnsureValidTriangulation)}: Node intersection fix failed.");
                        return;
                    }

                    AddNodeRequest t2 = nodes[j];

                    List<float2> intersection = Triangle.PolygonIntersection(t1.Triangle.Vertices, t2.Triangle.Vertices);

                    if (intersection.Count < 3)
                    {
                        continue; // no interior overlap — skip
                    }

                    Debug.LogWarning($"Intersection {t1} {t2} {intersection.ElementsString()}");

                    // Remove old nodes
                    nodes.RemoveAt(j);
                    nodes.RemoveAt(i);

                    // Restart loop after modifying the list
                    i = -1;

                    // Add new create nodes
                    float2[] intersectionArray = intersection.ToArray();
                    AddNotCommonPart(t1, intersectionArray, nodes);
                    AddNotCommonPart(t2, intersectionArray, nodes);
                    AddIntersection(t1, t2, intersectionArray, nodes);

                    break;
                }
            }
        }

        private static void AddNotCommonPart(AddNodeRequest subject, float2[] intersection, List<AddNodeRequest> output)
        {
            using var positions = new NativeList<float2>(intersection.Length + 3, Allocator.TempJob);
            var constraintEdges = new NativeList<int>(Allocator.TempJob);
            using var holes = new NativeList<float2>(Allocator.TempJob);

            float2 center = float2.zero;
            for (int i = 0; i < intersection.Length; i++)
            {
                positions.Add(intersection[i]);
                constraintEdges.Add(i - 1);
                constraintEdges.Add(i);
                center += intersection[i];
            }

            constraintEdges[0] = intersection.Length - 1; // fix first constraint edge
            holes.Add(center / intersection.Length);

            Insert(subject.Triangle.A);
            Insert(subject.Triangle.B);
            Insert(subject.Triangle.C);


            Debug.Log("Positions:");
            foreach (var n in positions)
            {
                Debug.Log(n);
            }

            Debug.Log("ConstraintEdges:");
            foreach (var n in constraintEdges)
            {
                Debug.Log(n);
            }

            using var triangulator = new Triangulator<float2>(Allocator.TempJob)
            {
                Input =
                {
                    Positions = positions.AsArray(),
                    ConstraintEdges = constraintEdges.AsArray(),
                    HoleSeeds = holes.AsArray(),
                },
            };
            triangulator.Run();

            Debug.Log("Result");
            var triangles = triangulator.Output.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                float2 a = positions[triangles[i]];
                float2 b = positions[triangles[i + 1]];
                float2 c = positions[triangles[i + 2]];
                if (Triangle.Area(a, b, c) < MIN_TRIANGLE_AREA)
                {
                    continue;
                }

                Debug.Log(CreateCCW(a, b, c));
                output.Add(new()
                {
                    Triangle = CreateCCW(a, b, c),
                    ObstacleIndex = subject.ObstacleIndex,
                });
            }

            constraintEdges.Dispose();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int Insert(float2 p)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    if (math.lengthsq(p - positions[i]) < .0001f)
                    {
                        return i;
                    }
                }

                positions.Add(p);
                return positions.Length - 1;
            }
        }

        private static void AddIntersection(AddNodeRequest n1, AddNodeRequest n2, float2[] intersection, List<AddNodeRequest> output)
        {
            using var positions = new NativeArray<float2>(intersection, Allocator.TempJob);
            using var triangulator = new Triangulator<float2>(Allocator.TempJob)
            {
                Input =
                {
                    Positions = positions,
                },
            };
            triangulator.Run();

            var triangles = triangulator.Output.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                output.Add(new()
                {
                    Triangle = CreateCCW(positions[triangles[i]], positions[triangles[i + 1]], positions[triangles[i + 2]]),
                    ObstacleIndex = n1.ObstacleIndex, // TODO: merge
                });
            }
        }

        private static Triangle CreateCCW(float2 p1, float2 p2, float2 p3)
        {
            if (Triangle.IsCCW(p1, p2, p3))
            {
                return new(p1, p2, p3);
            }
            else
            {
                return new(p1, p3, p2);
            }
        }

        private void AddAndFillEmptySpace(List<AddNodeRequest> nodesToAdd, List<NavNode> emptyNodes)
        {
            // Add nodes to navigation mesh
            foreach (var node in nodesToAdd)
            {
                AddNode(node);
            }

            // Calculate Border points
            List<EdgeKey> borderEdges = HullEdges.GetEdgesUnordered(emptyNodes);
            List<float2> borderPoints = HullEdges.GetPointsCCW(borderEdges);

            if (borderPoints.Count < 3)
            {
                Debug.LogError($"{nameof(AddAndFillEmptySpace)}: Not enough border points.");
                return;
            }

            // Debug.Log("Nodes to add:");
            // foreach (var n in nodesToAdd)
            // {
            //     Debug.Log(n);
            // }
            //
            // Debug.Log("Border points:");
            // foreach (var p in borderPoints)
            // {
            //     Debug.Log(p.To3D().ToString("F6"));
            // }

            using var positions = new NativeList<float2>(borderPoints.Count + nodesToAdd.Count * 3, Allocator.TempJob);
            var constraintEdges = new NativeList<int>(borderPoints.Count * 2, Allocator.TempJob);
            using var holes = new NativeList<float2>(nodesToAdd.Count, Allocator.TempJob);

            var edgesConstraints = new HashSet<EdgeKey>();
            
            // Add border points
            for (int i = 0; i < borderPoints.Count; i++)
            {
                AddPosition(borderPoints[i]);
            }

            // Add nodes as holes to not fill them
            for (int i = 0; i < nodesToAdd.Count; i++)
            {
                Triangle tr = nodesToAdd[i].Triangle;
                var indexA = AddPosition(tr.A);
                var indexB = AddPosition(tr.B);
                var indexC = AddPosition(tr.C);

                AddConstraint(indexA, indexB);
                AddConstraint(indexB, indexC);
                AddConstraint(indexC, indexA);
                
                holes.Add(tr.GetCenter);
            }
            
            foreach (var p in borderEdges)
            {
                Debug.DrawLine(p.A.To3D(), p.B.To3D(), Color.magenta);
                // Debug.DrawLine(p.A.To3D(), p.B.To3D(), Color.magenta, 5);
            }
            
            // foreach (var p in positions)
            // {
            //     p.To3D().DrawPoint(Color.magenta, 10);
            // }
            //
            // for (int i = 0; i < constraintEdges.Length; i+=2)
            // {
            //     var a = positions[constraintEdges[i]];
            //     var b = positions[constraintEdges[i + 1]];
            //     Debug.DrawLine(a.To3D(), b.To3D(), Color.red, 10);
            // }

            using var triangulator = new Triangulator<float2>(Allocator.TempJob)
            {
                Input =
                {
                    Positions = positions.AsArray(),
                    ConstraintEdges = constraintEdges.AsArray(),
                    HoleSeeds = holes.AsArray(),
                },
                // Settings = { AutoHolesAndBoundary = true, },
            };
            triangulator.Run();

            // Fill empty space to connect inserted nodes
            var triangles = triangulator.Output.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                float2 a = positions[triangles[i]];
                float2 b = positions[triangles[i + 1]];
                float2 c = positions[triangles[i + 2]];
                if (Triangle.Area(a, b, c) < MIN_TRIANGLE_AREA)
                {
                    continue;
                }

                // This solution not working, borders have to be constrained
                if (!HullEdges.IsPointInPolygon(Triangle.Center(a, b, c), borderEdges))
                {
                    // new Triangle(a, b, c).DrawBorder(Color.blue, 5);
                    continue;
                }

                AddNode(new()
                {
                    Triangle = new(a, b, c),
                    ObstacleIndex = AddNodeRequest.NO_OBSTACLE,
                });
            }

            constraintEdges.Dispose();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int AddPosition(float2 p)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    if (math.lengthsq(p - positions[i]) < .0001f)
                    {
                        return i;
                    }
                }

                positions.Add(p);
                return positions.Length - 1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void AddConstraint(int pi, int ei)
            {
                if (!edgesConstraints.Add(new(pi, ei)))
                {
                    return;
                }

                constraintEdges.Add(pi);
                constraintEdges.Add(ei);
            }
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

        /// <summary>
        /// Removes and disconnects nodes on given area
        /// </summary>
        /// <param name="min">left bottom area point</param>
        /// <param name="max">right top area point</param>
        /// <returns>Removed nodes</returns>
        private List<NavNode> RemoveNodes(int2 min, int2 max)
        {
            var removedNodes = new List<NavNode>();
            foreach (var cell in GetCellsFromMinMax(min, max))
            {
                foreach (var nodeIndex in _nodesPositionLookup.GetValuesForKey(cell))
                {
                    NavNode node = _nodes[nodeIndex];

                    if (!node.IsEmpty)
                    {
                        continue;
                    }

                    // Debug.Log($"Removing node: {nodeIndex} {node}");
                    // node.DrawBorder(Color.red, 1);
                    // DebugCell(cell);

                    _nodes[nodeIndex] = NavNode.Empty;
                    _nodes.RemoveAt(nodeIndex);
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
                }
            }

            return removedNodes;
        }

        /// <summary>
        /// Add node to mesh and connect it to existing nodes
        /// </summary>
        private void AddNode(AddNodeRequest addAddNodeRequest)
        {
            var bounds = addAddNodeRequest.Triangle.GetBounds();
            int2 min = ChunkPosition(bounds.min);
            int2 max = ChunkPosition(bounds.max);

            // fix position lookup capacity
            var newNumberOfLookupEntries = (max.x - min.x + 1) * (max.y - min.y + 1) + _nodesPositionLookup.Count();
            if (newNumberOfLookupEntries >= _nodesPositionLookup.Capacity)
            {
                _nodesPositionLookup.Capacity = newNumberOfLookupEntries;
            }

            // create node
            var newIndex = _nodes.FreeIndex;
            (float2 a, float2 b, float2 c) = addAddNodeRequest.Triangle;
            var newNode = new NavNode(
                a,
                b,
                c,
                connectionAB: TryConnect(a, b, newIndex),
                connectionAC: TryConnect(a, c, newIndex),
                connectionBC: TryConnect(b, c, newIndex),
                addAddNodeRequest.ObstacleIndex
            );

            // add to array
            // Debug.Log($"Add new node to index: {newIndex} / {_nodes.Length}");
            // newNode.DrawBorder(Color.green, 1);
            _nodes.Add(newNode);

            // add to position lookup
            foreach (var cell in GetCellsFromMinMax(min, max))
            {
                // DebugCell(cell);
                // Debug.Log($"Adding lookup node: {cell.x}, {cell.y} in {min} {max}");
                _nodesPositionLookup.Add(cell, newIndex);
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

        private static void DebugCell(int2 cell)
        {
            var s = (float2)cell * CELL_SIZE;
            new Rectangle(s, CELL_SIZE * Vector2.one).DrawBorder(Color.magenta, 2);
        }

        public void DrawNodes()
        {
            Gizmos.color = Color.white;
            foreach (var node in _nodes)
            {
                node.DrawBorderGizmos();
                node.Triangle.GetCenter.To3D().DrawPoint(Color.gray, duration: null, size: 0.1f);
            }
        }

        public void DrawObstacles()
        {
            Gizmos.color = Color.red;
            foreach (var node in _nodes)
            {
                if (node.ConfigIndex >= 0)
                {
                    node.DrawBorderGizmos();
                }
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

        public struct Obstacle
        {
            public int2 ChunkMin;
            public int2 ChunkMax;
        }

        public struct AddNodeRequest
        {
            public const int NO_OBSTACLE = -1;

            public Triangle Triangle;
            public int ObstacleIndex;

            public override string ToString() => $"AddNodeRequest: {Triangle} | ObstacleIndex: {ObstacleIndex}";
        }
    }
}