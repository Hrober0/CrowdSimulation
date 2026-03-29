using Unity.Entities;

namespace Examples.Storage
{
    /// <summary>
    /// Lives on a dedicated connection entity (one per storage pair + resource).
    /// Both StorageA and StorageB hold a <see cref="ConnectionRefElement"/> pointing here.
    /// </summary>
    public struct StorageConnectionComponent : IComponentData
    {
        public Entity        StorageA;
        public Entity        StorageB;
        public ResourceType  Resource;
        public byte          Priority;        // 0–255, higher = processed first
        public byte          MaxBatchSize;
        public ConnectionMode Mode;
        public double        LastPickupTime;  // ElapsedTime when a job was last dispatched; ties broken by oldest-first
    }

    public enum ConnectionMode : byte
    {
        Disabled = 0,
        TwoWays  = 1,   // balance fill-ratios between A and B
        AToB     = 2,   // transfer from A to B
        BToA     = 3,   // transfer from B to A
    }
}
