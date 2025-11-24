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
            
            using var results = new NativeList<Portal>(128,Allocator.Temp);
            PathFinding.FindPath(startPosition, startNodeIndex, targetPosition, targetNodeIndex, NavMesh.Nodes, Seeker, results);
            
            ResultPaths.BeginForEachIndex(index);
            foreach (var result in results)
            {
                ResultPaths.Write(result);
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