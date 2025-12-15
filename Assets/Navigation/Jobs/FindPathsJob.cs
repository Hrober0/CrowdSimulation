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
                ResultPaths.Write(new PathPortal
                {
                    Left = portal.Left,
                    Right = portal.Right,
                    PathPoint = pathPoints[i],
                });
            }

            float2 lastPoint = pathPoints.Length > 0 ? pathPoints[^1] : startPosition;
            float2 perp = math.normalize(new float2(targetPosition.y - lastPoint.y, lastPoint.x - targetPosition.x));
            ResultPaths.Write(new PathPortal
            {
                Left = targetPosition - perp,
                Right = targetPosition + perp,
                PathPoint = targetPosition,
            });

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