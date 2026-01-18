using Unity.Mathematics;

namespace Benchmarks
{
    public interface IObstacleProvider
    {
        void Initialize(float2 size);
        void SpawnObstacle(float2 position, float size);
        void ClearAll();
    }
}