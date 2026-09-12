using Unity.Burst;
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
    ///
    /// What releasing actually costs lives in <see cref="OrderReleaseUtils"/>, because being shot ends an
    /// order too and owes the world exactly the same things (§14 step 13).
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(InteractionSystem))]
    [BurstCompile]
    public partial struct OrderCompletionSystem : ISystem
    {
        [BurstCompile]
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
                OrderReleaseUtils.Unwind(entities, agent);
                entities.SetComponentEnabled<AssignedOrder>(agent, false);
            }

            finished.Dispose();
        }
    }
}
