using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Navigation
{
    [BurstCompile]
    public struct FindPathJob<TAttribute, TSeeker> : IJob
        where TAttribute : unmanaged, INodeAttributes<TAttribute>
        where TSeeker : unmanaged, IPathSeeker<TSeeker, TAttribute>
    {
        [ReadOnly] public float2 StartPosition;
        [ReadOnly] public float2 TargetPosition;
        [ReadOnly] public NavMesh<TAttribute> NavMesh;
        [ReadOnly] public TSeeker Seeker;

        public NativeList<Portal> ResultPath;
        
        public void Execute()
        {
            if (!NavMesh.TryGetNodeIndex(StartPosition, out int startNodeIndex))
            {
                Debug.LogWarning($"{StartPosition} not found in NavMesh");
                return;
            }
            if (!NavMesh.TryGetNodeIndex(TargetPosition, out int targetNodeIndex))
            {
                Debug.LogWarning($"{TargetPosition} not found in NavMesh");
                return;
            }
            PathFinding.FindPath(StartPosition, startNodeIndex, TargetPosition, targetNodeIndex, NavMesh.Nodes, Seeker, ResultPath);
        }
    }
}