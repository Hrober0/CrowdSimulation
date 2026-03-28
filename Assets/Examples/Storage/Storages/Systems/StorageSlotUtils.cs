using Unity.Entities;
using UnityEngine;

namespace Examples.Storage
{
    public static class StorageSlotUtils
    {
        public static bool TryGetSlotIndex(
            DynamicBuffer<StorageSlot> buffer,
            ResourceType resource,
            out int index)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].Resource != resource) continue;
                index = i;
                return true;
            }
            index = -1;
            return false;
        }
 
        // ── Source reservation ────────────────────────────────────────────────
 
        /// <summary>
        /// Locks <paramref name="amount"/> units at the source.
        /// Returns false if slot missing or insufficient AvailableToDispatch.
        /// </summary>
        public static bool TryReserveOutgoing(
            ref DynamicBuffer<StorageSlot> buffer,
            ResourceType resource, int amount)
        {
            if (!TryGetSlotIndex(buffer, resource, out int i)) return false;
            var slot = buffer[i];
            if (slot.AvailableToDispatch < amount) return false;
            slot.ReservedOutgoing += amount;
            buffer[i] = slot;
            return true;
        }
 
        public static void ReleaseOutgoing(
            ref DynamicBuffer<StorageSlot> buffer,
            ResourceType resource, int amount)
        {
            if (!TryGetSlotIndex(buffer, resource, out int i)) return;
            var slot = buffer[i];
            slot.ReservedOutgoing = Mathf.Max(0, slot.ReservedOutgoing - amount);
            buffer[i] = slot;
        }
 
        // ── Destination reservation ───────────────────────────────────────────
 
        /// <summary>
        /// Locks incoming capacity at the destination.
        /// Returns false if slot missing or insufficient AvailableCapacity.
        /// </summary>
        public static bool TryReserveIncoming(
            ref DynamicBuffer<StorageSlot> buffer,
            ResourceType resource, int amount)
        {
            if (!TryGetSlotIndex(buffer, resource, out int i)) return false;
            var slot = buffer[i];
            if (slot.AvailableCapacity < amount) return false;
            slot.ReservedIncoming += amount;
            buffer[i] = slot;
            return true;
        }
 
        public static void ReleaseIncoming(
            ref DynamicBuffer<StorageSlot> buffer,
            ResourceType resource, int amount)
        {
            if (!TryGetSlotIndex(buffer, resource, out int i)) return;
            var slot = buffer[i];
            slot.ReservedIncoming = Mathf.Max(0, slot.ReservedIncoming - amount);
            buffer[i] = slot;
        }
 
        // ── Combined — used before creating a job ─────────────────────────────
 
        /// <summary>
        /// Reserves amount on both sides atomically.
        /// Rolls back outgoing if incoming fails.
        /// </summary>
        public static bool TryReserveBoth(
            ref DynamicBuffer<StorageSlot> srcBuffer,
            ref DynamicBuffer<StorageSlot> dstBuffer,
            ResourceType resource, int amount)
        {
            if (!TryReserveOutgoing(ref srcBuffer, resource, amount))
                return false;
 
            if (!TryReserveIncoming(ref dstBuffer, resource, amount))
            {
                ReleaseOutgoing(ref srcBuffer, resource, amount);
                return false;
            }
 
            return true;
        }
 
        // ── Transfer helpers — called from ResourceTransferSystem ─────────────
 
        /// <summary>
        /// Subtracts amount from source CurrentAmount, releases outgoing reservation.
        /// Corrects destination incoming reservation for any shortfall.
        /// Returns actual units taken.
        /// </summary>
        public static int ExecutePickup(
            ref DynamicBuffer<StorageSlot> srcBuffer,
            ref DynamicBuffer<StorageSlot> dstBuffer,
            ResourceType resource, int reservedAmount)
        {
            if (!TryGetSlotIndex(srcBuffer, resource, out int si)) return 0;
 
            var src    = srcBuffer[si];
            int actual = Mathf.Min(reservedAmount, src.CurrentAmount);
 
            src.CurrentAmount    -= actual;
            src.ReservedOutgoing  = Mathf.Max(0, src.ReservedOutgoing - reservedAmount);
            srcBuffer[si] = src;
 
            int shortfall = reservedAmount - actual;
            if (shortfall > 0)
                ReleaseIncoming(ref dstBuffer, resource, shortfall);
 
            return actual;
        }
 
        /// <summary>
        /// Adds amount to destination CurrentAmount, releases incoming reservation.
        /// Returns actual units deposited.
        /// </summary>
        public static int ExecuteDelivery(
            ref DynamicBuffer<StorageSlot> dstBuffer,
            ResourceType resource, int amount, int reservedAmount)
        {
            if (!TryGetSlotIndex(dstBuffer, resource, out int di)) return 0;
 
            var dst       = dstBuffer[di];
            int deposited = Mathf.Min(amount, dst.Capacity - dst.CurrentAmount);
 
            dst.CurrentAmount    += deposited;
            dst.ReservedIncoming  = Mathf.Max(0, dst.ReservedIncoming - reservedAmount);
            dstBuffer[di] = dst;
 
            return deposited;
        }
 
        // ── Cancel ────────────────────────────────────────────────────────────

        /// <summary>
        /// Cleans up all storage reservations when a holder is killed mid-delivery.
        ///
        /// Not yet picked up (currentLoad == 0):
        ///   — releases src.ReservedOutgoing and dst.ReservedIncoming.
        ///
        /// Already picked up (currentLoad > 0):
        ///   — returns the carried goods back to src.CurrentAmount,
        ///   — releases dst.ReservedIncoming (clamped, safe if already partial).
        /// </summary>
        public static void CancelJob(
            ref DynamicBuffer<StorageSlot> srcBuffer,
            ref DynamicBuffer<StorageSlot> dstBuffer,
            in  DeliveryJobComponent       job,
            int                            currentLoad)
        {
            if (currentLoad > 0)
            {
                // Goods already left the source — return them.
                if (TryGetSlotIndex(srcBuffer, job.Resource, out int si))
                {
                    var slot = srcBuffer[si];
                    slot.CurrentAmount = Mathf.Min(slot.Capacity, slot.CurrentAmount + currentLoad);
                    srcBuffer[si] = slot;
                }
            }
            else
            {
                // Holder never reached the source — release the outgoing lock.
                ReleaseOutgoing(ref srcBuffer, job.Resource, job.ReservedAmount);
            }

            ReleaseIncoming(ref dstBuffer, job.Resource, job.ReservedAmount);
        }
    }
}