using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Takes a resource node away once there is nothing left in it and nobody is still coming for it
    /// (design §14 step 9).
    ///
    /// Destroying the entity is the whole of it. <see cref="CellObjectRegistrationSystem"/> refunds the cost
    /// and erases the map entry on the next grid phase, driven by the cleanup component that outlives the
    /// node - the same single path a demolished building already takes, so an exhausted seam cannot leak the
    /// cost it was contributing.
    ///
    /// **<see cref="StorageSlot.ReservedOut"/> is the half that matters.** A seam with nothing left may still
    /// have a miner most of the way to it for the last five ore, and a reservation is precisely the record of
    /// that promise. Waiting for it to clear means waiting until everyone who was promised something has
    /// either collected it or given up, which the pickup-shortfall path and the watchdog both already
    /// guarantee happens.
    ///
    /// Runs in the economy group rather than per frame because nothing is waiting on the answer: a node that
    /// stays on the map for another tenth of a second holds an empty slot nobody can take anything from.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup))]
    [BurstCompile]
    public partial struct ResourceNodeDepletionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate(SystemAPI.QueryBuilder().WithAll<ResourceNode, StorageSlot>().Build());
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((DynamicBuffer<StorageSlot> slots, Entity node)
                     in SystemAPI.Query<DynamicBuffer<StorageSlot>>()
                                 .WithAll<ResourceNode>()
                                 .WithEntityAccess())
            {
                if (IsSpent(slots))
                {
                    commands.DestroyEntity(node);
                }
            }

            commands.Playback(state.EntityManager);
            commands.Dispose();
        }

        /// <summary>
        /// Nothing left in any slot, and nothing promised out of any of them. Every slot has to agree: a seam
        /// of two minerals is done when both are done, and taking it away while one still held stock would
        /// destroy goods that exist.
        /// </summary>
        private static bool IsSpent(in DynamicBuffer<StorageSlot> slots)
        {
            foreach (StorageSlot slot in slots)
            {
                if (slot.Amount > 0 || slot.ReservedOut > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
