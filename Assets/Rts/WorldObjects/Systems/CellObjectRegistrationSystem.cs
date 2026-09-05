using GridNav;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Keeps the cell map and the grid's cost sums in step with the world objects that exist
    /// (design §13.3 #1).
    ///
    /// Adding is "insert into the map, queue +cost". Removing is the same in reverse, driven by the
    /// <see cref="CellObjectRegistered"/> cleanup component that outlives the destroyed entity. Because the
    /// refund uses the recorded cost rather than the current one, chopping one of three trees on a cell
    /// leaves the other two blocking it, exactly (§3).
    ///
    /// It also keeps <see cref="CellFlags.Object"/> in step, which is the same bookkeeping done as a count
    /// rather than as a sum: raised by the first object to arrive on a cell and lowered by the last to leave.
    /// The add pass runs before the remove pass, so a cell that gains and loses an object in one frame is
    /// never seen empty in between.
    /// </summary>
    [UpdateInGroup(typeof(GridUpdateGroup))]
    public partial struct CellObjectRegistrationSystem : ISystem
    {
        private const int INITIAL_CAPACITY = 4096;

        private EntityQuery _added;
        private EntityQuery _removed;

        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton(
                new CellObjectMap(INITIAL_CAPACITY, Allocator.Persistent),
                "CellObjectMap"
            );

            _added = SystemAPI.QueryBuilder().WithAll<CellObject>().WithNone<CellObjectRegistered>().Build();
            _removed = SystemAPI.QueryBuilder().WithAll<CellObjectRegistered>().WithNone<CellObject>().Build();

            state.RequireForUpdate<GridWorld>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out CellObjectMap map))
            {
                map.Dispose();
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (_added.IsEmpty && _removed.IsEmpty)
            {
                return;
            }

            GridEditQueue edits = SystemAPI.GetSingletonRW<GridWorld>().ValueRW.Edits;
            CellObjectMap map = SystemAPI.GetSingletonRW<CellObjectMap>().ValueRW;

            var commands = new EntityCommandBuffer(Allocator.Temp);

            foreach ((RefRO<CellObject> cellObject, Entity entity)
                     in SystemAPI.Query<RefRO<CellObject>>().WithNone<CellObjectRegistered>().WithEntityAccess())
            {
                CellObject added = cellObject.ValueRO;

                map.Add(added.Cell, entity);
                edits.Enqueue(GridEdit.CostDelta(added.Cell, added.Cost));

                // Only the first thing on a cell raises the flag, and only the last one to leave lowers it -
                // counted off the map, which is authoritative and already up to date, rather than off the
                // cost sum, which cannot tell one blocker from four cheap ones.
                if (map.CountAt(added.Cell) == 1)
                {
                    edits.Enqueue(GridEdit.AddFlags(added.Cell, CellFlags.Object));
                }

                commands.AddComponent(entity, new CellObjectRegistered
                {
                    Cell = added.Cell,
                    Cost = added.Cost,
                });
            }

            foreach ((RefRO<CellObjectRegistered> registered, Entity entity)
                     in SystemAPI.Query<RefRO<CellObjectRegistered>>().WithNone<CellObject>().WithEntityAccess())
            {
                CellObjectRegistered removed = registered.ValueRO;

                map.Remove(removed.Cell, entity);
                edits.Enqueue(GridEdit.CostDelta(removed.Cell, -removed.Cost));

                if (map.CountAt(removed.Cell) == 0)
                {
                    edits.Enqueue(GridEdit.RemoveFlags(removed.Cell, CellFlags.Object));
                }

                commands.RemoveComponent<CellObjectRegistered>(entity);
            }

            commands.Playback(state.EntityManager);
            commands.Dispose();
        }
    }
}
