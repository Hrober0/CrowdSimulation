using Unity.Entities;

namespace ComplexNavigation
{
    public struct AgentPathState : IComponentData
    {
        public int CurrentPathIndex;
    }
}