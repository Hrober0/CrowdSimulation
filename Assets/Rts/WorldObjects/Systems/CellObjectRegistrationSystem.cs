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

                commands.RemoveComponent<CellObjectRegistered>(entity);
            }

            commands.Playback(state.EntityManager);
            commands.Dispose();
        }
    }
}
