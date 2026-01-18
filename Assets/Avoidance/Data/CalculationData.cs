using Unity.Mathematics;

namespace Avoidance
{
    public struct Line
    {
        public float2 Direction;
        public float2 Point;
    }

    public struct ObstacleVertexNeighbor
    {
        public float Distance;
        public ObstacleVertex Obstacle;
    }

    public struct AgentNeighbor
    {
        public float Distance;
        public Agent Agent;
    }
}