using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Giving back whatever an agent was still holding (design §8, §14 step 13).
    ///
    /// Extracted from <see cref="OrderCompletionSystem"/> when a second caller appeared: an agent that runs
    /// out of steps has finished, and an agent that is shot has not, but what each of them owes the world is
    /// identical - a reservation on a shelf, room held at a destination, a bench claimed and never sat on.
    /// One function, for the same reason <see cref="RecipeUtils"/> is one: "the order ended" and "the order
    /// was released" must not be able to disagree about what that costs.
    ///
    /// Every step guards on the component being there, because the second caller also hands it buildings.
    /// </summary>
    public static class OrderReleaseUtils
    {
        /// <summary>
        /// Gives back whatever the haul still holds. <see cref="AssignedOrder.Amount"/> is the outstanding
        /// reservation and is zeroed by a successful deposit, so "nothing reserved and nothing carried" is
        /// the finished case and everything else needs unwinding.
        ///
        /// Goods in hand go back on the source's shelf rather than evaporating - the alternative is an
        /// economy that quietly loses stock whenever anything goes wrong, which is very hard to notice and
        /// impossible to reconstruct afterwards.
        /// </summary>
        public static void Unwind(in EntityManager entities, Entity agent)
        {
            if (!entities.Exists(agent))
            {
                return;
            }

            ReleaseInteriorClaim(entities, agent);

            if (!entities.HasComponent<AssignedOrder>(agent)
                || !entities.IsComponentEnabled<AssignedOrder>(agent)
                || !entities.HasComponent<Carry>(agent))
            {
                return;
            }

            var haul = entities.GetComponentData<AssignedOrder>(agent);
            Carry carry = entities.GetComponentData<Carry>(agent);

            if (haul.Kind != OrderKind.Haul || (haul.Amount <= 0 && carry.Amount <= 0))
            {
                return;
            }

            if (entities.HasBuffer<StorageSlot>(haul.Source) && entities.HasBuffer<StorageSlot>(haul.Target))
            {
                DynamicBuffer<StorageSlot> source = entities.GetBuffer<StorageSlot>(haul.Source);
                DynamicBuffer<StorageSlot> target = entities.GetBuffer<StorageSlot>(haul.Target);
                StorageSlotUtils.CancelHaul(ref source, ref target, haul.Item, haul.Amount, carry.Amount);
            }

            carry.Amount = 0;
            carry.Item = ItemId.None;
            entities.SetComponentData(agent, carry);

            haul.Amount = 0;
            entities.SetComponentData(agent, haul);
        }

        /// <summary>
        /// A work slot claimed but never taken up - the task was cut short before the worker got through the
        /// door - has to go back, or the building loses a bench permanently.
        ///
        /// A claim that *was* taken up is already gone: leaving the building clears it, and the agent has to
        /// have left for its steps to have run out. So a claim still standing here is by definition one that
        /// was never used.
        /// </summary>
        private static void ReleaseInteriorClaim(in EntityManager entities, Entity agent)
        {
            if (!entities.HasComponent<InteriorClaim>(agent)
                || !entities.IsComponentEnabled<InteriorClaim>(agent))
            {
                return;
            }

            Entity building = entities.GetComponentData<InteriorClaim>(agent).Building;
            if (entities.Exists(building) && entities.HasComponent<Interior>(building))
            {
                Interior interior = entities.GetComponentData<Interior>(building);
                interior.Claimed = math.max(interior.Claimed - 1, 0);
                entities.SetComponentData(building, interior);
            }

            entities.SetComponentEnabled<InteriorClaim>(agent, false);
        }
    }
}
