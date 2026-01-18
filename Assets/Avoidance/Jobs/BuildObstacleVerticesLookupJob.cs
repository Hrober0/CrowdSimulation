using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Avoidance
{
    [BurstCompile]
    public struct BuildObstacleVerticesLookupJob : IJobParallelFor
    {
        [ReadOnly] public NativeList<ObstacleVertex> Vertices;
        [ReadOnly] public float ChunkSizeMultiplier;

        [WriteOnly] public NativeParallelMultiHashMap<int, int>.ParallelWriter Lookup;

        public void Execute(int verticeIndex)
        {
            var vertex = Vertices[verticeIndex];
            var startPosition = vertex.Point;
            var endPosition = Vertices[vertex.Next].Point;

            var minX = (int)(math.min(startPosition.x, endPosition.x) * ChunkSizeMultiplier);
            var maxX = (int)(math.max(startPosition.x, endPosition.x) * ChunkSizeMultiplier);
            var minY = (int)(math.min(startPosition.y, endPosition.y) * ChunkSizeMultiplier);
            var maxY = (int)(math.max(startPosition.y, endPosition.y) * ChunkSizeMultiplier);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Lookup.Add(RVOMath.ChunkHash(x, y), verticeIndex);
                }
            }
        }
    }
}
