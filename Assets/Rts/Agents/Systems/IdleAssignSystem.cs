using GridNav;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Gives agents with nothing to do somewhere to be (design §6, §13.3 #11).
    ///
    /// The rule is one line - nearest shelter that still has room, otherwise stand somewhere that is not in
    /// anyone's way - and it is self-balancing by construction: nothing has to be rebalanced when a hut is
    /// built or destroyed, because nothing was balanced in the first place.
    ///
    /// The claim is taken here, before the walk, and that is the point. An agent that cannot get a slot never
    /// sets off, so a hut with two beds never collects a queue of eight arrivals at its door (§8).
    ///
    /// Runs at 10 Hz with the rest of the matching work, single-threaded, and takes a bounded number of
    /// claims per tick: a thousand agents going idle in the same frame is a thousand nearest-shelter searches
    /// otherwise, and being one tick late to sit down is not observable.
    /// </summary>
    [UpdateInGroup(typeof(RtsEconomyGroup))]
    public partial struct IdleAssignSystem : ISystem
    {
        private const int MAX_CLAIMS_PER_TICK = 32;

        /// <summary>How far an agent standing somewhere it should not will look for somewhere it may.</summary>
        private const int PARK_SEARCH_RADIUS = 6;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
        }

        public void OnUpdate(ref SystemState state)
        {
            GridMap map = SystemAPI.GetSingleton<GridWorld>().Map;

            NativeList<ShelterCandidate> shelters = CollectShelters(ref state);
            int claims = 0;

            foreach ((DynamicBuffer<TaskStep> steps, RefRO<AgentMove> agent, EnabledRefRO<PathFollow> walking,
                      RefRW<InteriorClaim> claim, EnabledRefRW<InteriorClaim> claimed)
                     in SystemAPI.Query<DynamicBuffer<TaskStep>, RefRO<AgentMove>, EnabledRefRO<PathFollow>,
                                        RefRW<InteriorClaim>, EnabledRefRW<InteriorClaim>>()
                                 .WithPresent<PathFollow, InteriorClaim>()
                                 .WithDisabled<InsideBuilding>())
            {
                // Idle is "nothing left to do, nothing held, and not already on the way somewhere".
                if (!steps.IsEmpty || claimed.ValueRO || walking.ValueRO)
                {
                    continue;
                }

                float2 position = agent.ValueRO.Position;

                if (claims < MAX_CLAIMS_PER_TICK && TryNearestShelter(shelters, position, out int index))
                {
                    ShelterCandidate shelter = shelters[index];
                    shelter.Free--;
                    shelter.TakenThisTick++;
                    shelters[index] = shelter;

                    claim.ValueRW = new InteriorClaim { Building = shelter.Building };
                    claimed.ValueRW = true;

                    steps.Add(TaskStep.GoTo(shelter.Entrance));
                    steps.Add(TaskStep.Enter(shelter.Building, shelter.Entrance));

                    claims++;
                    continue;
                }

                // No room anywhere: park on the ground. Only worth moving if the agent is standing on a cell
                // idle agents must keep clear - roads and doorways carry NoIdle (§6) - so the common case of
                // an agent already standing somewhere harmless costs one cell read and nothing else.
                int2 cell = GridCoords.CellOf(position);
                if (!map.GetCell(cell).Has(CellFlags.NoIdle))
                {
                    continue;
                }

                if (TryFindPark(map, cell, out int2 park))
                {
                    steps.Add(TaskStep.GoTo(park));
                }
            }

            ApplyClaims(ref state, shelters);
            shelters.Dispose();
        }

        private NativeList<ShelterCandidate> CollectShelters(ref SystemState state)
        {
            var shelters = new NativeList<ShelterCandidate>(16, Allocator.Temp);

            foreach ((RefRO<Interior> interior, DynamicBuffer<BuildingEntranceCell> entrances, Entity entity)
                     in SystemAPI.Query<RefRO<Interior>, DynamicBuffer<BuildingEntranceCell>>()
                                 .WithAll<IdleShelter>()
                                 .WithEntityAccess())
            {
                // A shelter nobody can walk into is not a shelter, whatever its capacity says.
                if (!interior.ValueRO.HasRoom || entrances.IsEmpty)
                {
                    continue;
                }

                shelters.Add(new ShelterCandidate
                {
                    Building = entity,
                    Entrance = entrances[0].Cell,
                    Free = interior.ValueRO.FreeSlots,
                });
            }

            return shelters;
        }

        /// <summary>
        /// Writes the tick's claims back in one pass. Counting in the local list first is what keeps a
        /// shelter with two free slots from being promised to five agents in the same tick.
        /// </summary>
        private void ApplyClaims(ref SystemState state, in NativeList<ShelterCandidate> shelters)
        {
            foreach (ShelterCandidate shelter in shelters)
            {
                if (shelter.TakenThisTick == 0)
                {
                    continue;
                }

                Interior interior = SystemAPI.GetComponent<Interior>(shelter.Building);
                interior.Claimed += shelter.TakenThisTick;
                SystemAPI.SetComponent(shelter.Building, interior);
            }
        }

        private static bool TryNearestShelter(in NativeList<ShelterCandidate> shelters, float2 position, out int index)
        {
            index = -1;
            float best = float.MaxValue;

            for (int i = 0; i < shelters.Length; i++)
            {
                if (shelters[i].Free <= 0)
                {
                    continue;
                }

                // Straight-line distance, not path length. A wrong pick costs one agent a longer walk; a path
                // query per shelter per idle agent would cost the frame.
                float distance = math.distancesq(position, GridCoords.CellCenter(shelters[i].Entrance));
                if (distance < best)
                {
                    best = distance;
                    index = i;
                }
            }

            return index >= 0;
        }

        /// <summary>Nearest cell an idle agent is allowed to stand on, searched ring by ring outwards.</summary>
        private static bool TryFindPark(in GridMap map, int2 from, out int2 park)
        {
            for (int radius = 1; radius <= PARK_SEARCH_RADIUS; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (math.max(math.abs(x), math.abs(y)) != radius)
                        {
                            continue;
                        }

                        var cell = new int2(from.x + x, from.y + y);
                        CellData data = map.GetCell(cell);
                        if (data.IsPassable && !data.Has(CellFlags.NoIdle))
                        {
                            park = cell;
                            return true;
                        }
                    }
                }
            }

            park = default;
            return false;
        }

        private struct ShelterCandidate
        {
            public Entity Building;

            public int2 Entrance;

            /// <summary>Slots left, counted down as this tick hands them out.</summary>
            public int Free;

            public int TakenThisTick;
        }
    }
}
