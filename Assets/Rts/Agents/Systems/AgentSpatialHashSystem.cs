using CustomNativeCollections;
using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Who is near whom, rebuilt every frame (design §13.3 #12). Avoidance is the only consumer for now;
    /// order assignment will want it later.
    /// </summary>
    public struct AgentSpatialHash : IComponentData
    {
        public NativeSpatialHash<AgentMove> Hash;
    }

    [UpdateInGroup(typeof(RtsAgentGroup), OrderFirst = true)]
    public partial struct AgentSpatialHashSystem : ISystem
    {
        private const int INITIAL_CAPACITY = 4096;
        private const float CELL_SIZE = 1f;

        public void OnCreate(ref SystemState state)
        {
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

        public void OnUpdate(ref SystemState state)
        {
            NativeSpatialHash<AgentMove> hash = SystemAPI.GetSingletonRW<AgentSpatialHash>().ValueRW.Hash;
            hash.Clear();

            foreach (RefRO<AgentMove> agent in SystemAPI.Query<RefRO<AgentMove>>())
            {
                AgentMove move = agent.ValueRO;
                float radius = move.Radius;
                hash.AddAABB(move.Position - radius, move.Position + radius, move);
            }
        }
    }
}
