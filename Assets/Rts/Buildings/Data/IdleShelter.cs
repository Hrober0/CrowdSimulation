using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// This building takes agents that have nothing to do - the haulers' hut of design §6 and §8.
    ///
    /// A tag rather than a building type, so any building the player wants to double as a rest stop becomes
    /// one by carrying it. Idle claiming is self-balancing on top: an agent picks the nearest tagged building
    /// with room, so building or losing a hut needs no rebalancing pass anywhere.
    /// </summary>
    public struct IdleShelter : IComponentData
    {
    }
}
