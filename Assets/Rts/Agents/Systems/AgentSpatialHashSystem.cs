using CustomNativeCollections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Who is near whom, rebuilt every frame (design §13.3 #12). Read by avoidance, target finding, idle
    /// parking and bridges.
    ///
    /// **An agent is filed under the cell its centre is in, once.** Every reader asks "whose centre is within
    /// this distance of here", and a box around that circle already contains every such centre - so filing a
    /// body under each cell it overlaps bought nothing but copies. It cost plenty: up to four nodes per agent,
    /// four times the candidates for every query, and avoidance filling its twelve neighbour slots with the
    /// same neighbour twice.
    /// </summary>
    public struct AgentSpatialHash : IComponentData
    {
        public NativeSpatialHash<AgentMove> Hash;
    }

    [UpdateInGroup(typeof(RtsAgentGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct AgentSpatialHashSystem : ISystem
    {
        private const int INITIAL_CAPACITY = 4096;

        /// <summary>
        /// Two cells, not one. Avoidance asks for a box about twelve cells across and a gun for one sixteen
        /// across, so at one cell a query probed 144-289 mostly empty buckets; at two it probes a quarter of
        /// that for a slightly longer list per bucket (RtsPerformance.md §4).
        /// </summary>
        private const float CELL_SIZE = 2f;

        private ComponentLookup<OnBridge> _onBridge;

        public void OnCreate(ref SystemState state)
        {
            _onBridge = state.GetComponentLookup<OnBridge>(isReadOnly: true);

            state.EntityManager.CreateSingleton(
                new AgentSpatialHash
                {
                    Hash = new NativeSpatialHash<AgentMove>(INITIAL_CAPACITY, CELL_SIZE, Allocator.Persistent),
                },
                "AgentSpatialHash"
            );
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out AgentSpatialHash spatial) && spatial.Hash.IsCreated)
            {
                spatial.Hash.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            NativeSpatialHash<AgentMove> hash = SystemAPI.GetSingletonRW<AgentSpatialHash>().ValueRW.Hash;
            hash.Clear();

            _onBridge.Update(ref state);

            foreach ((RefRO<AgentMove> agent, Entity entity)
                     in SystemAPI.Query<RefRO<AgentMove>>().WithEntityAccess())
            {
                // An agent on a bridge is *over* the map rather than on it. It keeps its view and its position,
                // but it is nobody's neighbour: leaving it in would let it shoulder the traffic passing under
                // the deck, which is the one thing a bridge exists to let happen.
                //
                // Asked per agent rather than filtered on, because a query keyed on OnBridge would silently
                // drop every agent whose archetype does not carry it - a headless test, an agent kind added
                // later - and a missing agent in the spatial hash is invisible until two of them walk through
                // each other.
                if (_onBridge.HasComponent(entity) && _onBridge.IsComponentEnabled(entity))
                {
                    continue;
                }

                hash.AddPoint(agent.ValueRO.Position, agent.ValueRO);
            }
        }
    }
}
