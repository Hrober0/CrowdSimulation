using Unity.Entities;

namespace Rts
{
    /// <summary>What an agent has in its hands (design §9). Untouched by entering and leaving buildings.</summary>
    public struct Carry : IComponentData
    {
        public ItemId Item;

        public int Amount;

        public int Capacity;

        public readonly bool IsEmpty => Amount <= 0;
    }
}
