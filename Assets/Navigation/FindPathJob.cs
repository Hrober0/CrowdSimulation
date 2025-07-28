using System;
using System.Collections.Generic;
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
    public struct FindPathJob : IJob
    {
        [ReadOnly] public PathSeekerData SeekerData;
        [ReadOnly] public float2 StartPos;
        [ReadOnly] public int StartNodeIndex;
        [ReadOnly] public float2 TargetPos;
        [ReadOnly] public int TargetNodeIndex;
        [ReadOnly] public NativeArray<NavNode> Nodes;

        public NativeList<float2> ResultPath;

        public void Execute()
        {
            var cameFrom = new NativeHashMap<int, AStarNode>(Nodes.Length, Allocator.Temp);
            var closedSet = new NativeHashSet<int>(Nodes.Length, Allocator.Temp);
            var openSet = new NativePriorityQueue<AStarNode>(Nodes.Length, Allocator.Temp);

            var requiredSpace = SeekerData.Radius * 2;

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

                NavNode triangle = Nodes[current.Index];
                TryProcessEdge(triangle.EdgeAB, triangle.ConnectionAB, EdgeId.AB, triangle.Center, requiredSpace, TargetPos, current, cameFrom, closedSet, Nodes, openSet);
                TryProcessEdge(triangle.EdgeAC, triangle.ConnectionAC, EdgeId.AC, triangle.Center, requiredSpace, TargetPos, current, cameFrom, closedSet, Nodes, openSet);
                TryProcessEdge(triangle.EdgeBC, triangle.ConnectionBC, EdgeId.BC, triangle.Center, requiredSpace, TargetPos, current, cameFrom, closedSet, Nodes, openSet);
            }

            // Recreate path
            // first node is target node
            // last node should be start node
            var trianglePath = new NativeList<AStarNode>(Allocator.Temp);
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
                Portal portal = CreatePortal(edge.A, edge.B, portals[i - 1]);

                // Shrink portal by radius
                float2 shrinkOffset = math.normalize(portal.Right - portal.Left) * requiredSpace * 0.5f;
                portal = new(portal.Left + shrinkOffset, portal.Right - shrinkOffset);

                portals[i] = portal;
            }
            
            // Create final path
            FunnelPath.FromPortals(portals, ResultPath);
            
            openSet.Dispose();
            cameFrom.Dispose();
            closedSet.Dispose();
            trianglePath.Dispose();
            portals.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Portal CreatePortal(float2 p1, float2 p2, Portal lastPortal)
        {
            float2 lastPortalCenter = (lastPortal.Right + lastPortal.Left) * 0.5f;
            float area = Triangle.Area2(lastPortalCenter, p1, p2);
            Portal portal = area < 0
                ? new(p1, p2)
                : new(p2, p1);
            return portal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TryProcessEdge(
            float length,
            int connectedIndex,
            EdgeId edgeId,
            float2 nodeCenter,
            float requiredSpace,
            float2 targetPos,
            AStarNode current,
            NativeHashMap<int, AStarNode> cameFrom,
            NativeHashSet<int> closedSet,
            NativeArray<NavNode> nodes,
            NativePriorityQueue<AStarNode> openSet)
        {
            if (connectedIndex == NavNode.NULL_INDEX || length < requiredSpace)
            {
                return;
            }

            if (closedSet.Contains(connectedIndex))
            {
                return;
            }

            NavNode neighbor = nodes[connectedIndex];
            float g = current.GCost + math.distance(nodeCenter, neighbor.Center);
            if (cameFrom.TryGetValue(connectedIndex, out AStarNode existing) && g >= existing.GCost)
            {
                return;
            }
            
            float h = math.distance(neighbor.Center, targetPos);

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