using Unity.Entities;

namespace Examples.Storage
{
    /// <summary>
    /// Carries all data a holder needs to complete one delivery cycle.
    /// Implemented as IEnableableComponent: added once to the holder archetype
    /// at spawn, then enabled/disabled per-job — zero structural changes.
    ///
    /// Enabled  by JobAssignmentSystem  when a job is found.
    /// Disabled by ResourceTransferSystem after delivery is confirmed.
    /// </summary>
    public struct DeliveryJobComponent : IComponentData, IEnableableComponent
    {
        public Entity       SourceStorage;
        public Entity       DestStorage;
        public ResourceType Resource;
        public int          ReservedAmount; // units locked on both src and dst slots
    }
}