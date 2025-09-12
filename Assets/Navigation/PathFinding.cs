using System.Runtime.CompilerServices;
using CustomNativeCollections;
using HCore.Extensions;
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
            if (Triangle.SignedArea(apex, right, end) >= 0f && !GeometryUtils.NearlyEqual(apex, right) && !(Triangle.SignedArea(apex, left, end) < 0f))
            {
                resultPath.Add(left);
            }
            else if (Triangle.SignedArea(apex, left, end) <= 0f && !GeometryUtils.NearlyEqual(apex, left) && !(Triangle.SignedArea(apex, right, end) > 0f))
            {
                resultPath.Add(right);
            }
            if (resultPath.Length == 0 || !GeometryUtils.NearlyEqual(resultPath[^1], end))
            {
                resultPath.Add(end);
            }
        }

        public static void FindPath<TAttribute, TSeeker>(
            float2 startPosition, 
            int startNodeIndex,
            float2 targetPosition,
            int targetNodeIndex,
            in NativeArray<NavNode<TAttribute>> nodes,
            TSeeker seeker,
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
                    float g = current.GCost + math.distance(node.Center, neighbor.Center) * seeker.CalculateMultiplier(neighbor.Attributes);
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
                int currentNodeIndex = pathNodeIndexes[^(i+1)];
                int nextNodeIndex = pathNodeIndexes[^(i+2)];
                NavNode<TAttribute> currentNode = nodes[currentNodeIndex];

                Edge edge;
                if (currentNode.ConnectionAB == nextNodeIndex) edge = new(currentNode.CornerA, currentNode.CornerB);
                else if (currentNode.ConnectionBC == nextNodeIndex) edge = new(currentNode.CornerB, currentNode.CornerC);
                else if (currentNode.ConnectionCA == nextNodeIndex) edge = new(currentNode.CornerC, currentNode.CornerA);
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
        
        public static float2 ComputeGuidanceVector(float2 agentPosition, Portal portal, Portal nextPortal, float nextPortalImpact = 0.3f)
        {
            float2 t = math.clamp(
                ((agentPosition - portal.Left) * (portal.Right - portal.Left)) /
                math.lengthsq(portal.Right - portal.Left), 0, 1);
            float2 proj = portal.Left + t * (portal.Right - portal.Left);
            // proj.To3D().DrawPoint(Color.cyan, 5, .3f);
            
            float2 dir = math.normalize(proj - agentPosition);
            // DebugUtils.Draw(agentPosition, agentPosition + dir, Color.cyan, 5);
            
            var leftDist = math.distance(proj, nextPortal.Left);
            var rightDist = math.distance(proj, nextPortal.Right);
            var lateralPush = ((rightDist - leftDist) / (rightDist + leftDist)) * nextPortalImpact;
            float2 pushDir = new float2(-dir.y, dir.x) * lateralPush; 
            // DebugUtils.Draw(proj, proj + pushDir, Color.blue, 5);
            
            float2 guidance = dir + pushDir;
            return math.normalize(guidance);
        }
    }
}