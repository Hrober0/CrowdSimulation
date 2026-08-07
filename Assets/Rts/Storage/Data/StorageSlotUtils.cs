using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Reservations and transfers on <see cref="StorageSlot"/> buffers, ported from the delivery example
    /// (design §1: the reservation semantics were the part of it worth keeping).
    ///
    /// The whole point is that a promise is recorded the moment it is made, not when it is kept. Five loaves
    /// of bread can only be promised five times, so haulers cannot over-dispatch to a source and the
    /// thundering herd of §8 has nothing to form around - the order count is bounded by real inventory
    /// without anything counting orders.
    ///
    /// Every function here writes reservations or amounts and nothing else, which is what keeps §13.2
    /// invariant 2 checkable: reservations are written by the assign and release systems, amounts only by
    /// <see cref="InteractionSystem"/>.
    /// </summary>
    public static class StorageSlotUtils
    {
        public static bool TryGetSlotIndex(in DynamicBuffer<StorageSlot> slots, ItemId item, out int index)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Item != item)
                {
                    continue;
                }

                index = i;
                return true;
            }

            index = -1;
            return false;
        }

        /// <summary>Promises <paramref name="amount"/> units away from this slot.</summary>
        public static bool TryReserveOut(ref DynamicBuffer<StorageSlot> slots, ItemId item, int amount)
        {
            if (amount <= 0 || !TryGetSlotIndex(slots, item, out int index))
            {
                return false;
            }

            StorageSlot slot = slots[index];
            if (slot.AvailableOut < amount)
            {
                return false;
            }

            slot.ReservedOut += amount;
            slots[index] = slot;
            return true;
        }

        public static void ReleaseOut(ref DynamicBuffer<StorageSlot> slots, ItemId item, int amount)
        {
            if (amount <= 0 || !TryGetSlotIndex(slots, item, out int index))
            {
                return;
            }

            StorageSlot slot = slots[index];
            slot.ReservedOut = math.max(0, slot.ReservedOut - amount);
            slots[index] = slot;
        }

        /// <summary>
        /// Holds room for <paramref name="amount"/> units arriving. Against <see cref="StorageSlot.FreeCapacity"/>
        /// rather than <see cref="StorageSlot.WantedIn"/>, because the threshold decides whether to *ask*;
        /// once a hauler is on the way the only question left is whether the goods will physically fit.
        /// </summary>
        public static bool TryReserveIn(ref DynamicBuffer<StorageSlot> slots, ItemId item, int amount)
        {
            if (amount <= 0 || !TryGetSlotIndex(slots, item, out int index))
            {
                return false;
            }

            StorageSlot slot = slots[index];
            if (slot.FreeCapacity < amount)
            {
                return false;
            }

            slot.ReservedIn += amount;
            slots[index] = slot;
            return true;
        }

        public static void ReleaseIn(ref DynamicBuffer<StorageSlot> slots, ItemId item, int amount)
        {
            if (amount <= 0 || !TryGetSlotIndex(slots, item, out int index))
            {
                return;
            }

            StorageSlot slot = slots[index];
            slot.ReservedIn = math.max(0, slot.ReservedIn - amount);
            slots[index] = slot;
        }

        /// <summary>
        /// Both ends or neither. A half-reserved haul would leave stock promised to nobody, and the rollback
        /// is what makes claiming an order safe to attempt and abandon.
        /// </summary>
        public static bool TryReserveBoth(
            ref DynamicBuffer<StorageSlot> source,
            ref DynamicBuffer<StorageSlot> target,
            ItemId item,
            int amount)
        {
            if (!TryReserveOut(ref source, item, amount))
            {
                return false;
            }

            if (!TryReserveIn(ref target, item, amount))
            {
                ReleaseOut(ref source, item, amount);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Takes what is actually there, which may be less than was promised - something else can have eaten
        /// it while the hauler walked. The shortfall gives back the room held at the destination, so a
        /// half-full trip does not leave a warehouse pretending to be fuller than it is.
        /// </summary>
        /// <returns>Units actually picked up.</returns>
        public static int ExecutePickup(
            ref DynamicBuffer<StorageSlot> source,
            ref DynamicBuffer<StorageSlot> target,
            ItemId item,
            int reserved)
        {
            if (!TryGetSlotIndex(source, item, out int index))
            {
                ReleaseIn(ref target, item, reserved);
                return 0;
            }

            StorageSlot slot = source[index];
            int taken = math.min(reserved, slot.Amount);

            slot.Amount -= taken;
            slot.ReservedOut = math.max(0, slot.ReservedOut - reserved);
            source[index] = slot;

            int shortfall = reserved - taken;
            if (shortfall > 0)
            {
                ReleaseIn(ref target, item, shortfall);
            }

            return taken;
        }

        /// <summary>
        /// Puts down what was carried. It always fits: the room was held by <see cref="ReservedIn"/> from
        /// the moment the order was claimed, and the pickup shortfall path gave back any part of it that
        /// turned out not to be needed.
        /// </summary>
        /// <returns>Units actually deposited.</returns>
        public static int ExecuteDeposit(ref DynamicBuffer<StorageSlot> target, ItemId item, int carried)
        {
            if (carried <= 0 || !TryGetSlotIndex(target, item, out int index))
            {
                return 0;
            }

            StorageSlot slot = target[index];
            int deposited = math.min(carried, slot.Capacity - slot.Amount);

            slot.Amount += deposited;
            slot.ReservedIn = math.max(0, slot.ReservedIn - carried);
            target[index] = slot;

            return deposited;
        }

        /// <summary>
        /// Unwinds a haul that will not happen - the hauler died, the building was demolished, the watchdog
        /// gave up on it. Goods already in hand go back where they came from; goods still on the shelf are
        /// simply un-promised.
        /// </summary>
        public static void CancelHaul(
            ref DynamicBuffer<StorageSlot> source,
            ref DynamicBuffer<StorageSlot> target,
            ItemId item,
            int reserved,
            int carried)
        {
            if (carried > 0)
            {
                if (TryGetSlotIndex(source, item, out int index))
                {
                    StorageSlot slot = source[index];
                    slot.Amount = math.min(slot.Capacity, slot.Amount + carried);
                    source[index] = slot;
                }
            }
            else
            {
                ReleaseOut(ref source, item, reserved);
            }

            ReleaseIn(ref target, item, reserved);
        }
    }
}
