using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples.Storage
{
    /// <summary>
    /// Every 20 frames, assigns delivery jobs to idle holders.
    ///
    /// Iterates all connection entities sorted by priority (highest first).
    /// Each effective source storage gets at most one job per run.
    ///
    /// Execution order:
    ///   SpatialGridRebuildSystem        (builds idle grid)
    ///   HolderResourceTransferSystem    (handles arrivals, frees holders)
    ///   → HoldersJobAssignmentSystem
    ///   HolderMovementSystem            (moves holders, fires ArrivalTag)
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(HolderResourceTransferSystem))]
    [UpdateBefore(typeof(HolderMovementSystem))]
    public partial struct HoldersJobAssignmentSystem : ISystem
    {
        private EntityQuery _connQuery;
        private BufferLookup<StorageSlot> _slotLookup;
        private ComponentLookup<StorageConnectionComponent> _connDataLookup;
        private ComponentLookup<StorageComponent> _storageLookup;
        private ComponentLookup<HolderComponent> _holderLookup;
        private int _frameCounter;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _connQuery = new EntityQueryBuilder(Allocator.Temp)
                         .WithAll<StorageConnectionComponent>()
                         .Build(ref state);

            state.RequireForUpdate<IdleHolderGridSingleton>();
            state.RequireForUpdate<HolderComponent>();

            _slotLookup = state.GetBufferLookup<StorageSlot>(isReadOnly: false);
            _connDataLookup = state.GetComponentLookup<StorageConnectionComponent>(isReadOnly: true);
            _storageLookup = state.GetComponentLookup<StorageComponent>(isReadOnly: true);
            _holderLookup = state.GetComponentLookup<HolderComponent>(isReadOnly: false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (++_frameCounter % 20 != 0) return;

            var grid = SystemAPI.GetSingleton<IdleHolderGridSingleton>();
            var availableHolder = grid.Cells.Count();
            if (availableHolder == 0) return;

            _slotLookup.Update(ref state);
            _connDataLookup.Update(ref state);
            _storageLookup.Update(ref state);
            _holderLookup.Update(ref state);

            // Build a priority-sorted list of active connection entities.
            using var connEntities = _connQuery.ToEntityArray(Allocator.Temp);
            var sorted = new NativeList<ConnSort>(connEntities.Length, Allocator.Temp);
            for (int i = 0; i < connEntities.Length; i++)
            {
                var c = _connDataLookup[connEntities[i]];
                if (c.Mode != ConnectionMode.Disabled)
                {
                    sorted.Add(new ConnSort { Connection = connEntities[i], Priority = c.Priority });
                }
            }

            if (sorted.Length == 0)
            {
                sorted.Dispose();
                return;
            }

            sorted.Sort(new PriorityDescComparer());

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var assignedSources = new NativeHashSet<Entity>(256, Allocator.Temp);
            var assignedHolders = new NativeHashSet<Entity>(256, Allocator.Temp);

            for (int ci = 0; ci < sorted.Length; ci++)
            {
                var conn = _connDataLookup[sorted[ci].Connection];

                if (!_slotLookup.HasBuffer(conn.StorageA) || !_slotLookup.HasBuffer(conn.StorageB))
                {
                    continue;
                }

                DynamicBuffer<StorageSlot> slotsA = _slotLookup[conn.StorageA];
                DynamicBuffer<StorageSlot> slotsB = _slotLookup[conn.StorageB];
                if (!StorageSlotUtils.TryGetSlotIndex(slotsA, conn.Resource, out int slotAIndex) ||
                    !StorageSlotUtils.TryGetSlotIndex(slotsB, conn.Resource, out int slotBIndex))
                {
                    continue;
                }

                // Determine effective transfer direction.
                Entity sourceStorageEntity = conn.StorageA;
                int sourceSlotIndex = slotAIndex;
                Entity targetStorageEntity = conn.StorageB;
                int targetSlotIndex = slotBIndex;
                if (conn.Mode == ConnectionMode.BToA)
                {
                    sourceStorageEntity = conn.StorageB;
                    sourceSlotIndex = slotBIndex;
                    targetStorageEntity = conn.StorageA;
                    targetSlotIndex = slotAIndex;
                }
                else if (conn.Mode == ConnectionMode.TwoWays)
                {
                    if (slotsA[slotAIndex].Fill >= slotsB[slotBIndex].Fill)
                    {
                        sourceStorageEntity = conn.StorageA;
                        sourceSlotIndex = slotAIndex;
                        targetStorageEntity = conn.StorageB;
                        targetSlotIndex = slotBIndex;
                    }
                    else
                    {
                        sourceStorageEntity = conn.StorageB;
                        sourceSlotIndex = slotBIndex;
                        targetStorageEntity = conn.StorageA;
                        targetSlotIndex = slotAIndex;
                    }
                }

                // Calculate max amount to transfer
                var maxTransferAmount = 0;
                var sourceSlots = _slotLookup[sourceStorageEntity];
                var targetSlots = _slotLookup[targetStorageEntity];
                if (conn.Mode == ConnectionMode.TwoWays)
                {
                    StorageSlot sourceSlot = sourceSlots[sourceSlotIndex];
                    StorageSlot targetSlot = targetSlots[targetSlotIndex];
                    var averageFill = (sourceSlot.Fill + targetSlot.Fill) * 0.5f;
                    var sourceSurplusAmount = sourceSlot.AvailableToDispatch - (int)(sourceSlot.Capacity * averageFill);
                    var targetNeedAmount = (int)(targetSlot.Capacity * averageFill) - (targetSlot.CurrentAmount + targetSlot.ReservedIncoming);
                    maxTransferAmount = (sourceSurplusAmount + targetNeedAmount) / 2;
                    // Debug.Log($"{sourceSlot.Fill} {targetSlot.Fill} => avg {averageFill}");
                    // Debug.Log($"source: {sourceSlot.AvailableToDispatch} - {(int)(sourceSlot.Capacity * averageFill)} = {sourceSurplusAmount}");
                    // Debug.Log($"target: {(int)(targetSlot.Capacity * averageFill)} - {targetSlot.CurrentAmount + targetSlot.ReservedIncoming} {targetNeedAmount}");
                    // Debug.Log($"max transfer: {sourceSurplusAmount} + {targetNeedAmount} => {maxTransferAmount}");
                }
                else
                {
                    int available = sourceSlots[sourceSlotIndex].AvailableToDispatch;
                    int capacity = targetSlots[targetSlotIndex].AvailableCapacity;
                    maxTransferAmount = math.min(available, capacity);
                }


                // One job per effective source per run.
                if (assignedSources.Contains(sourceStorageEntity)) continue;


                if (!_storageLookup.HasComponent(sourceStorageEntity)) continue;


                var sourceStorage = _storageLookup[sourceStorageEntity];

                // Walk holders in ring-expansion order, skipping already-assigned ones.
                var cursor = new GridSearchCursor();
                cursor.Init(in grid, sourceStorage.WorldPosition);
                Entity holderEntity = Entity.Null;
                while (cursor.MoveNext(out HolderSpatilEntry holderEntry))
                {
                    if (!assignedHolders.Contains(holderEntry.Entity))
                    {
                        holderEntity = holderEntry.Entity;
                        break;
                    }
                }

                if (holderEntity == Entity.Null) continue; // no free holder in range

                // Cap batch at the holder's carry capacity.
                HolderComponent holder = _holderLookup[holderEntity];
                var transferAmount = math.min(maxTransferAmount, holder.CarryCapacity);
                if (transferAmount <= 0)
                {
                    continue;
                }

                if (!StorageSlotUtils.TryReserveBoth(ref sourceSlots, ref targetSlots, conn.Resource, transferAmount))
                {
                    continue;
                }

                assignedSources.Add(sourceStorageEntity);
                assignedHolders.Add(holderEntity);

                holder.State = HolderState.MovingToSource;
                holder.TargetPos = sourceStorage.WorldPosition;
                holder.CarriedType = conn.Resource;
                holder.AssignedJob = sourceStorageEntity;
                _holderLookup[holderEntity] = holder;

                availableHolder--;

                ecb.SetComponent(holderEntity, new DeliveryJobComponent
                {
                    SourceStorage = sourceStorageEntity,
                    DestStorage = targetStorageEntity,
                    Resource = conn.Resource,
                    ReservedAmount = transferAmount,
                });
                ecb.SetComponentEnabled<DeliveryJobComponent>(holderEntity, true);

                if (availableHolder == 0)
                {
                    break;
                }
            }

            sorted.Dispose();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            assignedSources.Dispose();
            assignedHolders.Dispose();
        }

        private struct ConnSort
        {
            public Entity Connection;
            public byte Priority;
        }

        private struct PriorityDescComparer : IComparer<ConnSort>
        {
            public readonly int Compare(ConnSort x, ConnSort y)
                => y.Priority.CompareTo(x.Priority);
        }
    }
}