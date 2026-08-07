using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Whether a batch can be made, and making it. Two functions, used by both the system that *asks* for a
    /// worker and the one that runs the worker's shift - so "the crafter said it could work" and "the
    /// crafter worked" can never disagree about what the condition was.
    /// </summary>
    public static class RecipeUtils
    {
        /// <summary>
        /// Inputs on the shelf and room for the outputs. Room is checked against
        /// <see cref="StorageSlot.FreeCapacity"/>, so a batch is never started that would have nowhere to
        /// go - a crafter with a full output shelf stops asking for workers instead of destroying what it
        /// makes.
        /// </summary>
        public static bool CanCraft(
            in DynamicBuffer<StorageSlot> slots,
            in DynamicBuffer<RecipeInput> inputs,
            in DynamicBuffer<RecipeOutput> outputs)
        {
            foreach (RecipeInput input in inputs)
            {
                if (!StorageSlotUtils.TryGetSlotIndex(slots, input.Item, out int index)
                    || slots[index].Amount < input.Amount)
                {
                    return false;
                }
            }

            foreach (RecipeOutput output in outputs)
            {
                if (!StorageSlotUtils.TryGetSlotIndex(slots, output.Item, out int index)
                    || slots[index].FreeCapacity < output.Amount)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Turns one batch of inputs into one batch of outputs. Caller checks <see cref="CanCraft"/> first;
        /// this does not re-check, because the two calls happen at different moments and the interesting
        /// failure is the caller forgetting to ask, not this function being lenient.
        /// </summary>
        public static void Craft(
            ref DynamicBuffer<StorageSlot> slots,
            in DynamicBuffer<RecipeInput> inputs,
            in DynamicBuffer<RecipeOutput> outputs)
        {
            foreach (RecipeInput input in inputs)
            {
                if (!StorageSlotUtils.TryGetSlotIndex(slots, input.Item, out int index))
                {
                    continue;
                }

                StorageSlot slot = slots[index];
                slot.Amount -= input.Amount;
                slots[index] = slot;
            }

            foreach (RecipeOutput output in outputs)
            {
                if (!StorageSlotUtils.TryGetSlotIndex(slots, output.Item, out int index))
                {
                    continue;
                }

                StorageSlot slot = slots[index];
                slot.Amount += output.Amount;
                slots[index] = slot;
            }
        }
    }
}
