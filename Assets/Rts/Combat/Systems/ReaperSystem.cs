using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Takes away what has run out of health (design §14 step 13).
    ///
    /// Split from <see cref="DamageApplySystem"/> so that the one writer of <see cref="Health"/> writes only
    /// health. What made the split worth having is not tidiness: **a dead agent owes the world things**. A
    /// hauler shot halfway to a warehouse is holding a reservation on the shelf it was going to, room held at
    /// the destination, and possibly a bench it claimed and never sat on. Destroying the entity where the
    /// damage lands would strand all three, and the leak is invisible - a warehouse that slowly stops
    /// accepting deliveries for no reason anybody can see.
    ///
    /// So this unwinds first, through the same <see cref="OrderReleaseUtils"/> an ordinary finished task goes
    /// through, and destroys afterwards. Buildings come through here too and unwind to nothing, which is
    /// correct: their cells come back from the cleanup buffers on the next grid phase, exactly as a demolished
    /// one's do.
    ///
    /// Runs at the end of the agent phase so that everything which reads a dying thing this frame still sees
    /// it - and nothing has to be taught the difference between "dead" and "gone" in between, because
    /// <c>AttackSystem</c> already refuses to shoot what is not <see cref="Health.IsAlive"/>.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(OrderCompletionSystem))]
    public partial struct ReaperSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // Collected first: destroying is a structural change, and doing it mid-query would invalidate
            // the iteration that found the bodies.
            var dead = new NativeList<Entity>(8, Allocator.Temp);

            foreach ((RefRO<Health> health, Entity entity)
                     in SystemAPI.Query<RefRO<Health>>().WithEntityAccess())
            {
                if (!health.ValueRO.IsAlive)
                {
                    dead.Add(entity);
                }
            }

            EntityManager entities = state.EntityManager;

            foreach (Entity entity in dead)
            {
                OrderReleaseUtils.Unwind(entities, entity);
                entities.DestroyEntity(entity);
            }

            dead.Dispose();
        }
    }
}
