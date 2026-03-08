using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Examples.Storage
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    // [UpdateBefore(typeof(JobAssignmentSystem))]
    public partial struct SpatialGridRebuildSystem : ISystem
    {
        // Capacity tuned for 1000 holders. Resize here if needed.
        private const int InitialCapacity = 1024;
        private const float DefaultCellSize = 20f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // Create the singleton entity and allocate the map once
            var map = new NativeParallelMultiHashMap<int2, HolderSptailEntry>(
                InitialCapacity, Allocator.Persistent);

            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(entity, new IdleHolderGridSingleton
            {
                Cells = map,
                CellSize = DefaultCellSize,
            });

            state.RequireForUpdate<HolderComponent>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingletonRW<IdleHolderGridSingleton>(out var grid))
                grid.ValueRW.Cells.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var grid = ref SystemAPI.GetSingletonRW<IdleHolderGridSingleton>().ValueRW;
            grid.Cells.Clear();

            new FillGridJob
            {
                Grid = grid.Cells.AsParallelWriter(),
                CellSize = grid.CellSize,
            }.ScheduleParallel();
        }

        [BurstCompile]
        private partial struct FillGridJob : IJobEntity
        {
            public NativeParallelMultiHashMap<int2, HolderSptailEntry>.ParallelWriter Grid;
            public float CellSize;

            private void Execute(Entity e, in HolderComponent h, in LocalTransform t)
            {
                if (h.State != HolderState.Idle) return;

                WorldToCell(t.Position, CellSize, out var cell);
                Grid.Add(cell, new HolderSptailEntry { Entity = e, Position = t.Position });
            }
        }
        

        [BurstCompile]
        public static void WorldToCell(in float3 pos, float cellSize, out int2 cell)
        {
            cell = new(
                (int)math.floor(pos.x / cellSize),
                (int)math.floor(pos.z / cellSize));
        }

        /// <summary>
        /// Ring-expansion nearest-idle-holder search.
        /// Checks rings 0 → MaxRings until at least one holder is found,
        /// then checks one more ring to make sure we haven't missed a closer one.
        /// Returns Entity.Null if no idle holder exists anywhere.
        /// </summary>
        [BurstCompile]
        public static void FindNearestIdleHolder(
            in IdleHolderGridSingleton grid,
            in float3 targetPos,
            out float bestDistSq,
            out Entity bestEntity)
        {
            WorldToCell(targetPos, grid.CellSize, out int2 targetCell);
            bestEntity = Entity.Null;
            bestDistSq = float.MaxValue;
            const int MaxRings = 8;

            for (int ring = 0; ring <= MaxRings; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                for (int dz = -ring; dz <= ring; dz++)
                {
                    // Only process cells on the current ring's edge
                    if (math.abs(dx) != ring && math.abs(dz) != ring) continue;

                    int2 cell = targetCell + new int2(dx, dz);

                    if (!grid.Cells.TryGetFirstValue(cell, out var entry, out var it))
                        continue;

                    do
                    {
                        float dsq = math.distancesq(entry.Position, targetPos);
                        if (dsq < bestDistSq)
                        {
                            bestDistSq = dsq;
                            bestEntity = entry.Entity;
                        }
                    } while (grid.Cells.TryGetNextValue(out entry, ref it));
                }

                // Found at least one candidate — check one more ring for safety, then stop
                if (bestEntity != Entity.Null && ring >= 1) break;
            }
        }
    }
}