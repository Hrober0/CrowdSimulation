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
    public struct FindPathJob<T> : IJob where T : unmanaged, INodeAttributes<T>
    {
        [ReadOnly] public PathSeekerData SeekerData;
        [ReadOnly] public float2 StartPos;
        [ReadOnly] public int StartNodeIndex;
        [ReadOnly] public float2 TargetPos;
        [ReadOnly] public int TargetNodeIndex;
        [ReadOnly] public NativeArray<NavNode<T>> Nodes;

        public NativeList<float2> ResultPath;

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
                cameFromIndex: -1,
                comeFromBy: 0));

            // Find path to target node
            while (openSet.Count > 0)
            {
                AStarNode current = openSet.Dequeue();
                if (current.Index == TargetNodeIndex)
                {
                    // Found end node
                    break;
                }

                closedSet.Add(current.Index);

                NavNode<T> node = Nodes[current.Index];
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

                    NavNode<T> neighbor = Nodes[connectedIndex];
                    float g = current.GCost + math.distance(node.Center, neighbor.Center);
                    if (cameFrom.TryGetValue(connectedIndex, out AStarNode existing) && g >= existing.GCost)
                    {
                        continue;
                    }

                    float h = math.distance(neighbor.Center, TargetPos);

                    var newNode = new AStarNode(
                        index: connectedIndex,
                        gCost: g,
                        fCost: g + h,
                        cameFromIndex: current.Index,
                        comeFromBy: edgeId
                    );

                    cameFrom[connectedIndex] = newNode;
                    openSet.Enqueue(newNode);
                }
            }

            // Recreate path
            // first node is target node
            // last node should be start node
            using var trianglePath = new NativeList<AStarNode>(Allocator.Temp);
            int currentIndex = TargetNodeIndex;
            while (cameFrom.TryGetValue(currentIndex, out AStarNode node))
            {
                trianglePath.Add(node);
                currentIndex = node.CameFromIndex;
            }

            // Create portals
            var portals = new NativeArray<Portal>(trianglePath.Length + 1, Allocator.Temp);
            portals[0] = new(StartPos, StartPos);
            portals[^1] = new(TargetPos, TargetPos);
            for (int i = 1; i < portals.Length - 1; i++)
            {
                // Path is processed from last node to achieve correct order
                AStarNode pathNode = trianglePath[^i];
                Edge edge = Nodes[pathNode.Index].GetEdge(pathNode.ComeFromBy);

                // Determine left/right ordering
                portals[i] = CreatePortal(edge.A, edge.B, portals[i - 1]);
            }

            // Create final path
            FunnelPath.FromPortals(portals, ResultPath);

            edgesIds.Dispose();
            cameFrom.Dispose();
            portals.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Portal CreatePortal(float2 p1, float2 p2, Portal lastPortal)
        {
            float2 lastPortalCenter = (lastPortal.Right + lastPortal.Left) * 0.5f;
            float area = Triangle.SignedArea(lastPortalCenter, p1, p2);
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
            public readonly EdgeId ComeFromBy;

            public AStarNode(int index, float gCost, float fCost, int cameFromIndex, EdgeId comeFromBy)
            {
                Index = index;
                GCost = gCost;
                FCost = fCost;
                CameFromIndex = cameFromIndex;
                ComeFromBy = comeFromBy;
            }

            public int CompareTo(AStarNode other) => FCost.CompareTo(other.FCost);
        }
    }
}