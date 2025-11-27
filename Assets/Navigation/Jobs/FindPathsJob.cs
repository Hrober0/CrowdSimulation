using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    [BurstCompile]
    public struct FindPathsJob<TAttribute, TSeeker> : IJobParallelFor
        where TAttribute : unmanaged, INodeAttributes<TAttribute>
        where TSeeker : unmanaged, IPathSeeker<TSeeker, TAttribute>
    {
        [ReadOnly] public NativeArray<StartAndTarget> StartAndTargetEntry;
        [ReadOnly] public NavMesh<TAttribute> NavMesh;
        [ReadOnly] public TSeeker Seeker;

        [WriteOnly] public NativeStream.Writer ResultPaths;

        public void Execute(int index)
        {
            var (startPosition, targetPosition) = StartAndTargetEntry[index];
            if (!NavMesh.TryGetNodeIndex(startPosition, out int startNodeIndex))
            {
                Debug.LogWarning($"{startPosition} not found in NavMesh");
                return;
            }

            if (!NavMesh.TryGetNodeIndex(targetPosition, out int targetNodeIndex))
            {
                Debug.LogWarning($"{targetPosition} not found in NavMesh");
                return;
            }

            using var portals = new NativeList<Portal>(128, Allocator.Temp);
            PathFinding.FindPath(startPosition, startNodeIndex, targetPosition, targetNodeIndex, NavMesh.Nodes, Seeker, portals);

            using var pathPoints = new NativeArray<float2>(portals.Length, Allocator.Temp);
            PathFinding.FunnelPortals(startPosition, targetPosition, portals.AsArray(), pathPoints);

            ResultPaths.BeginForEachIndex(index);
            for (var i = 0; i < portals.Length; i++)
            {
                Portal portal = portals[i];
                Portal lastPortal = i > 0 ? portals[i - 1] : new Portal(startPosition, startPosition);

                float2 dirRight = math.normalizesafe(portal.Right - lastPortal.Right);
                float2 dirLeft = math.normalizesafe(portal.Left - lastPortal.Left);
                float2 flowDirection = GeometryUtils.NormalizeSum(dirRight, dirLeft);

                ResultPaths.Write(new PathPortal
                {
                    Left = portal.Left,
                    Right = portal.Right,
                    Path = pathPoints[i] + flowDirection * 0.1f,
                    Direction = flowDirection,
                });
            }

            ResultPaths.EndForEachIndex();
        }
    }

    public readonly struct StartAndTarget
    {
        public readonly float2 StartPosition;
        public readonly float2 TargetPosition;

        public StartAndTarget(float2 startPosition, float2 targetPosition)
        {
            StartPosition = startPosition;
            TargetPosition = targetPosition;
        }

        public void Deconstruct(out float2 startPosition, out float2 targetPosition)
        {
            startPosition = StartPosition;
            targetPosition = TargetPosition;
        }
    }
}