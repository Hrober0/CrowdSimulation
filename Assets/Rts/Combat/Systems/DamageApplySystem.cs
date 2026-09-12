using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// The one writer of <see cref="Health"/> (design §13.5, §14 step 12).
    ///
    /// Everything that hurts anything enqueues; this subtracts. That is what lets a turret, and soon a
    /// soldier, be written as read-only searches that can be scheduled beside each other - and it is the same
    /// bargain <c>GridApplySystem</c> and <see cref="InteractionSystem"/> make for the grid and for slot
    /// amounts.
    ///
    /// It does not destroy what it kills. <see cref="ReaperSystem"/> does, at the end of the agent phase,
    /// because a dead hauler owes the world a reservation and a bench before it is allowed to disappear - and
    /// because "the only writer of Health" is a much easier promise to keep when writing health is all this
    /// does.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup))]
    public partial struct DamageApplySystem : ISystem
    {
        private ComponentLookup<Health> _health;

        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton(new DamageQueue(Allocator.Persistent), "DamageQueue");
            _health = state.GetComponentLookup<Health>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out DamageQueue queue))
            {
                queue.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            DamageQueue queue = SystemAPI.GetSingleton<DamageQueue>();
            if (queue.Count == 0)
            {
                return;
            }

            _health.Update(ref state);

            while (queue.TryDequeue(out DamageEvent damage))
            {
                if (!_health.HasComponent(damage.Target))
                {
                    continue;
                }

                Health health = _health[damage.Target];

                // Already dead, from an earlier event this tick. Two turrets that fired at the same raider
                // in the same tick both land here, and the second one must not take another twenty off a
                // corpse - the health write is immediate and the reaping is not, so the component is the
                // only honest record of what has already happened.
                if (!health.IsAlive)
                {
                    continue;
                }

                health.Current = math.max(0, health.Current - damage.Amount);
                _health[damage.Target] = health;
            }
        }
    }
}
