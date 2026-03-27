using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Examples.Storage
{
    /// <summary>
    /// Drives the holder state machine when an arrival is signalled.
    /// Runs before JobAssignmentSystem so freshly freed holders are visible to
    /// the spatial grid on the same frame they become idle.
    ///
    /// ArrivalTag is read and immediately disabled via EnabledRefRW — no ECB,
    /// no structural change, no one-frame lag.
    /// DeliveryJobComponent is disabled via ECB after delivery (still no
    /// structural change — IEnableableComponent).
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpatialGridRebuildSystem))]
    [UpdateBefore(typeof(HoldersJobAssignmentSystem))]
    public partial struct HolderResourceTransferSystem : ISystem
    {
        private BufferLookup<StorageSlot>         _slotLookup;
        private ComponentLookup<StorageComponent> _storageLookup;
 
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HolderComponent>();
            _slotLookup    = state.GetBufferLookup<StorageSlot>(isReadOnly: false);
            _storageLookup = state.GetComponentLookup<StorageComponent>(isReadOnly: true);
        }
 
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _slotLookup   .Update(ref state);
            _storageLookup.Update(ref state);
 
            var ecb = new EntityCommandBuffer(Allocator.Temp);
 
            // Query matches only holders where BOTH ArrivalTag AND DeliveryJobComponent are enabled.
            foreach (var (holderRW, jobRO, arrivalEnabledRW, entity) in
                SystemAPI
                    .Query<RefRW<HolderComponent>,
                           RefRO<DeliveryJobComponent>,
                           EnabledRefRW<ArrivalTag>>()
                    .WithEntityAccess())
            {
                // Disable ArrivalTag immediately — no re-triggering this frame.
                arrivalEnabledRW.ValueRW = false;
 
                ProcessArrival(
                    ref holderRW.ValueRW,
                    in  jobRO.ValueRO,
                    entity,
                    ref _slotLookup,
                    in  _storageLookup,
                    ref ecb);
            }
 
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
 
        // ── State machine ─────────────────────────────────────────────────────
 
        /// <summary>
        /// Handles one arrival event for a holder.
        /// Static so the logic is unit-testable without running a full ECS world.
        /// </summary>
        public static void ProcessArrival(
            ref HolderComponent              holder,
            in  DeliveryJobComponent         job,
            Entity                           holderEntity,
            ref BufferLookup<StorageSlot>    slotLookup,
            in  ComponentLookup<StorageComponent> storageLookup,
            ref EntityCommandBuffer          ecb)
        {
            switch (holder.State)
            {
                case HolderState.MovingToSource:
                    HandlePickup(ref holder, in job, ref slotLookup);
 
                    // Redirect toward destination.
                    if (storageLookup.HasComponent(job.DestStorage))
                        holder.TargetPos = storageLookup[job.DestStorage].WorldPosition;
 
                    holder.State = HolderState.MovingToDest;
                    break;
 
                case HolderState.MovingToDest:
                    HandleDelivery(ref holder, in job, ref slotLookup);
 
                    holder.State       = HolderState.Idle;
                    holder.AssignedJob = Unity.Entities.Entity.Null;
 
                    // Disable job component — IEnableableComponent, no structural change.
                    ecb.SetComponentEnabled<DeliveryJobComponent>(holderEntity, false);
                    break;
            }
        }
 
        // ── Transfer helpers — static, unit-testable ──────────────────────────
 
        /// <summary>
        /// Picks up resources from the source slot, releasing the outgoing reservation.
        /// Updates holder.CurrentLoad with actual units taken.
        /// </summary>
        public static void HandlePickup(
            ref HolderComponent           holder,
            in  DeliveryJobComponent      job,
            ref BufferLookup<StorageSlot> slotLookup)
        {
            if (!slotLookup.HasBuffer(job.SourceStorage) ||
                !slotLookup.HasBuffer(job.DestStorage))
                return;
 
            var srcSlots = slotLookup[job.SourceStorage];
            var dstSlots = slotLookup[job.DestStorage];
 
            // ExecutePickup corrects dstSlots.ReservedIncoming for any shortfall.
            int actual = StorageSlotUtils.ExecutePickup(
                ref srcSlots, ref dstSlots, job.Resource, job.ReservedAmount);
 
            holder.CurrentLoad = actual;
            holder.CarriedType = job.Resource;
        }
 
        /// <summary>
        /// Deposits resources at the destination slot, releasing the incoming reservation.
        /// Resets holder load to zero.
        /// </summary>
        public static void HandleDelivery(
            ref HolderComponent           holder,
            in  DeliveryJobComponent      job,
            ref BufferLookup<StorageSlot> slotLookup)
        {
            if (!slotLookup.HasBuffer(job.DestStorage))
                return;
 
            var dstSlots = slotLookup[job.DestStorage];
 
            // Pass job.ReservedAmount so ExecuteDelivery can clear the correct reservation,
            // even if holder.CurrentLoad ended up smaller due to a shortfall at pickup.
            StorageSlotUtils.ExecuteDelivery(
                ref dstSlots, job.Resource, holder.CurrentLoad, job.ReservedAmount);
 
            holder.CurrentLoad = 0;
            holder.CarriedType = ResourceType.None;
        }
    }
}