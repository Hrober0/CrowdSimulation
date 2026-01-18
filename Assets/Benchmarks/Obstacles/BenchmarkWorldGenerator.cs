using Unity.Mathematics;
using UnityEngine;

namespace Benchmarks
{
    public sealed class BenchmarkWorldGenerator : MonoBehaviour
    {
        [SerializeField] private int _seed = 12345;
        [SerializeField] private Vector2 _terrainSize = new(100, 100);
        [SerializeField] private int _obstacleCount = 200;
        [SerializeField] private Vector2 _obstacleSizeRange = new(1.5f, 4f);
        [SerializeField] private MonoBehaviour _obstacleProviderBehaviour;

        private IObstacleProvider Provider => (IObstacleProvider)_obstacleProviderBehaviour;

        [ContextMenu("Create")]
        public void Generate()
        {
            Provider.ClearAll();
            Provider.Initialize(_terrainSize);

            var rng = new Unity.Mathematics.Random((uint)_seed);

            for (int i = 0; i < _obstacleCount; i++)
            {
                float size = rng.NextFloat(
                    _obstacleSizeRange.x,
                    _obstacleSizeRange.y);

                float2 pos = new float2(
                    rng.NextFloat(0, _terrainSize.x),
                    rng.NextFloat(0, _terrainSize.y));

                Provider.SpawnObstacle(pos, size);
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        [ContextMenu("Clear")]
        public void Clear() => Provider.ClearAll();

        [ContextMenu("Randomize seed")]
        public void RandomizeSeed()
        {
            _seed = UnityEngine.Random.Range(0, int.MaxValue);
        }

        public void SetConfig(float terrainSize, int obstacleCount)
        {
            _terrainSize = new Vector2(terrainSize, terrainSize);
            _obstacleCount = obstacleCount;
        }

        public Vector2 TerrainSize => _terrainSize;
        public int Seed => _seed;
        public int ObstacleCount => _obstacleCount;
    }
}