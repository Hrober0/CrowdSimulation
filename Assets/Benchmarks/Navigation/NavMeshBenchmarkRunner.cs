using UnityEngine;
using System.Diagnostics;
using Unity.Mathematics;
using Debug = UnityEngine.Debug;

namespace Benchmarks
{
    public sealed class NavMeshBenchmarkRunner : MonoBehaviour
    {
        [SerializeField] private BenchmarkWorldGenerator _world;
        [SerializeField] private NavMeshBenchmarkProvider _navMeshBenchmarkProvider;
        [SerializeField] private Vector2 _updateSize = new Vector2(1, 3);
        [SerializeField] private int _iterations = 200;
        [SerializeField] private int _seed = 777;
        [SerializeField] private bool _updateAll = false;

        [ContextMenu("Run Full Build Benchmark")]
        public void RunFullBenchmark()
        {
            var configs = new (float worldSize, int obstacleCount)[]
            {
                (500f, 1000),
                (500f, 800),
                (500f, 600),
                (500f, 400),
                (500f, 200),
                (500f, 100),

                (300f, 1000),
                (300f, 800),
                (300f, 600),
                (300f, 400),
                (300f, 200),
                (300f, 100),

                (100f, 1000),
                (100f, 800),
                (100f, 600),
                (100f, 400),
                (100f, 200),
                (100f, 100),
            };

            var output = "";
            foreach (var config in configs)
            {
                _world.SetConfig(config.worldSize, config.obstacleCount);

                var result = RunBuildBenchmark();
                output += $"{result:F4}\n";
            }

            Debug.Log(output);
        }

        [ContextMenu("Run Build Benchmark")]
        public double RunBuildBenchmark()
        {
            _world.Generate();

            var updateAreas = _updateAll
                ? GenerateFullUpdateAreas(_iterations, _world.TerrainSize)
                : GenerateUpdateAreas(_seed, _iterations, _updateSize, _world.TerrainSize);
            
            _navMeshBenchmarkProvider.Initialize(_world.TerrainSize);

            // Warm-up
            for (int i = 0; i < _iterations; i++)
                _navMeshBenchmarkProvider.UpdateNavMesh(updateAreas[i].Min, updateAreas[i].Max);
            
            long totalTicks = 0;

            for (int i = 0; i < _iterations; i++)
            {
                var sw = Stopwatch.StartNew();

                _navMeshBenchmarkProvider.UpdateNavMesh(updateAreas[i].Min, updateAreas[i].Max);

                sw.Stop();
                totalTicks += sw.ElapsedTicks;
            }

            double avgMs = totalTicks * 1000.0 / (_iterations * Stopwatch.Frequency);

            Debug.Log($"Avg build time: {avgMs:F4} ms");

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            return avgMs;
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

        public static UpdateArea[] GenerateFullUpdateAreas
        (
            int count,
            Vector2 terrainSize)
        {
            var queries = new UpdateArea[count];

            for (int i = 0; i < count; i++)
            {
                queries[i] = new UpdateArea(float2.zero, terrainSize);
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