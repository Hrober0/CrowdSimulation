using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Storage
{
    /// <summary>
    /// Each frame, scans every warehouse's outgoing connections and creates delivery
    /// jobs for idle holders.
    ///
    /// Execution order:
    ///   SpatialGridRebuildSystem  (builds idle grid)
    ///   ResourceTransferSystem    (handles arrivals, frees holders)
    ///   → JobAssignmentSystem     (assigns freed holders to new jobs)
    ///   HolderMovementSystem      (moves holders, fires ArrivalTag)
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ResourceTransferSystem))]
    [UpdateBefore(typeof(HolderMovementSystem))]
    public partial struct JobAssignmentSystem : ISystem
    {
        private EntityQuery                              _storageQuery;
        private BufferLookup<StorageSlot>               _slotLookup;
        private BufferLookup<StorageConnectionElement>  _connLookup;
        private ComponentLookup<StorageComponent>       _storageLookup;
        private ComponentLookup<HolderComponent>        _holderLookup;
 
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _storageQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<StorageComponent, StorageSlot, StorageConnectionElement>()
                .Build(ref state);
 
            state.RequireForUpdate<IdleHolderGridSingleton>();
            state.RequireForUpdate<HolderComponent>();
 
            _slotLookup    = state.GetBufferLookup<StorageSlot>(isReadOnly: false);
            _connLookup    = state.GetBufferLookup<StorageConnectionElement>(isReadOnly: true);
            _storageLookup = state.GetComponentLookup<StorageComponent>(isReadOnly: true);
            _holderLookup  = state.GetComponentLookup<HolderComponent>(isReadOnly: false);
        }
 
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _slotLookup   .Update(ref state);
            _connLookup   .Update(ref state);
            _storageLookup.Update(ref state);
            _holderLookup .Update(ref state);
 
            var grid    = SystemAPI.GetSingleton<IdleHolderGridSingleton>();
            var ecb     = new EntityCommandBuffer(Allocator.Temp);
 
            // Track holders assigned this frame so they aren't double-booked.
            var assignedThisFrame = new NativeHashSet<Entity>(16, Allocator.Temp);
 
            using var storageEntities = _storageQuery.ToEntityArray(Allocator.Temp);
 
            foreach (var srcEntity in storageEntities)
            {
                var connections = _connLookup[srcEntity];
                var srcStorage  = _storageLookup[srcEntity];
                var srcSlots    = _slotLookup[srcEntity];
 
                for (int ci = 0; ci < connections.Length; ci++)
                {
                    var conn = connections[ci];
 
                    if (!conn.Active) continue;
 
                    Entity dstEntity = conn.TargetStorage;
                    if (!_slotLookup.HasBuffer(dstEntity)) continue;
 
                    var dstSlots = _slotLookup[dstEntity];
 
                    if (!TryPickAmount(srcSlots, dstSlots, conn.Resource, conn.MaxBatchSize,
                                       out int amount)) continue;
 
                    // Find nearest idle holder to the source position.
                    SpatialGridRebuildSystem.FindNearestIdleHolder(
                        in grid, srcStorage.WorldPosition, out _, out Entity holderEntity);
 
                    if (holderEntity == Entity.Null) return; // no idle holders — nothing to do
                    if (assignedThisFrame.Contains(holderEntity)) continue;
 
                    // Atomically reserve on both sides. Roll-back is built into TryReserveBoth.
                    if (!StorageSlotUtils.TryReserveBoth(ref srcSlots, ref dstSlots, conn.Resource, amount))
                        continue;
 
                    assignedThisFrame.Add(holderEntity);
 
                    // Write HolderComponent directly — immediate, no ECB delay.
                    // This ensures HolderMovementSystem sees the new state in the same frame.
                    var holder = _holderLookup[holderEntity];
                    holder.State       = HolderState.MovingToSource;
                    holder.TargetPos   = srcStorage.WorldPosition;
                    holder.CarriedType = conn.Resource;
                    holder.AssignedJob = srcEntity; // source storage — debug/UI reference
                    _holderLookup[holderEntity] = holder;
 
                    // Enable the job component via ECB (IEnableableComponent — no structural change).
                    ecb.SetComponent(holderEntity, new DeliveryJobComponent
                    {
                        SourceStorage  = srcEntity,
                        DestStorage    = dstEntity,
                        Resource       = conn.Resource,
                        ReservedAmount = amount,
                    });
                    ecb.SetComponentEnabled<DeliveryJobComponent>(holderEntity, true);
 
                    break; // one job per source storage per frame
                }
            }
 
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            assignedThisFrame.Dispose();
        }
 
        /// <summary>
        /// Returns the batch size that can actually move:
        ///   min(srcAvailable, dstFreeCapacity, maxBatch).
        /// Returns false if nothing can move (slot missing or empty/full).
        /// </summary>
        public static bool TryPickAmount(
            DynamicBuffer<StorageSlot> srcSlots,
            DynamicBuffer<StorageSlot> dstSlots,
            ResourceType resource,
            int maxBatch,
            out int amount)
        {
            amount = 0;
            if (!StorageSlotUtils.TryGetSlotIndex(srcSlots, resource, out int si)) return false;
            if (!StorageSlotUtils.TryGetSlotIndex(dstSlots, resource, out int di)) return false;
 
            int available = srcSlots[si].AvailableToDispatch;
            int capacity  = dstSlots[di].AvailableCapacity;
 
            amount = math.min(math.min(available, capacity), maxBatch);
            return amount > 0;
        }
    }
}