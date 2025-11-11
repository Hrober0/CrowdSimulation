using Unity.Entities;

namespace ComplexNavigation
{
    public struct AgentMovementData : IComponentData
    {
        public float MovementSpeed;
        public float RotationSpeed;
    }
}
