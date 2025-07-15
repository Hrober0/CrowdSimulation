using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using CustomNativeCollections;
using HCore.Extensions;
using UnityEngine;
using EdgeId = Navigation.NavNode.EdgeId;

namespace Navigation
{
    // [BurstCompile]
    public struct AStarFunnelJob : IJob
    {
        [ReadOnly] public PathSeekerData SeekerData;
        [ReadOnly] public float2 StartPos;
        [ReadOnly] public float2 TargetPos;
        [ReadOnly] public NativeArray<NavNode> Nodes;
        [ReadOnly] public NativeParallelMultiHashMap<int2, int> NodesPositionLookup;

        public NativeList<float2> ResultPath;

        public void Execute()
        {
            var cameFrom = new NativeHashMap<int, AStarNode>(Nodes.Length, Allocator.Temp);
            var closedSet = new NativeHashSet<int>(Nodes.Length, Allocator.Temp);
            var openSet = new NativePriorityQueue<AStarNode>(new NodeComparer(), Nodes.Length, Allocator.Temp);

            var startNodeIndex = 0; // TODO
            var targetNodeIndex = 0; // TODO

            var requiredSpace = SeekerData.Radius * 2;

            openSet.Enqueue(new(
                index: startNodeIndex,
                gCost: 0,
                fCost: math.distance(Nodes[startNodeIndex].Center, TargetPos),
                cameFromIndex: -1,
                comeFromBy: 0));

            var edgeBuffer = new NativeArray<ConnectedEdge>(3, Allocator.Temp);

            // find path to target node
            while (openSet.Count > 0)
            {
                AStarNode current = openSet.Dequeue();
                if (current.Index == targetNodeIndex)
                {
                    // found end node
                    break;
                }

                closedSet.Add(current.Index);

                NavNode triangle = Nodes[current.Index];
                edgeBuffer[0] = new(triangle.EdgeAB, triangle.ConnectionAB, EdgeId.AB);
                edgeBuffer[1] = new(triangle.EdgeAC, triangle.ConnectionAC, EdgeId.AC);
                edgeBuffer[2] = new(triangle.EdgeBC, triangle.ConnectionBC, EdgeId.BC);
                for (int i = 0; i < edgeBuffer.Length; i++)
                {
                    ConnectedEdge edge = edgeBuffer[i];
                    if (edge.ConnectedIndex == NavNode.NULL_INDEX)
                    {
                        continue;
                    }

                    if (edge.Length < requiredSpace)
                    {
                        continue;
                    }

                    if (closedSet.Contains(edge.ConnectedIndex))
                    {
                        continue;
                    }

                    NavNode neighbor = Nodes[edge.ConnectedIndex];
                    float g = current.GCost + math.distance(triangle.Center, neighbor.Center);
                    float h = math.distance(neighbor.Center, TargetPos);
                    if (cameFrom.TryGetValue(edge.ConnectedIndex, out AStarNode existing) && g >= existing.GCost)
                    {
                        continue;
                    }

                    var newNode = new AStarNode(
                        index: edge.ConnectedIndex,
                        gCost: g,
                        fCost: g + h,
                        cameFromIndex: current.Index,
                        comeFromBy: edge.EdgeId
                        );
                    cameFrom[edge.ConnectedIndex] = newNode;
                    openSet.Enqueue(newNode);
                }
            }

            // recreate path
            // first node is target node
            // last node should be start node
            var trianglePath = new NativeList<AStarNode>(Allocator.Temp);
            int currentIndex = targetNodeIndex;
            while (cameFrom.TryGetValue(currentIndex, out AStarNode node))
            {
                trianglePath.Add(node);
                currentIndex = node.CameFromIndex;
            }

            // create portals
            var portals = new NativeArray<Portal>(trianglePath.Length + 1, Allocator.Temp);
            portals[0] = new(StartPos, StartPos);
            portals[^1] = new(TargetPos, TargetPos);
            for (int i = 1; i < portals.Length - 1; i++)
            {
                // path is processed from last node to achieve correct order
                AStarNode pathNode = trianglePath[^i];
                Edge edge = Nodes[pathNode.Index].GetEdge(pathNode.ComeFromBy);
                portals[i] = new(edge.A, edge.B);
            }

            // RunFunnel(portals, ResultPath);
            foreach (var portal in portals)
            {
                Debug.DrawLine(portal.Right.To3D(), portal.Left.To3D(), Color.green, 5);
            }

            openSet.Dispose();
            cameFrom.Dispose();
            closedSet.Dispose();
            trianglePath.Dispose();
            portals.Dispose();
            edgeBuffer.Dispose();
        }

        private readonly struct AStarNode
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
        }

        private struct NodeComparer : IComparer<AStarNode>
        {
            public int Compare(AStarNode x, AStarNode y) => (int)(x.FCost - y.FCost);
        }

        private readonly struct Portal
        {
            public readonly float2 Left;
            public readonly float2 Right;

            public Portal(float2 left, float2 right)
            {
                Left = left;
                Right = right;
            }
        }

        private readonly struct ConnectedEdge
        {
            public readonly float Length;
            public readonly int ConnectedIndex;
            public readonly EdgeId EdgeId;

            public ConnectedEdge(float length, int connectedIndex, EdgeId edgeId)
            {
                Length = length;
                ConnectedIndex = connectedIndex;
                EdgeId = edgeId;
            }
        }
    }
}