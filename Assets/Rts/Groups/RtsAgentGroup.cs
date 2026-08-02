using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Everything an agent does every frame (design §13.1): spatial hash, task steps, path following,
    /// avoidance, integration, interactions. This is where the parallel work lives.
    ///
    /// Runs after <see cref="RtsEconomyGroup"/>, so an order claimed this tick is walked the same frame.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(RtsEconomyGroup))]
    public partial class RtsAgentGroup : ComponentSystemGroup
    {
    }
}
