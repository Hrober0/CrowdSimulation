using Unity.Mathematics;

namespace AgentSimulation
{
    public struct Agent
    {
        public int ObjectId;

        public float2 Position;
        public float2 Velocity;
        public float2 PrefVelocity;
        public float MaxSpeed;
        public float Radius;

        public int MaxNeighbors;
        public float NeighborDist;
        public float TimeHorizonAgent;
        public float TimeHorizonObstacle;
    }
}