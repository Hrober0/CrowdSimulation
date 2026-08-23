using CustomNativeCollections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Who is near whom, rebuilt every frame (design §13.3 #12). Avoidance is the only consumer for now;
    /// order assignment and bridges also read it.
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
        private const float CELL_SIZE = 1f;

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

                AgentMove move = agent.ValueRO;
                float radius = move.Radius;
                hash.AddAABB(move.Position - radius, move.Position + radius, move);
            }
        }
    }
}
