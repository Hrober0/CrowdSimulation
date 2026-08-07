using GridNav;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Takes agents in and out of buildings (design §6, §13.3 #18).
    ///
    /// Going in disables <see cref="AgentMove"/>, <see cref="PathFollow"/> and <see cref="ViewVisible"/>. That
    /// is the whole of it, and every consequence §6 claims falls out of those three flags: no path, no
    /// steering, no RVO neighbour query, no spatial hash entry and no GameObject. An agent resting in a hut
    /// costs nothing per frame because there is no per-frame loop left that can see it - not because anything
    /// was tuned.
    ///
    /// Single-threaded and after integration, because each transition writes two entities at once - the
    /// agent's flags and the building's occupancy - which is the shape §13.2 invariant 4 keeps off the job
    /// system.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup))]
    [UpdateAfter(typeof(AgentIntegrateSystem))]
    public partial struct InteriorTransitionSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton(
                new InteriorTransitionQueue(Allocator.Persistent),
                "InteriorTransitionQueue"
            );
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out InteriorTransitionQueue queue))
            {
                queue.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            InteriorTransitionQueue queue = SystemAPI.GetSingleton<InteriorTransitionQueue>();

            while (queue.TryDequeue(out InteriorTransition transition))
            {
                if (transition.Kind == InteriorTransitionKind.Enter)
                {
                    Enter(ref state, transition);
                }
                else
                {
                    Exit(ref state, transition);
                }
            }
        }

        private static void Enter(ref SystemState state, in InteriorTransition transition)
        {
            EntityManager entities = state.EntityManager;
            if (!IsLive(entities, transition))
            {
                return;
            }

            Interior interior = entities.GetComponentData<Interior>(transition.Building);
            interior.Occupied++;
            entities.SetComponentData(transition.Building, interior);

            // Both velocities: nothing updates PrefVelocity while the agent is off the map, so a leftover
            // value would be honoured the moment it steps back out.
            AgentMove move = entities.GetComponentData<AgentMove>(transition.Agent);
            move.Velocity = float2.zero;
            move.PrefVelocity = float2.zero;
            entities.SetComponentData(transition.Agent, move);

            entities.SetComponentData(transition.Agent, new InsideBuilding { Building = transition.Building });
            entities.SetComponentEnabled<InsideBuilding>(transition.Agent, true);

            entities.SetComponentEnabled<AgentMove>(transition.Agent, false);
            entities.SetComponentEnabled<PathFollow>(transition.Agent, false);

            SetVisible(entities, transition.Agent, false);
        }

        private static void Exit(ref SystemState state, in InteriorTransition transition)
        {
            EntityManager entities = state.EntityManager;
            if (!IsLive(entities, transition))
            {
                return;
            }

            Interior interior = entities.GetComponentData<Interior>(transition.Building);
            interior.Occupied = math.max(interior.Occupied - 1, 0);
            interior.Claimed = math.max(interior.Claimed - 1, 0);
            entities.SetComponentData(transition.Building, interior);

            // Put back down on the doorstep. The agent kept its position while inside, but the building may
            // have been re-placed or the claim released from somewhere else in the meantime, and the entrance
            // cell is the one place it is certainly allowed to stand.
            AgentMove move = entities.GetComponentData<AgentMove>(transition.Agent);
            move.Position = GridCoords.CellCenter(transition.Cell);
            move.Velocity = float2.zero;
            move.PrefVelocity = float2.zero;
            entities.SetComponentData(transition.Agent, move);

            entities.SetComponentEnabled<InsideBuilding>(transition.Agent, false);
            entities.SetComponentEnabled<InteriorClaim>(transition.Agent, false);

            entities.SetComponentEnabled<AgentMove>(transition.Agent, true);

            SetVisible(entities, transition.Agent, true);

            // PathFollow stays off: the agent is standing in a doorway with nothing to do until its next
            // GoTo step turns it back on.
        }

        /// <summary>
        /// Optional, because <see cref="ViewVisible"/> is the seam to a view layer that need not exist - a
        /// headless run has no GameObjects to release and should not have to carry a flag to say so.
        /// </summary>
        private static void SetVisible(in EntityManager entities, Entity agent, bool visible)
        {
            if (entities.HasComponent<ViewVisible>(agent))
            {
                entities.SetComponentEnabled<ViewVisible>(agent, visible);
            }
        }

        /// <summary>
        /// Whether both ends of the transition still exist and are what they were. A building can be
        /// demolished while an agent walks to it, and a queued transition is a frame older than the world it
        /// is applied to.
        /// </summary>
        private static bool IsLive(in EntityManager entities, in InteriorTransition transition) =>
            entities.Exists(transition.Agent)
            && entities.Exists(transition.Building)
            && entities.HasComponent<Interior>(transition.Building)
            && entities.HasComponent<AgentMove>(transition.Agent);
    }
}
