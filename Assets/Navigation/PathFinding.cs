using System.Runtime.CompilerServices;
using CustomNativeCollections;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    public static class PathFinding
    {
        public static void FunnelPath(float2 start, float2 end, NativeArray<Portal> portals, NativeList<float2> resultPath)
        {
            float2 apex = start;
            float2 left = apex;
            float2 right = apex;
            int leftIndex = 0;
            int rightIndex = 0;

            resultPath.Add(apex);

            for (int i = 0; i < portals.Length; i++)
            {
                float2 pLeft = portals[i].Left;
                float2 pRight = portals[i].Right;

                // Left check
                if (Triangle.SignedArea(apex, right, pRight) >= 0f)
                {
                    if (GeometryUtils.NearlyEqual(apex, right) || Triangle.SignedArea(apex, left, pRight) < 0f)
                    {
                        right = pRight;
                        rightIndex = i;
                    }
                    else
                    {
                        // Tight turn on left
                        resultPath.Add(left);
                        apex = left;
                        left = apex;
                        right = apex;
                        var apexIndex = leftIndex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }

                // Right check
                if (Triangle.SignedArea(apex, left, pLeft) <= 0f)
                {
                    if (GeometryUtils.NearlyEqual(apex, left) || Triangle.SignedArea(apex, right, pLeft) > 0f)
                    {
                        left = pLeft;
                        leftIndex = i;
                    }
                    else
                    {
                        // Tight turn on right
                        resultPath.Add(right);
                        apex = right;
                        left = apex;
                        right = apex;
                        var apexIndex = rightIndex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }
            }

            // Add end
            if (Triangle.SignedArea(apex, right, end) >= 0f && !GeometryUtils.NearlyEqual(apex, right) &&
                !(Triangle.SignedArea(apex, left, end) < 0f))
            {
                resultPath.Add(left);
            }
            else if (Triangle.SignedArea(apex, left, end) <= 0f && !GeometryUtils.NearlyEqual(apex, left) &&
                     !(Triangle.SignedArea(apex, right, end) > 0f))
            {
                resultPath.Add(right);
            }

            if (resultPath.Length == 0 || !GeometryUtils.NearlyEqual(resultPath[^1], end))
            {
                resultPath.Add(end);
            }
        }

        public static void FunnelPortals(float2 start, float2 end, NativeArray<Portal> portals, NativeArray<float2> resultPath)
        {
            float2 apex = start;
            float2 left = apex;
            float2 right = apex;
            int leftIndex = 0;
            int rightIndex = 0;

            for (int i = 0; i < portals.Length; i++)
            {
                float2 pLeft = portals[i].Left;
                float2 pRight = portals[i].Right;
                resultPath[i] = float2.zero; // path will be corrected in fix iteration

                // Left check
                if (Triangle.SignedArea(apex, right, pRight) >= 0f)
                {
                    if (GeometryUtils.NearlyEqual(apex, right) || Triangle.SignedArea(apex, left, pRight) < 0f)
                    {
                        right = pRight;
                        rightIndex = i;
                    }
                    else
                    {
                        // Tight turn on left
                        apex = left;
                        right = apex;
                        var apexIndex = leftIndex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        resultPath[apexIndex] = apex;
                        continue;
                    }
                }

                // Right check
                if (Triangle.SignedArea(apex, left, pLeft) <= 0f)
                {
                    if (GeometryUtils.NearlyEqual(apex, left) || Triangle.SignedArea(apex, right, pLeft) > 0f)
                    {
                        left = pLeft;
                        leftIndex = i;
                    }
                    else
                    {
                        // Tight turn on right
                        apex = right;
                        left = apex;
                        var apexIndex = rightIndex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        resultPath[apexIndex] = apex;
                        continue;
                    }
                }
            }

            // Fix end
            if (Triangle.SignedArea(apex, right, end) >= 0f && !GeometryUtils.NearlyEqual(apex, right) &&
                !(Triangle.SignedArea(apex, left, end) < 0f))
            {
                resultPath[leftIndex] = left;
            }
            else if (Triangle.SignedArea(apex, left, end) <= 0f && !GeometryUtils.NearlyEqual(apex, left) &&
                     !(Triangle.SignedArea(apex, right, end) > 0f))
            {
                resultPath[rightIndex] = right;
            }

            // Fix points on portals
            int lastCornerPointIndex = -1;
            float2 lastCornerPoint = start;
            for (int i = 0; i < portals.Length; i++)
            {
                if (resultPath[i].Equals(float2.zero))
                {
                    continue;
                }

                float2 secondCornerPoint = resultPath[i];
                for (int j = lastCornerPointIndex + 1; j < i; j++)
                {
                    Portal portal = portals[j];
                    // DebugUtils.Draw(lastCornerPoint, secondCornerPoint, Color.magenta, 5);
                    resultPath[j] = GeometryUtils.IntersectionPoint(portal.Right, portal.Left, lastCornerPoint, secondCornerPoint);
                }

                lastCornerPointIndex = i;
                lastCornerPoint = resultPath[i];
            }

            for (int j = lastCornerPointIndex + 1; j < resultPath.Length; j++)
            {
                Portal portal = portals[j];
                // DebugUtils.Draw(lastCornerPoint, end, Color.red, 5);
                resultPath[j] = GeometryUtils.IntersectionPoint(portal.Right, portal.Left, lastCornerPoint, end);
            }
        }

        [BurstCompile]
        public static void FindPath<TAttribute, TSeeker>(
            float2 startPosition,
            int startNodeIndex,
            float2 targetPosition,
            int targetNodeIndex,
            in NativeArray<NavNode<TAttribute>> nodes,
            in TSeeker seeker,
            NativeList<Portal> resultPath
        )
            where TAttribute : unmanaged, INodeAttributes<TAttribute>
            where TSeeker : unmanaged, IPathSeeker<TSeeker, TAttribute>
        {
            var cameFrom = new NativeHashMap<int, AStarNode>(nodes.Length, Allocator.Temp);
            using var closedSet = new NativeHashSet<int>(nodes.Length, Allocator.Temp);
            using var openSet = new NativePriorityQueue<AStarNode>(nodes.Length, Allocator.Temp);

            var edgesIds = new NativeArray<NavNode.EdgeId>(3, Allocator.Temp);
            edgesIds[0] = NavNode.EdgeId.AB;
            edgesIds[1] = NavNode.EdgeId.BC;
            edgesIds[2] = NavNode.EdgeId.CA;

            openSet.Enqueue(new(
                index: startNodeIndex,
                gCost: 0,
                fCost: math.distance(nodes[startNodeIndex].Center, targetPosition),
                cameFromIndex: -1));

            bool foundTargetNode = false;
            int triesAfterFoundTargetNode = 10;

            // Find path to target node
            while (openSet.Count > 0)
            {
                AStarNode current = openSet.Dequeue();

                if (foundTargetNode)
                {
                    triesAfterFoundTargetNode--;
                    if (triesAfterFoundTargetNode < 1)
                    {
                        break;
                    }
                }
                else if (current.Index == targetNodeIndex)
                {
                    foundTargetNode = true;
                }

                closedSet.Add(current.Index);

                NavNode<TAttribute> node = nodes[current.Index];
                foreach (NavNode.EdgeId edgeId in edgesIds)
                {
                    var connectedIndex = node.GetConnectionIndex(edgeId);

                    if (connectedIndex == NavNode.NULL_INDEX)
                    {
                        continue;
                    }

                    if (closedSet.Contains(connectedIndex))
                    {
                        continue;
                    }

                    NavNode<TAttribute> neighbor = nodes[connectedIndex];

                    // ReSharper disable once PossiblyImpureMethodCallOnReadonlyVariable
                    float moveCost = seeker.CalculateCost(neighbor.Attributes, node.Center, neighbor.Center);

                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (moveCost == float.MaxValue)
                    {
                        continue;
                    }

                    float g = current.GCost + moveCost;
                    float h = math.distance(neighbor.Center, targetPosition);
                    float f = g + h;
                    if (cameFrom.TryGetValue(connectedIndex, out AStarNode existing) && f >= existing.FCost)
                    {
                        continue;
                    }

                    var newNode = new AStarNode(
                        index: connectedIndex,
                        gCost: g,
                        fCost: f,
                        cameFromIndex: current.Index
                    );

                    cameFrom[connectedIndex] = newNode;
                    openSet.Enqueue(newNode);
                }
            }

            // Recreate path
            // first node is target node
            // last node should be start node
            using var pathNodeIndexes = new NativeList<int>(Allocator.Temp);
            int currentIndex = targetNodeIndex;
            while (cameFrom.TryGetValue(currentIndex, out AStarNode node))
            {
                pathNodeIndexes.Add(node.Index);
                currentIndex = node.CameFromIndex;
            }

            pathNodeIndexes.Add(startNodeIndex);

            // Create portals
            var lastPortal = new Portal(startPosition, startPosition);
            for (int i = 0; i < pathNodeIndexes.Length - 1; i++)
            {
                // Path is processed from last node to achieve correct order
                int currentNodeIndex = pathNodeIndexes[^(i + 1)];
                int nextNodeIndex = pathNodeIndexes[^(i + 2)];
                NavNode<TAttribute> currentNode = nodes[currentNodeIndex];

                Edge edge;
                if (currentNode.ConnectionAB == nextNodeIndex)
                {
                    edge = new(currentNode.CornerA, currentNode.CornerB);
                }
                else if (currentNode.ConnectionBC == nextNodeIndex)
                {
                    edge = new(currentNode.CornerB, currentNode.CornerC);
                }
                else if (currentNode.ConnectionCA == nextNodeIndex)
                {
                    edge = new(currentNode.CornerC, currentNode.CornerA);
                }
                else
                {
                    Debug.LogWarning("Nodes should be connected but are not...");
                    edge = new(currentNode.CornerA, currentNode.CornerB);
                }

                // Determine left/right ordering
                Portal newPortal = CreatePortal(edge.A, edge.B, lastPortal);
                lastPortal = newPortal;
                resultPath.Add(newPortal);
            }

            edgesIds.Dispose();
            cameFrom.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Portal CreatePortal(float2 p1, float2 p2, Portal lastPortal)
        {
            float area = Triangle.SignedArea(lastPortal.Center, p1, p2);
            Portal portal = area < 0
                ? new(p1, p2)
                : new(p2, p1);
            return portal;
        }

        [BurstCompile]
        public static void FindSpaces<TAttribute, TSeeker>(
            float2 centerPosition,
            int centerNodeIndex,
            int positionsToFind,
            float spacing,
            in NativeArray<NavNode<TAttribute>> nodes,
            in TSeeker seeker,
            NativeList<float2> positions
        )
            where TAttribute : unmanaged, INodeAttributes<TAttribute>
            where TSeeker : unmanaged, IPlaceSeeker<TSeeker, TAttribute>
        {
            if (positionsToFind <= 0)
            {
                return;
            }

            using var closedSet = new NativeHashSet<int>(nodes.Length, Allocator.Temp);
            using var openSet = new NativeQueue<int>(Allocator.Temp);
            using var foundPositions = new NativeHashSet<int2>(math.max(positionsToFind * 4, 16), Allocator.Temp);

            // TODO: If first node is invalid algorithm should find the closest valid node before start searching

            var invSpacing = 1f / spacing;
            openSet.Enqueue(centerNodeIndex);
            while (!openSet.IsEmpty())
            {
                var evaluatingIndex = openSet.Dequeue();

                NavNode<TAttribute> node = nodes[evaluatingIndex];

                // ReSharper disable once PossiblyImpureMethodCallOnReadonlyVariable
                if (seeker.IsValid(node.Attributes))
                {
                    Triangle nodeTriangle = node.Triangle;
                    float2 min = math.floor((nodeTriangle.Min - centerPosition) / spacing) * spacing + centerPosition;
                    float2 max = math.ceil((nodeTriangle.Max - centerPosition) / spacing) * spacing + centerPosition;
                    for (float y = min.y; y <= max.y; y += spacing)
                    {
                        for (float x = min.x; x <= max.x; x += spacing)
                        {
                            var p = new float2(x, y);
                            // p.To3D().DrawPoint(Triangle.PointIn(p, nodeTriangle.A, nodeTriangle.B, nodeTriangle.C) ? Color.green : Color.red, 5, .3f);

                            if (!Triangle.PointIn(p, nodeTriangle.A, nodeTriangle.B, nodeTriangle.C))
                            {
                                continue;
                            }

                            var key = new int2((int)math.floor(p.x * invSpacing), (int)math.floor(p.y * invSpacing));
                            if (foundPositions.Add(key))
                            {
                                positions.Add(p);
                            }
                        }
                    }
                }

                if (foundPositions.Count >= positionsToFind)
                {
                    break;
                }

                AddConnectedNode(node.ConnectionAB);
                AddConnectedNode(node.ConnectionBC);
                AddConnectedNode(node.ConnectionCA);
            }

            positions.Sort(new PointDistanceComparer(centerPosition));
            if (positions.Length > positionsToFind)
            {
                positions.RemoveRange(positionsToFind, positions.Length - positionsToFind);
            }

            return;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void AddConnectedNode(int index)
            {
                if (index != NavNode.NULL_INDEX && closedSet.Add(index))
                {
                    openSet.Enqueue(index);
                }
            }
        }

        public static void AssignTargets(in NativeArray<float2> positions, in NativeArray<float2> targets, NativeArray<float2> assignments)
        {
            int positionsCount = positions.Length;
            int targetCount = targets.Length;

            if (targetCount == 0)
            {
                Debug.LogWarning($"Target count have to be positive");
                return;
            }

            if (targetCount < positionsCount)
            {
                Debug.LogWarning($"Target count ({targetCount}) cannot be less than position count ({positionsCount}).");
                return;
            }

            float2 agentsCenter = float2.zero;
            for (int i = 0; i < positionsCount; i++)
            {
                agentsCenter += positions[i];
            }

            agentsCenter /= positionsCount;

            float2 targetsCenter = float2.zero;
            for (int i = 0; i < targetCount; i++)
            {
                targetsCenter += targets[i];
            }

            targetsCenter /= targetCount;

            var relativeTargets = new NativeArray<float2>(targetCount, Allocator.Temp);
            for (int i = 0; i < targetCount; i++)
            {
                relativeTargets[i] = targets[i] - targetsCenter;
            }

            var relativeTargetsCount = relativeTargets.Length;
            for (int i = 0; i < positionsCount; i++)
            {
                float2 relativePosition = positions[i] - agentsCenter;

                float bestCost = float.MaxValue;
                int bestTarget = -1;

                for (int j = 0; j < relativeTargetsCount; j++)
                {
                    float cost = math.lengthsq(relativePosition - relativeTargets[j]);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestTarget = j;
                    }
                }

                assignments[i] = relativeTargets[bestTarget] + targetsCenter;
                relativeTargets[bestTarget] = relativeTargets[--relativeTargetsCount];
            }

            relativeTargets.Dispose();
        }
    }
}