using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Avoidance
{
    [BurstCompile]
    public struct BuildAgentLookupJob : IJobParallelFor
    {
        [ReadOnly] public NativeList<Agent> Agents;
        [ReadOnly] public float ChunkSizeMultiplier;

        [WriteOnly] public NativeParallelMultiHashMap<int, int>.ParallelWriter Lookup;

        public void Execute(int agentIndex)
        {
            var position = Agents[agentIndex].Position;
            var radius = Agents[agentIndex].Radius;

            var minX = (int)((position.x - radius) * ChunkSizeMultiplier);
            var maxX = (int)((position.x + radius) * ChunkSizeMultiplier);
            var minY = (int)((position.y - radius) * ChunkSizeMultiplier);
            var maxY = (int)((position.y + radius) * ChunkSizeMultiplier);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Lookup.Add(RVOMath.ChunkHash(x, y), agentIndex);
                }
            }
        }
    }
}
