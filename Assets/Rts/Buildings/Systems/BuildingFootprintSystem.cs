using GridNav;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Takes and gives back the cells a building stands on (design §13.3 #2).
    ///
    /// Every footprint cell is blocked and flagged <see cref="CellFlags.Building"/>. Blocked cells are what
    /// keep *paths* out of a building; RVO obstacles - which keep a shoved agent out of one - come with the
    /// avoidance integration, not here (§3).
    /// </summary>
    [UpdateInGroup(typeof(GridUpdateGroup))]
    [UpdateAfter(typeof(CellObjectRegistrationSystem))]
    public partial struct BuildingFootprintSystem : ISystem
    {
        /// <summary>A footprint cell always blocks, so the refund is a constant and needs no recording.</summary>
        private const int FOOTPRINT_COST = CellData.BLOCKED;

        private EntityQuery _placed;
        private EntityQuery _demolished;

        public void OnCreate(ref SystemState state)
        {
            _placed = SystemAPI.QueryBuilder()
                               .WithAll<BuildingPlacement, BuildingFootprintOffset>()
                               .WithNone<BuildingFootprintCell>()
                               .Build();

            _demolished = SystemAPI.QueryBuilder()
                                   .WithAll<BuildingFootprintCell>()
                                   .WithNone<BuildingPlacement>()
                                   .Build();

            state.RequireForUpdate<GridWorld>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (_placed.IsEmpty && _demolished.IsEmpty)
            {
                return;
            }

            GridEditQueue edits = SystemAPI.GetSingletonRW<GridWorld>().ValueRW.Edits;
            var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<BuildingPlacement> placement, DynamicBuffer<BuildingFootprintOffset> footprint, Entity entity)
                     in SystemAPI.Query<RefRO<BuildingPlacement>, DynamicBuffer<BuildingFootprintOffset>>()
                                 .WithNone<BuildingFootprintCell>()
                                 .WithEntityAccess())
            {
                DynamicBuffer<BuildingFootprintCell> occupied =
                    commands.AddBuffer<BuildingFootprintCell>(entity);

                BuildingPlacement placed = placement.ValueRO;
                foreach (BuildingFootprintOffset offset in footprint)
                {
                    int2 cell = placed.OriginCell + RotationUtils.Rotate(offset.Offset, placed.Rotation);

                    edits.Enqueue(GridEdit.CostDelta(cell, FOOTPRINT_COST));
                    edits.Enqueue(GridEdit.AddFlags(cell, CellFlags.Building));

                    occupied.Add(new BuildingFootprintCell { Cell = cell });
                }
            }

            foreach ((DynamicBuffer<BuildingFootprintCell> occupied, Entity entity)
                     in SystemAPI.Query<DynamicBuffer<BuildingFootprintCell>>()
                                 .WithNone<BuildingPlacement>()
                                 .WithEntityAccess())
            {
                foreach (BuildingFootprintCell cell in occupied)
                {
                    edits.Enqueue(GridEdit.CostDelta(cell.Cell, -FOOTPRINT_COST));
                    edits.Enqueue(GridEdit.RemoveFlags(cell.Cell, CellFlags.Building));
                }

                commands.RemoveComponent<BuildingFootprintCell>(entity);
            }

            commands.Playback(state.EntityManager);
            commands.Dispose();
        }
    }
}
