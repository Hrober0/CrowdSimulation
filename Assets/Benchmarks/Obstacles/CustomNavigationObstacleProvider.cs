using System;
using Navigation;
using Unity.Mathematics;
using UnityEngine;

namespace Benchmarks
{
    public class CustomNavigationObstacleProvider : MonoBehaviour, IObstacleProvider
    {
        private NavObstacles<IdAttribute> _navObstacles;
        public NavObstacles<IdAttribute> NavObstacles => _navObstacles;

        public void Initialize(float2 size)
        {
            ClearAll();
            _navObstacles = new(5);
        }

        public void SpawnObstacle(float2 position, float size)
        {
            _navObstacles.AddObstacle(new IdAttribute().Empty(), new[]
            {
                new float2(position.x, position.y),
                new float2(position.x + size, position.y),
                new float2(position.x + size, position.y + size),
                new float2(position.x, position.y + size),
            });
        }

        public void ClearAll()
        {
            if (_navObstacles.IsCreated) _navObstacles.Dispose();
        }

        private void OnDestroy()
        {
            ClearAll();
        }

        private void OnDrawGizmosSelected()
        {
            if (_navObstacles.IsCreated)
            {
                _navObstacles.DrawEdges();
            }
        }
    }
}