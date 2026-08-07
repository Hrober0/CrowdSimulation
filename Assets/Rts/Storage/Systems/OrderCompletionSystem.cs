using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Lets go of an order once its task has run out of steps (design §13.3 #20).
    ///
    /// Completion is not a separate signal: a hauler with nothing left to do has, by definition, either
    /// finished or been cut short, and both end the same way. Whatever the haul still holds at that point -
    /// stock promised but never collected, goods carried to a building that was demolished - is unwound
    /// here, so the release path is one piece of code rather than one per way of failing.
    ///
    /// The agent then simply has no order, which is exactly what <see cref="IdleAssignSystem"/> and
    /// <see cref="OrderAssignSystem"/> are both looking for. There is no "finished" state to leave.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(InteractionSystem))]
    public partial struct OrderCompletionSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // Collected first: disabling AssignedOrder is what the query filters on, and changing that under
            // the iteration is asking the enumerator to skip agents at random.
            var finished = new NativeList<Entity>(16, Allocator.Temp);

            foreach ((DynamicBuffer<TaskStep> steps, Entity agent)
                     in SystemAPI.Query<DynamicBuffer<TaskStep>>().WithAll<AssignedOrder>().WithEntityAccess())
            {
                if (steps.IsEmpty)
                {
                    finished.Add(agent);
                }
            }

            EntityManager entities = state.EntityManager;
            foreach (Entity agent in finished)
            {
                Release(entities, agent);
                entities.SetComponentEnabled<AssignedOrder>(agent, false);
            }

            finished.Dispose();
        }

        /// <summary>
        /// Gives back whatever the haul still holds. <see cref="AssignedOrder.Amount"/> is the outstanding
        /// reservation and is zeroed by a successful deposit, so "nothing reserved and nothing carried" is
        /// the finished case and everything else needs unwinding.
        ///
        /// Goods in hand go back on the source's shelf rather than evaporating - the alternative is an
        /// economy that quietly loses stock whenever anything goes wrong, which is very hard to notice and
        /// impossible to reconstruct afterwards.
        /// </summary>
        private static void Release(in EntityManager entities, Entity agent)
        {
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
    }
}
