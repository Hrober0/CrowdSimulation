using System;
using Navigation;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Benchmarks
{
    public class CustomNavMeshAdapter : NavMeshBenchmarkProvider
    {
        [SerializeField] private CustomNavigationObstacleProvider _obstacleProvider;
        
        private NavMesh<IdAttribute> _navMesh;
        
        public override void Initialize(float2 size)
        {
            ClearAll();
            _navMesh = new(10);
            _navMesh.AddNode(new(new(new(0, 0), new(size.x, 0), new(size.x, size.y)), new(0)));
            _navMesh.AddNode(new(new(new(0, 0), new(0, size.y), new(size.x, size.y)), new(0)));
        }

        public override void UpdateNavMesh(float2 min, float2 max)
        {
            new NavMeshUpdateJob<IdAttribute>
            {
                NavMesh = _navMesh,
                NavObstacles = _obstacleProvider.NavObstacles,
                UpdateMin = min,
                UpdateMax = max,
            }.Run();
        }

        public override void FindPath(float2 start, float2 end)
        {
            using var resultPath = new NativeList<Portal>(Allocator.TempJob);
            
            new FindPathJob<IdAttribute, SamplePathSeeker>
            {
                StartPosition = start,
                TargetPosition = end,
                NavMesh = _navMesh,
                ResultPath = resultPath
            }.Run();
        }
        
        public  void ClearAll()
        {
            if (_navMesh.IsCreated)
            {
                _navMesh.Dispose();
            }
        }

        private void OnDestroy()
        {
            ClearAll();
        }
        
        private void OnDrawGizmosSelected()
        {
            if (_navMesh.IsCreated)
            {
                _navMesh.DrawNodes(false);
            }
        }
    }
}