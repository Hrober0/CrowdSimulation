using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using CustomNativeCollections;
using UnityEngine;
using EdgeId = Navigation.NavNode.EdgeId;

namespace Navigation
{
    [BurstCompile]
    public struct FindPathJob<TAttribute, TSeeker> : IJob
        where TAttribute : unmanaged, INodeAttributes<TAttribute>
        where TSeeker : unmanaged, IPathSeeker<TSeeker, TAttribute>
    {
        [ReadOnly] public float2 StartPos;
        [ReadOnly] public int StartNodeIndex;
        [ReadOnly] public float2 TargetPos;
        [ReadOnly] public int TargetNodeIndex;
        [ReadOnly] public NativeArray<NavNode<TAttribute>> Nodes;
        [ReadOnly] public TSeeker Seeker;

        public NativeList<Portal> ResultPath;

        public void Execute()
        {
            var cameFrom = new NativeHashMap<int, AStarNode>(Nodes.Length, Allocator.Temp);
            using var closedSet = new NativeHashSet<int>(Nodes.Length, Allocator.Temp);
            using var openSet = new NativePriorityQueue<AStarNode>(Nodes.Length, Allocator.Temp);

            var edgesIds = new NativeArray<EdgeId>(3, Allocator.Temp);
            edgesIds[0] = EdgeId.AB;
            edgesIds[1] = EdgeId.BC;
            edgesIds[2] = EdgeId.CA;

            openSet.Enqueue(new(
                index: StartNodeIndex,
                gCost: 0,
                fCost: math.distance(Nodes[StartNodeIndex].Center, TargetPos),
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
                else if (current.Index == TargetNodeIndex)
                {
                    foundTargetNode = true;
                }

                closedSet.Add(current.Index);

                NavNode<TAttribute> node = Nodes[current.Index];
                foreach (EdgeId edgeId in edgesIds)
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

                    NavNode<TAttribute> neighbor = Nodes[connectedIndex];
                    float g = current.GCost + math.distance(node.Center, neighbor.Center) * Seeker.CalculateMultiplier(neighbor.Attributes);
                    float h = math.distance(neighbor.Center, TargetPos);
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
            int currentIndex = TargetNodeIndex;
            while (cameFrom.TryGetValue(currentIndex, out AStarNode node))
            {
                pathNodeIndexes.Add(node.Index);
                currentIndex = node.CameFromIndex;
            }
            pathNodeIndexes.Add(StartNodeIndex);

            // Create portals
            var lastPortal = new Portal(StartPos, StartPos);
            for (int i = 0; i < pathNodeIndexes.Length - 1; i++)
            {
                // Path is processed from last node to achieve correct order
                int currentNodeIndex = pathNodeIndexes[^(i+1)];
                int nextNodeIndex = pathNodeIndexes[^(i+2)];
                NavNode<TAttribute> currentNode = Nodes[currentNodeIndex];

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
                ResultPath.Add(newPortal);
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

        private readonly struct AStarNode : IComparable<AStarNode>
        {
            public readonly int Index;
            public readonly float GCost;
            public readonly float FCost;
            public readonly int CameFromIndex;

            public AStarNode(int index, float gCost, float fCost, int cameFromIndex)
            {
                Index = index;
                GCost = gCost;
                FCost = fCost;
                CameFromIndex = cameFromIndex;
            }

            public int CompareTo(AStarNode other) => FCost.CompareTo(other.FCost);
        }
    }
}