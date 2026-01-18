using UnityEngine;
using System.Diagnostics;
using System.Linq;
using Unity.Mathematics;

namespace Benchmarks
{
    public sealed class NavMeshBenchmarkRunner : MonoBehaviour
    {
        [SerializeField] private BenchmarkWorldGenerator _world;
        [SerializeField] private NavMeshBenchmarkProvider _navMeshBenchmarkProvider;
        [SerializeField] private Vector2 _updateSize = new Vector2(1, 3);
        [SerializeField] private int _iterations = 200;
        [SerializeField] private int _seed = 777;

        [ContextMenu("Run Build Benchmark")]
        public void RunBuildBenchmark()
        {
            _world.Generate();

            var updateAreas = GenerateUpdateAreas(_seed, _iterations, _updateSize, _world.TerrainSize);

            // Warm-up
            for (int i = 0; i < _iterations; i++)
                _navMeshBenchmarkProvider.Update(updateAreas[i].Min, updateAreas[i].Max);

            long totalTicks = 0;

            for (int i = 0; i < _iterations; i++)
            {
                var sw = Stopwatch.StartNew();

                _navMeshBenchmarkProvider.Update(updateAreas[i].Min, updateAreas[i].Max);

                sw.Stop();
                totalTicks += sw.ElapsedTicks;
            }

            double avgMs = totalTicks * 1000.0 / (_iterations * Stopwatch.Frequency);

            UnityEngine.Debug.Log($"Avg build time: {avgMs:F4} ms");

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        [ContextMenu("Randomize seed")]
        public void RandomizeSeed()
        {
            _seed = UnityEngine.Random.Range(0, int.MaxValue);
        }

        public static UpdateArea[] GenerateUpdateAreas
        (
            int seed,
            int count,
            Vector2 updateSize,
            Vector2 terrainSize)
        {
            var rng = new Unity.Mathematics.Random((uint)seed);
            var queries = new UpdateArea[count];

            for (int i = 0; i < count; i++)
            {
                float2 min = new(
                    rng.NextFloat(0f, terrainSize.x),
                    rng.NextFloat(0f, terrainSize.y));

                float2 size = new(
                    rng.NextFloat(updateSize.x, updateSize.y),
                    rng.NextFloat(updateSize.x, updateSize.y));

                queries[i] = new UpdateArea(min, min + size);
            }

            return queries;
        }

        public readonly struct UpdateArea
        {
            public readonly float2 Min;
            public readonly float2 Max;

            public UpdateArea(float2 min, float2 max)
            {
                Min = min;
                Max = max;
            }
        }
    }
}