using Unity.Entities;

namespace Examples.Storage
{
    public struct StorageConnectionElement : IBufferElementData
    {
        public Entity TargetStorage;
        public ResourceType Resource; // which resource this link transfers
        public byte Priority; // 0–255, player-set
        public byte MaxBatchSize;
        public ConnectionFlags Flags;
    }

    public enum ConnectionFlags : byte
    {
        Enabled,
        PlayerOverride,
        OneWay
    }
}