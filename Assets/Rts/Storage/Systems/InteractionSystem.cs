using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Settles finished interactions - goods actually changing hands (design §13.3 #19).
    ///
    /// This is the **only** writer of <see cref="StorageSlot.Amount"/> (§13.2 invariant 2). Reservations are
    /// written by the assign and release systems, amounts only here: different fields, different phases, so
    /// the race that would corrupt the reservation invariant cannot occur rather than being unlikely.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(InteriorTransitionSystem))]
    public partial struct InteractionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton(new InteractionQueue(Allocator.Persistent), "InteractionQueue");
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out InteractionQueue queue))
            {
                queue.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            InteractionQueue queue = SystemAPI.GetSingleton<InteractionQueue>();

            while (queue.TryDequeue(out InteractionEvent interaction))
            {
                switch (interaction.Kind)
                {
                    case InteractionKind.Pickup:
                        Pickup(ref state, interaction);
                        break;

                    case InteractionKind.Deposit:
                        Deposit(ref state, interaction);
                        break;
                }
            }
        }

        private static void Pickup(ref SystemState state, in InteractionEvent interaction)
        {
            EntityManager entities = state.EntityManager;
            if (!TryGetHaul(entities, interaction.Agent, out AssignedOrder haul))
            {
                return;
            }

            if (!entities.HasBuffer<StorageSlot>(haul.Source) || !entities.HasBuffer<StorageSlot>(haul.Target))
            {
                return;
            }

            DynamicBuffer<StorageSlot> source = entities.GetBuffer<StorageSlot>(haul.Source);
            DynamicBuffer<StorageSlot> target = entities.GetBuffer<StorageSlot>(haul.Target);

            // Takes what is there rather than what was promised: something can have eaten the stock while
            // the hauler walked, and the shortfall gives the destination's held room back (§8).
            int taken = StorageSlotUtils.ExecutePickup(ref source, ref target, haul.Item, haul.Amount);

            Carry carry = entities.GetComponentData<Carry>(interaction.Agent);
            carry.Item = haul.Item;
            carry.Amount = math.min(taken, carry.Capacity);
            entities.SetComponentData(interaction.Agent, carry);

            haul.Amount = carry.Amount;
            entities.SetComponentData(interaction.Agent, haul);

            // Nothing to carry means nothing to deliver. Dropping the rest of the task here is what keeps a
            // hauler from walking the whole way to hand over an empty crate.
            if (carry.Amount <= 0)
            {
                entities.GetBuffer<TaskStep>(interaction.Agent).Clear();
            }
        }

        private static void Deposit(ref SystemState state, in InteractionEvent interaction)
        {
            EntityManager entities = state.EntityManager;
            if (!TryGetHaul(entities, interaction.Agent, out AssignedOrder haul))
            {
                return;
            }

            Carry carry = entities.GetComponentData<Carry>(interaction.Agent);
            if (carry.Amount <= 0)
            {
                return;
            }

            if (entities.HasBuffer<StorageSlot>(haul.Target))
            {
                DynamicBuffer<StorageSlot> target = entities.GetBuffer<StorageSlot>(haul.Target);
                StorageSlotUtils.ExecuteDeposit(ref target, carry.Item, carry.Amount);
            }

            carry.Amount = 0;
            carry.Item = ItemId.None;
            entities.SetComponentData(interaction.Agent, carry);

            // Nothing outstanding any more: the deposit released the destination's held room as it went, and
            // this is what tells OrderCompletionSystem there is nothing left to unwind.
            haul.Amount = 0;
            entities.SetComponentData(interaction.Agent, haul);
        }

        private static bool TryGetHaul(in EntityManager entities, Entity agent, out AssignedOrder haul)
        {
            haul = default;

            if (!entities.Exists(agent)
                || !entities.HasComponent<AssignedOrder>(agent)
                || !entities.IsComponentEnabled<AssignedOrder>(agent))
            {
                return false;
            }

            haul = entities.GetComponentData<AssignedOrder>(agent);
            return haul.Kind == OrderKind.Haul;
        }
    }
}
