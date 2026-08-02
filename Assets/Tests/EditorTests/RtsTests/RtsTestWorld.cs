using System;
using GridNav;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// A world with the grid write phase wired up in the order the real group runs it, so a test can say
    /// "this happened, now tick" and look at the grid.
    /// </summary>
    internal sealed class RtsTestWorld : IDisposable
    {
        private readonly SystemHandle _gridMapSystem;
        private readonly SystemHandle _cellObjectSystem;
        private readonly SystemHandle _buildingSystem;
        private readonly SystemHandle _gridApplySystem;

        public RtsTestWorld(int sizeInCells = 64)
        {
            World = new World("RtsTests");

            _gridMapSystem = World.CreateSystem<GridMapSystem>();
            _cellObjectSystem = World.CreateSystem<CellObjectRegistrationSystem>();
            _buildingSystem = World.CreateSystem<BuildingFootprintSystem>();
            _gridApplySystem = World.CreateSystem<GridApplySystem>();

            World.EntityManager.CreateSingleton(
                GridSettings.FromCells(new int2(sizeInCells, sizeInCells), centerOnOrigin: true)
            );
            _gridMapSystem.Update(World.Unmanaged);
        }

        public World World { get; }

        public EntityManager Entities => World.EntityManager;

        public GridMap Map => GetSingleton<GridWorld>().Map;

        public CellObjectMap CellObjects => GetSingleton<CellObjectMap>();

        /// <summary>One pass of GridUpdateGroup: producers first, the single writer last.</summary>
        public void Tick()
        {
            _cellObjectSystem.Update(World.Unmanaged);
            _buildingSystem.Update(World.Unmanaged);
            _gridApplySystem.Update(World.Unmanaged);
            Entities.CompleteAllTrackedJobs();
        }

        public Entity CreateCellObject(int2 cell, ushort cost, ObjectKind kind = ObjectKind.Tree)
        {
            Entity entity = Entities.CreateEntity(typeof(CellObject));
            Entities.SetComponentData(entity, new CellObject { Cell = cell, Cost = cost, Kind = kind });
            return entity;
        }

        public Entity CreateBuilding(int2 origin, GridRotation rotation, params int2[] footprintOffsets)
        {
            Entity entity = Entities.CreateEntity(typeof(BuildingPlacement));
            Entities.SetComponentData(entity, new BuildingPlacement { OriginCell = origin, Rotation = rotation });

            DynamicBuffer<BuildingFootprintOffset> footprint = Entities.AddBuffer<BuildingFootprintOffset>(entity);
            foreach (int2 offset in footprintOffsets)
            {
                footprint.Add(new BuildingFootprintOffset { Offset = offset });
            }

            return entity;
        }

        private T GetSingleton<T>() where T : unmanaged, IComponentData
        {
            using EntityQuery query = Entities.CreateEntityQuery(typeof(T));
            return query.GetSingleton<T>();
        }

        public void Dispose() => World.Dispose();
    }
}
