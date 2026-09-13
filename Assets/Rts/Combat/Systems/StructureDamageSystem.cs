using GridNav;
using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Re-prices a building on the grid when it has taken enough damage to change band
    /// (design §14 step 13, amended).
    ///
    /// This is the half of the cost model that makes a wall worth attacking rather than merely possible to
    /// attack: a battered wall is a shorter breach, so a route through it gets cheaper as it burns, and an
    /// attacking wave drifts towards the weak spot with nothing steering it there.
    ///
    /// **It fires on the band, not on the hit.** Every write here bumps a chunk's structure version and
    /// rebuilds the assault fields covering it; doing that per hit would mean rebuilding for every swing of
    /// every axe in a siege. Four writes over a building's whole life is the whole cost, and no route worth
    /// having changes between one shot and the next.
    ///
    /// In the grid phase, through the edit queue, because the grid has exactly one writer (§13.2).
    /// </summary>
    [UpdateInGroup(typeof(GridUpdateGroup))]
    [UpdateAfter(typeof(BuildingFootprintSystem))]
    public partial struct StructureDamageSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
        }

        public void OnUpdate(ref SystemState state)
        {
            GridEditQueue edits = SystemAPI.GetSingletonRW<GridWorld>().ValueRW.Edits;

            foreach ((RefRO<Health> health, RefRW<StructureBand> band, RefRO<Faction> faction,
                      DynamicBuffer<BuildingFootprintCell> footprint)
                     in SystemAPI.Query<RefRO<Health>, RefRW<StructureBand>, RefRO<Faction>,
                                        DynamicBuffer<BuildingFootprintCell>>())
            {
                byte now = StructureBands.Of(health.ValueRO);
                if (now == band.ValueRO.Value)
                {
                    continue;
                }

                band.ValueRW.Value = now;

                // Band zero is a building that has run out of health. Its cells are given back by the
                // demolish path a moment later, when ReaperSystem destroys it - clearing here as well would
                // be a second answer to the same question, so the entry is simply left to that.
                if (now == 0)
                {
                    continue;
                }

                ushort banded = StructureBands.HealthOf(health.ValueRO);

                foreach (BuildingFootprintCell cell in footprint)
                {
                    edits.Enqueue(GridEdit.SetStructure(cell.Cell, faction.ValueRO.Id, banded));
                }
            }
        }
    }
}
