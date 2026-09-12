using GridNav;
using Rts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Rts
{
    /// <summary>
    /// Turns a baked <see cref="AgentSpawn"/> into agents. Example scaffolding, not simulation: the real game
    /// will spawn agents from buildings, but nothing can be seen walking until something puts agents on the
    /// map.
    ///
    /// What an agent is made of is <see cref="AgentFactory"/>'s business, not this system's - a camp makes
    /// them too now (§14 step 12), and two hand-written copies of one archetype is one of them silently
    /// missing a component that was added later.
    ///
    /// Agents are only placed on passable cells. Starting inside a blocked one is not survivable - the clamp
    /// in <see cref="AgentIntegrateSystem"/> refuses every move out of it, and the agent would stand there
    /// forever with nothing to explain why.
    /// </summary>
    [UpdateInGroup(typeof(RtsAgentGroup), OrderFirst = true)]
    [UpdateBefore(typeof(AgentSpatialHashSystem))]
    public partial struct AgentSpawnSystem : ISystem
    {
        /// <summary>Attempts at a passable cell before an agent is given up on.</summary>
        private const int PLACEMENT_ATTEMPTS = 16;

        private EntityArchetype _archetype;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
            state.RequireForUpdate<AgentSpawn>();

            _archetype = AgentFactory.Archetype(state.EntityManager);
        }

        public void OnUpdate(ref SystemState state)
        {
            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;

            // Collected first: creating entities is a structural change, and doing it mid-query would
            // invalidate the very iteration that found the requests.
            var requests = new NativeList<AgentSpawn>(4, Allocator.Temp);

            foreach ((RefRO<AgentSpawn> spawn, Entity entity)
                     in SystemAPI.Query<RefRO<AgentSpawn>>().WithEntityAccess())
            {
                requests.Add(spawn.ValueRO);
                SystemAPI.SetComponentEnabled<AgentSpawn>(entity, false);
            }

            foreach (AgentSpawn request in requests)
            {
                Spawn(ref state, map, request);
            }

            requests.Dispose();
        }

        private void Spawn(ref SystemState state, in GridMap map, in AgentSpawn request)
        {
            var random = new Random(request.Seed);
            float2 half = request.Size * 0.5f;

            for (int i = 0; i < request.Count; i++)
            {
                if (!TryFindStart(map, ref random, request.Center - half, request.Center + half, out float2 position))
                {
                    continue;
                }

                Entity agent = AgentFactory.Create(state.EntityManager, _archetype, new AgentSpec
                {
                    Position = position,
                    MaxSpeed = request.MaxSpeed,
                    Radius = request.Radius,
                    CarryCapacity = request.CarryCapacity,
                    MaxHealth = request.MaxHealth,
                    Faction = request.Faction,
                    Weapon = request.Weapon,
                    Leash = request.Leash,
                });

                // The walk is a task step rather than an enabled PathFollow: TaskStepSystem owns when an
                // agent walks, and an agent that arrives with an empty buffer is then idle by definition and
                // gets picked up by IdleAssignSystem (§6). An idle spawn just skips the step and is picked
                // up on the next economy tick.
                if (!request.Idle)
                {
                    state.EntityManager.GetBuffer<TaskStep>(agent).Add(TaskStep.GoTo(request.GoalCell));
                }
            }
        }

        private static bool TryFindStart(in GridMap map, ref Random random, float2 min, float2 max, out float2 position)
        {
            for (int attempt = 0; attempt < PLACEMENT_ATTEMPTS; attempt++)
            {
                position = random.NextFloat2(min, max);
                if (map.IsPassable(GridCoords.CellOf(position)))
                {
                    return true;
                }
            }

            position = default;
            return false;
        }
    }
}
