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
        private const int NODES_DEFAULT_CAPACITY = 1024;
        private const int OBSTACLES_DEFAULT_CAPACITY = 512;

        private const float CELL_SIZE = 1f;
        private const float CELL_MULTIPLIER = 1f / CELL_SIZE;

        private const float MIN_TRIANGLE_AREA = 0.01f;

        private NativeFixedList<NavNode> _nodes;

        // cell position to node index
        private NativeParallelMultiHashMap<int2, int> _nodesPositionLookup;

        // edge to node index (if edge is common it points to one of two nodes)
        private readonly Dictionary<EdgeKey, int> _nodesEdgeLookup = new();

        private NativeFixedList<Obstacle> _obstacles;
        private NativeListHash<int> _obstacleIndexesCombined;

        public NativeArray<NavNode> Nodes => _nodes.DirtyList.AsArray();
        public IEnumerable<NavNode> GetActiveNodes => _nodes;

        public NavMesh(List<float2> startPoints)
        {
            _nodes = new(NODES_DEFAULT_CAPACITY, Allocator.Persistent);
            _nodesPositionLookup = new(NODES_DEFAULT_CAPACITY * 2, Allocator.Persistent);

            _obstacles = new(OBSTACLES_DEFAULT_CAPACITY, Allocator.Persistent);
            _obstacleIndexesCombined = new(OBSTACLES_DEFAULT_CAPACITY, Allocator.Persistent);

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
                    ObstacleId = AddNodeRequest.NO_OBSTACLE,
                });
            }
        }

        public void Dispose()
        {
            _nodes.Dispose();
            _nodesPositionLookup.Dispose();
            _obstacles.Dispose();
            _obstacleIndexesCombined.Dispose();
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

            var obstacleIndexesArray = new NativeArray<int>(1, Allocator.Temp);
            obstacleIndexesArray[0] = obstacleIndex;
            var obstacleId = _obstacleIndexesCombined.AddOrGetId(obstacleIndexesArray);
            obstacleIndexesArray.Dispose();

            List<AddNodeRequest> nodesToAdd = new();

            // Add new obstacle to nodes to add 
            foreach (Triangle part in parts)
            {
                nodesToAdd.Add(new()
                {
                    Triangle = part,
                    ObstacleId = obstacleId,
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
                        ObstacleId = node.ConfigIndex,
                    });
                }
            }

            EnsureValidTriangulation(nodesToAdd);

            TryMergeTriangles(nodesToAdd);
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
                if (node.ConfigIndex < 0)
                {
                    // Skip fill nodes
                    continue;
                }

                using NativeArray<int> obstacleIndexes = _obstacleIndexesCombined.GetElements(node.ConfigIndex, Allocator.Temp);
                var removedObstacleIndex = obstacleIndexes.IndexOf(id);
                if (removedObstacleIndex == -1)
                {
                    // Obstacle node does not contain removing id, add it without changes
                    nodesToAdd.Add(new()
                    {
                        Triangle = node.Triangle,
                        ObstacleId = node.ConfigIndex,
                    });
                    continue;
                }

                if (obstacleIndexes.Length <= 1)
                {
                    // Do not add empty obstacle
                    continue;
                }

                // Remove obstacle index
                using NativeArray<int> fixedObstacleIndexes = new(obstacleIndexes.Length - 1, Allocator.Temp);
                NativeArray<int>.Copy(obstacleIndexes, 0, fixedObstacleIndexes, 0, removedObstacleIndex);
                if (removedObstacleIndex + 1 < fixedObstacleIndexes.Length)
                {
                    NativeArray<int>.Copy(obstacleIndexes, removedObstacleIndex + 1, fixedObstacleIndexes, removedObstacleIndex,
                        obstacleIndexes.Length - removedObstacleIndex - 1);
                }

                var newObstacleId = _obstacleIndexesCombined.AddOrGetId(fixedObstacleIndexes);
                nodesToAdd.Add(new()
                {
                    Triangle = node.Triangle,
                    ObstacleId = newObstacleId,
                });
            }

            TryMergeTriangles(nodesToAdd);
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

        public IEnumerable<int> GetObstacleIndexes(int obstacleId)
        {
            using var indexes = _obstacleIndexesCombined.GetElements(obstacleId, Allocator.Temp);
            foreach (var index in indexes)
            {
                yield return index;
            }
        }

        public void EnsureValidTriangulation(List<AddNodeRequest> nodes)
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

                    List<float2> intersection = PolygonUtils.PolygonIntersection(t1.Triangle.Vertices, t2.Triangle.Vertices);

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
                    ObstacleId = subject.ObstacleId,
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

        private void AddIntersection(AddNodeRequest n1, AddNodeRequest n2, float2[] intersection, List<AddNodeRequest> output)
        {
            // Combine obstacle indexes
            using NativeArray<int> indexes1 = _obstacleIndexesCombined.GetElements(n1.ObstacleId, Allocator.Temp);
            using NativeArray<int> indexes2 = _obstacleIndexesCombined.GetElements(n2.ObstacleId, Allocator.Temp);
            using NativeList<int> indexesCombined = new(Allocator.Temp);
            indexesCombined.AddRange(indexes1);
            foreach (int index in indexes2)
            {
                if (!indexesCombined.Contains(index))
                {
                    indexesCombined.Add(index);
                }
            }

            var obstacleId = _obstacleIndexesCombined.AddOrGetId(indexesCombined.AsArray());

            // Fill intersection with triangles
            using NativeArray<float2> positions = new(intersection, Allocator.TempJob);
            using Triangulator<float2> triangulator = new(Allocator.TempJob)
            {
                Input =
                {
                    Positions = positions,
                },
            };
            triangulator.Run();

            // Add new obstacles
            var triangles = triangulator.Output.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                output.Add(new()
                {
                    Triangle = CreateCCW(positions[triangles[i]], positions[triangles[i + 1]], positions[triangles[i + 2]]),
                    ObstacleId = obstacleId
                });
            }
        }

        public static void TryMergeTriangles(List<AddNodeRequest> triangles)
        {
            var edgeMap = new Dictionary<EdgeKey, (int index, float2 firstThirdPoint)>(triangles.Count * 3);

            bool merged;
            do
            {
                merged = false;
                edgeMap.Clear();

                // Find triangles with common edge
                for (int i = 0; i < triangles.Count; i++)
                {
                    Triangle t = triangles[i].Triangle;
                    merged = AddEdge(t.A, t.B, t.C, i)
                             || AddEdge(t.B, t.C, t.A, i)
                             || AddEdge(t.C, t.A, t.B, i);

                    if (merged)
                    {
                        break;
                    }
                }
            } while (merged);

            return;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool AddEdge(float2 v1, float2 v2, float2 thirdPoint, int index)
            {
                var key = new EdgeKey(v1, v2);

                // when edge was already register it means that triangles have common edge
                if (!edgeMap.TryGetValue(key, out (int index, float2 thirdPoint) first))
                {
                    edgeMap.Add(key, (index, thirdPoint));
                    return false;
                }

                if (triangles[index].ObstacleId != triangles[first.index].ObstacleId)
                {
                    return false;
                }

                if (GeometryUtils.Collinear(thirdPoint, first.thirdPoint, v2))
                {
                    // merge into one triangle by v1
                    var last = triangles[first.index];
                    last.Triangle = new Triangle(thirdPoint, first.thirdPoint, v1);
                    triangles[first.index] = last;
                    triangles.RemoveAt(index);
                    return true;
                }

                if (GeometryUtils.Collinear(thirdPoint, first.thirdPoint, v1))
                {
                    // merge into one triangle by v2
                    var last = triangles[first.index];
                    last.Triangle = new Triangle(thirdPoint, first.thirdPoint, v2);
                    triangles[first.index] = last;
                    triangles.RemoveAt(index);
                    return true;
                }

                return false;
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
            // Calculate Border points
            List<EdgeKey> borderEdges = PolygonUtils.GetEdgesUnordered(emptyNodes);
            float2 borderCenter = PolygonUtils.PolygonCenter(borderEdges);

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

            using var positions = new NativeList<float2>(borderEdges.Count * 2 + nodesToAdd.Count * 3, Allocator.TempJob);
            var constraintEdges = new NativeList<int>(borderEdges.Count * 4, Allocator.TempJob);
            using var holes = new NativeList<float2>(nodesToAdd.Count, Allocator.TempJob);

            var edgesConstraints = new HashSet<EdgeKey>(); // it's using ints as indexes not float positions

            // Add nodes to navigation mesh
            for (int i = 0; i < nodesToAdd.Count; i++)
            {
                AddNodeRequest node = nodesToAdd[i];
                Triangle tr = node.Triangle;

                // Check border intersection
                if (IntersectWithBorder(tr, out int intersectedEdgeIndex))
                {
                    EdgeKey intersectedEdge = borderEdges[intersectedEdgeIndex];

                    Debug.LogWarning($"Intersected border {intersectedEdge}");
                    Debug.DrawLine(intersectedEdge.A.To3D(), intersectedEdge.B.To3D(), Color.yellow, 5);

                    var pointsOnEdge = new List<float2>(3);
                    switch (GeometryUtils.Side(intersectedEdge.A, intersectedEdge.B, tr.A, borderCenter))
                    {
                        case 0:
                            pointsOnEdge.Add(tr.A);
                            break;
                        case < 0:
                            Debug.LogWarning($"Point {tr.A} is out of bounds");
                            continue;
                    }

                    switch (GeometryUtils.Side(intersectedEdge.A, intersectedEdge.B, tr.B, borderCenter))
                    {
                        case 0:
                            pointsOnEdge.Add(tr.B);
                            break;
                        case < 0:
                            Debug.LogWarning($"Point {tr.B} is out of bounds");
                            continue;
                    }

                    switch (GeometryUtils.Side(intersectedEdge.A, intersectedEdge.B, tr.C, borderCenter))
                    {
                        case 0:
                            pointsOnEdge.Add(tr.C);
                            break;
                        case < 0:
                            Debug.LogWarning($"Point {tr.C} is out of bounds");
                            continue;
                    }

                    // Debug.DrawLine(intersectedEdge.A.To3D() + Vector3.down * 0.1f, intersectedEdge.B.To3D() + Vector3.down * 0.1f,
                    //     Color.black, 5);
                    // borderCenter.To3D().DrawPoint(Color.black, 5);

                    if (pointsOnEdge.Count == 1)
                    {
                        float2 singlePoint = pointsOnEdge[0];

                        // intersectedEdge.A.To3D().DrawPoint(Color.red, 5);
                        // intersectedEdge.B.To3D().DrawPoint(Color.red, 5);
                        // singlePoint.To3D().DrawPoint(Color.magenta, 5);

                        borderEdges[intersectedEdgeIndex] = new(intersectedEdge.A, singlePoint); // reuse removed index
                        borderEdges.Add(new(intersectedEdge.B, singlePoint));
                    }
                    else if (pointsOnEdge.Count == 2)
                    {
                        float2 pointsCloseToEdgeA = pointsOnEdge[0];
                        float2 pointsCloseToEdgeB = pointsOnEdge[1];
                        if (math.distancesq(intersectedEdge.A, pointsCloseToEdgeA) >
                            math.distancesq(intersectedEdge.A, pointsCloseToEdgeB))
                        {
                            (pointsCloseToEdgeA, pointsCloseToEdgeB) = (pointsCloseToEdgeB, pointsCloseToEdgeA);
                        }

                        // intersectedEdge.A.To3D().DrawPoint(Color.blue, 5);
                        // intersectedEdge.B.To3D().DrawPoint(Color.red, 5);
                        // pointsCloseToEdgeA.To3D().DrawPoint(Color.cyan, 5);
                        // pointsCloseToEdgeB.To3D().DrawPoint(Color.yellow, 5);

                        borderEdges[intersectedEdgeIndex] = new(intersectedEdge.A, pointsCloseToEdgeA); // reuse removed index
                        borderEdges.Add(new(intersectedEdge.B, pointsCloseToEdgeB));
                        borderEdges.Add(new(pointsCloseToEdgeA, pointsCloseToEdgeB));
                    }
                    else
                    {
                        borderEdges.RemoveAt(intersectedEdgeIndex);
                        Debug.LogError($"Unexpected number of points outside filling area ({pointsOnEdge.Count} points outside)");
                        continue;
                    }
                }

                // Add nodes to navigation mesh
                AddNode(node);

                var indexA = AddPosition(tr.A);
                var indexB = AddPosition(tr.B);
                var indexC = AddPosition(tr.C);

                AddConstraint(indexA, indexB);
                AddConstraint(indexB, indexC);
                AddConstraint(indexC, indexA);

                // Do not fill holes
                holes.Add(tr.GetCenter);
            }

            // Add border points
            for (int i = 0; i < borderEdges.Count; i++)
            {
                EdgeKey edge = borderEdges[i];
                var aIndex = AddPosition(edge.A);
                var bIndex = AddPosition(edge.B);
                AddConstraint(aIndex, bIndex);
            }

            // Debug.Log("Border edges:");
            // foreach (var p in borderEdges)
            // {
            //     // Debug.DrawLine(p.A.To3D(), p.B.To3D(), Color.magenta);
            //     Debug.DrawLine(p.A.To3D(), p.B.To3D(), Color.magenta, 5);
            //     Debug.Log($"{p.A} - {p.B}");
            // }
            //
            // Debug.Log("Positions:");
            // foreach (var p in positions)
            // {
            //     // p.To3D().DrawPoint(Color.magenta);
            //     p.To3D().DrawPoint(Color.magenta, 5);
            //     Debug.Log(p);
            // }
            //
            // Debug.Log("constraints:");
            // for (int i = 0; i < constraintEdges.Length; i += 2)
            // {
            //     var a = positions[constraintEdges[i]];
            //     var b = positions[constraintEdges[i + 1]];
            //     // Debug.DrawLine(a.To3D(), b.To3D(), Color.red);
            //     Debug.DrawLine(a.To3D() + Vector3.down * 0.1f, b.To3D() + Vector3.down * 0.1f, Color.red, 5);
            //     Debug.Log($"{i} ({a}) - {(i + 1)} ({b})");
            // }

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

            // Fill empty space to connect inserted nodes
            var triangles = triangulator.Output.Triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                float2 a = positions[triangles[i]];
                float2 b = positions[triangles[i + 1]];
                float2 c = positions[triangles[i + 2]];
                var triangle = new Triangle(a, b, c);
                if (Triangle.Area(a, b, c) < MIN_TRIANGLE_AREA || !PolygonUtils.IsPointInPolygon(triangle.GetCenter, borderEdges))
                {
                    continue;
                }

                AddNode(new()
                {
                    Triangle = triangle,
                    ObstacleId = AddNodeRequest.NO_OBSTACLE,
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

            bool IntersectWithBorder(Triangle triangle, out int edgeIndex)
            {
                for (edgeIndex = 0; edgeIndex < borderEdges.Count; edgeIndex++)
                {
                    var edge = borderEdges[edgeIndex];
                    if (EdgesIntersect(edge.A, edge.B, triangle.A, triangle.B)
                        || EdgesIntersect(edge.A, edge.B, triangle.B, triangle.C)
                        || EdgesIntersect(edge.A, edge.B, triangle.C, triangle.A))
                    {
                        return true;
                    }
                }

                edgeIndex = -1;
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool EdgesIntersect(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            return !a1.Equals(b1) && !a1.Equals(b2) && !a2.Equals(b1) && !a2.Equals(b2) &&
                   GeometryUtils.EdgesIntersectIncludeEnds(a1, a2, b1, b2);
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
                addAddNodeRequest.ObstacleId
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
            public int ObstacleId;

            public override string ToString() => $"AddNodeRequest: {Triangle} | ObstacleIndex: {ObstacleId}";
        }
    }
}