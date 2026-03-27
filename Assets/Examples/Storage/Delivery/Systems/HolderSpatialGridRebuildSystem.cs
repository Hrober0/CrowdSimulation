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
            var map = new NativeParallelMultiHashMap<int2, HolderSpatilEntry>(
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
            {
                grid.ValueRW.Cells.Dispose();
            }
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
            public NativeParallelMultiHashMap<int2, HolderSpatilEntry>.ParallelWriter Grid;
            public float CellSize;

            private void Execute(Entity e, in HolderComponent h, in LocalTransform t)
            {
                if (h.State != HolderState.Idle) return;

                WorldToCell(t.Position, CellSize, out var cell);
                Grid.Add(cell, new HolderSpatilEntry { Entity = e, Position = t.Position });
            }
        }
        

        [BurstCompile]
        public static void WorldToCell(in float3 pos, float cellSize, out int2 cell)
        {
            cell = new(
                (int)math.floor(pos.x / cellSize),
                (int)math.floor(pos.y / cellSize));
        }

        /// <summary>
        /// Returns the nearest idle holder within <paramref name="maxRange"/> using
        /// <see cref="GridSearchCursor"/>.  After finding a candidate in ring R, one
        /// additional ring is checked to catch holders that are geometrically closer
        /// despite belonging to the next ring (ring corners vs. next-ring edge centres).
        /// Returns Entity.Null if no idle holder exists in range.
        /// </summary>
        [BurstCompile]
        public static void FindNearestIdleHolder(
            in IdleHolderGridSingleton grid,
            in float3 targetPos,
            out float bestDistSq,
            out Entity bestEntity,
            float maxRange = 200)
        {
            bestEntity = Entity.Null;
            bestDistSq = float.MaxValue;

            var cursor = new GridSearchCursor();
            cursor.Init(in grid, targetPos, maxRange);

            int foundRing = -1;
            while (cursor.MoveNext(out HolderSpatilEntry entry))
            {
                // Once we are two rings past the ring where the first candidate was
                // found, no closer holder can appear.
                if (foundRing >= 0 && cursor.Ring > foundRing + 1) break;

                float dsq = math.distancesq(entry.Position, targetPos);
                if (dsq < bestDistSq)
                {
                    bestDistSq = dsq;
                    bestEntity = entry.Entity;
                    foundRing  = cursor.Ring;
                }
            }
        }
    }
}