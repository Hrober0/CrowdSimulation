using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Benchmarks
{
    public class PathFindingBenchmarkRunner : MonoBehaviour
    {
        [SerializeField] private BenchmarkWorldGenerator _world;
        [SerializeField] private NavMeshBenchmarkProvider _navMeshBenchmarkProvider;
        [SerializeField] private int _queryCount = 1000;
        [SerializeField] private int _iterations = 20;
        [SerializeField] private int _seed = 777;

        [ContextMenu("Run Full PathFinding Benchmark")]
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

                var result = RunPathFindingBenchmark();
                output += $"{result:F4}\n";
            }
            Debug.Log(output);
        }
        
        [ContextMenu("Run PathFinding Benchmark")]
        public double RunPathFindingBenchmark()
        {
            _world.Generate();
            _navMeshBenchmarkProvider.Initialize(_world.TerrainSize);
            _navMeshBenchmarkProvider.UpdateNavMesh(float2.zero,_world.TerrainSize);

            var queries = GeneratePath(
                _seed,
                _queryCount,
                _world.TerrainSize);

            // Warm-up
            for (int i = 0; i < _queryCount; i++)
                _navMeshBenchmarkProvider.FindPath(queries[i].Start, queries[i].End);

            long totalTicks = 0;

            for (int i = 0; i < _iterations; i++)
            {
                var sw = Stopwatch.StartNew();

                for (int q = 0; q < _queryCount; q++)
                    _navMeshBenchmarkProvider.FindPath(queries[q].Start, queries[q].End);

                sw.Stop();
                totalTicks += sw.ElapsedTicks;
            }

            double avgMs = totalTicks * 1000.0 / (_iterations * Stopwatch.Frequency) / _queryCount;

            UnityEngine.Debug.Log($"Avg path time: {avgMs:F4} ms");
            return avgMs;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        [ContextMenu("Randomize seed")]
        public void RandomizeSeed()
        {
            _seed = UnityEngine.Random.Range(0, int.MaxValue);
        }

        public static PathQuery[] GeneratePath(
            int seed,
            int count,
            Vector2 terrainSize)
        {
            var rng = new Unity.Mathematics.Random((uint)seed);
            var queries = new PathQuery[count];

            for (int i = 0; i < count; i++)
            {
                float2 start2D = new(
                    rng.NextFloat(0f, terrainSize.x),
                    rng.NextFloat(0f, terrainSize.y));

                float2 end2D = new(
                    rng.NextFloat(0f, terrainSize.x),
                    rng.NextFloat(0f, terrainSize.y));

                queries[i] = new PathQuery(start2D, end2D);
            }

            return queries;
        }

        public readonly struct PathQuery
        {
            public readonly float2 Start;
            public readonly float2 End;

            public PathQuery(float2 start, float2 end)
            {
                Start = start;
                End = end;
            }
        }
    }
}