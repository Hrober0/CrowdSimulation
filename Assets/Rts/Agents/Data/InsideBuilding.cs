using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// The agent is inside <see cref="Building"/> and off the map (design §6).
    ///
    /// Enableable rather than added and removed, which is what makes entering and leaving a building cost
    /// nothing structural: one archetype, no chunk moves, and carried load, health and identity untouched.
    /// </summary>
    public struct InsideBuilding : IComponentData, IEnableableComponent
    {
        public Entity Building;
    }

    /// <summary>
    /// A slot held in <see cref="Building"/>'s <see cref="Interior"/> for an agent that has not arrived yet.
    ///
    /// Claim before approach is the admission control of §6 and §8: an agent that cannot get a slot never
    /// starts walking, so a building's doorway never collects a crowd of arrivals it has no room for.
    /// Enabled from the claim until the agent leaves again.
    /// </summary>
    public struct InteriorClaim : IComponentData, IEnableableComponent
    {
        public Entity Building;
    }
}
