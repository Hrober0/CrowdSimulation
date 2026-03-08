using Unity.Entities;

namespace Examples.Storage
{
    public struct StorageSlot : IBufferElementData
    {
        public ResourceType Resource;
        public int CurrentAmount;
        public int Capacity;
        public int ReservedOutgoing; // locked at source, not yet picked up
        public int ReservedIncoming; // heading here, not yet delivered

        public readonly int AvailableToDispatch
            => CurrentAmount - ReservedOutgoing;

        public readonly int AvailableCapacity
            => Capacity - CurrentAmount - ReservedIncoming;
    }
}