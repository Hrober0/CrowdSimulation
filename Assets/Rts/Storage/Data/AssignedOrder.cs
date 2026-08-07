using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// The order this agent has claimed (design §9). Enabled from the claim until the task finishes or is
    /// given up on; an agent with this disabled is free to be handed something else, or to go and rest.
    ///
    /// <see cref="Amount"/> is what was *reserved* at both ends, which is not always what ends up carried -
    /// something can eat the stock while the hauler walks. Keeping the reserved figure is what lets the
    /// release path give back exactly what it took.
    /// </summary>
    public struct AssignedOrder : IComponentData, IEnableableComponent
    {
        public OrderKind Kind;

        public Entity Source;

        public Entity Target;

        public ItemId Item;

        public int Amount;
    }

    /// <summary>
    /// How many haulers may work one building at once (design §8). Optional: a building without one uses the
    /// default. This is the hard cap behind the reservations - a construction site wanting five hundred
    /// planks posts the demand, but only this many trips are ever in flight, so throughput is bounded by
    /// hauler count instead of by demand size.
    /// </summary>
    public struct HaulLimit : IComponentData
    {
        public int MaxConcurrent;
    }
}
