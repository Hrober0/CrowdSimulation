using GridNav;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Rts
{
    /// <summary>
    /// Turns baked <see cref="GridCostPatch"/> rectangles into grid edits. Like every other producer it only
    /// enqueues - <see cref="GridApplySystem"/>, ordered last in the group, does the writing.
    /// </summary>
    [UpdateInGroup(typeof(GridUpdateGroup))]
    [BurstCompile]
    public partial struct GridCostPatchSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GridWorld>();
            state.RequireForUpdate<GridCostPatch>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RefRW<GridWorld> gridWorld = SystemAPI.GetSingletonRW<GridWorld>();
            GridEditQueue edits = gridWorld.ValueRW.Edits;

            foreach ((RefRO<GridCostPatch> patch, Entity entity)
                     in SystemAPI.Query<RefRO<GridCostPatch>>().WithEntityAccess())
            {
                Enqueue(edits, patch.ValueRO);
                SystemAPI.SetComponentEnabled<GridCostPatch>(entity, false);
            }
        }

        private static void Enqueue(GridEditQueue edits, in GridCostPatch patch)
        {
            int2 max = patch.MinCell + patch.SizeInCells;
            for (int y = patch.MinCell.y; y < max.y; y++)
            {
                for (int x = patch.MinCell.x; x < max.x; x++)
                {
                    var cell = new int2(x, y);

                    if (patch.Cost != 0)
                    {
                        edits.Enqueue(GridEdit.CostDelta(cell, patch.Cost));
                    }

                    if (patch.Flags != CellFlags.None)
                    {
                        edits.Enqueue(GridEdit.AddFlags(cell, patch.Flags));
                    }

                    if (patch.Exits != DirectionUtils.ALL_EXITS)
                    {
                        edits.Enqueue(GridEdit.SetExits(cell, patch.Exits));
                    }
                }
            }
        }
    }
}
