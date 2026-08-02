using System;
using GridNav;
using Rts;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// A world with the grid, navigation and agent phases wired up in the order the real groups run them,
    /// so a test can say "this happened, now tick" and look at the result.
    /// </summary>
    internal sealed class RtsTestWorld : IDisposable
    {
        private readonly SystemHandle _gridMapSystem;
        private readonly SystemHandle _cellObjectSystem;
        private readonly SystemHandle _buildingSystem;
        private readonly SystemHandle _gridApplySystem;

        private readonly SystemHandle _gateGraphSystem;
        private readonly SystemHandle _flowFieldSystem;

        private readonly SystemHandle _agentSpatialHashSystem;
        private readonly SystemHandle _pathRouteSystem;
        private readonly SystemHandle _pathRequestSystem;
        private readonly SystemHandle _pathFollowSystem;
        private readonly SystemHandle _avoidanceSystem;
        private readonly SystemHandle _integrateSystem;

        private double _elapsed;

        public RtsTestWorld(int sizeInCells = 64)
        {
            World = new World("RtsTests");

            _gridMapSystem = World.CreateSystem<GridMapSystem>();
            _cellObjectSystem = World.CreateSystem<CellObjectRegistrationSystem>();
            _buildingSystem = World.CreateSystem<BuildingFootprintSystem>();
            _gridApplySystem = World.CreateSystem<GridApplySystem>();

            _gateGraphSystem = World.CreateSystem<ChunkGateGraphSystem>();
            _flowFieldSystem = World.CreateSystem<FlowFieldCacheSystem>();

            _agentSpatialHashSystem = World.CreateSystem<AgentSpatialHashSystem>();
            _pathRouteSystem = World.CreateSystem<PathRouteSystem>();
            _pathRequestSystem = World.CreateSystem<PathRequestSystem>();
            _pathFollowSystem = World.CreateSystem<PathFollowSystem>();
            _avoidanceSystem = World.CreateSystem<AgentAvoidanceSystem>();
            _integrateSystem = World.CreateSystem<AgentIntegrateSystem>();

            World.EntityManager.CreateSingleton(
                GridSettings.FromCells(new int2(sizeInCells, sizeInCells), centerOnOrigin: true)
            );
            _gridMapSystem.Update(World.Unmanaged);
        }

        public World World { get; }

        public EntityManager Entities => World.EntityManager;

        public GridMap Map => GetSingleton<GridWorld>().Map;

        public CellObjectMap CellObjects => GetSingleton<CellObjectMap>();

        public FlowFieldCache Fields => GetSingleton<FlowFieldCache>();

        public void Enqueue(GridEdit edit) => GetSingleton<GridWorld>().Edits.Enqueue(edit);

        /// <summary>The grid write phase only - enough for tests that never move anything.</summary>
        public void Tick()
        {
            _cellObjectSystem.Update(World.Unmanaged);
            _buildingSystem.Update(World.Unmanaged);
            _gridApplySystem.Update(World.Unmanaged);
            Entities.CompleteAllTrackedJobs();
        }

        /// <summary>A whole frame: grid, then navigation, then agents.</summary>
        public void TickFrame(float deltaTime)
        {
            _elapsed += deltaTime;
            World.SetTime(new TimeData(_elapsed, deltaTime));

            Tick();

            _gateGraphSystem.Update(World.Unmanaged);
            _flowFieldSystem.Update(World.Unmanaged);

            _agentSpatialHashSystem.Update(World.Unmanaged);
            _pathRouteSystem.Update(World.Unmanaged);
            _pathRequestSystem.Update(World.Unmanaged);
            _pathFollowSystem.Update(World.Unmanaged);
            _avoidanceSystem.Update(World.Unmanaged);
            _integrateSystem.Update(World.Unmanaged);

            Entities.CompleteAllTrackedJobs();
        }

        public void TickFrames(int count, float deltaTime = 0.1f)
        {
            for (int i = 0; i < count; i++)
            {
                TickFrame(deltaTime);
            }
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

        public Entity CreateAgent(float2 position, int2 goalCell, float maxSpeed = 4f, float radius = 0.35f)
        {
            Entity entity = Entities.CreateEntity(typeof(AgentMove), typeof(PathFollow), typeof(ArrivedTag));
            Entities.AddBuffer<PathRoute>(entity);

            Entities.SetComponentData(entity, new AgentMove
            {
                Entity = entity,
                Position = position,
                MaxSpeed = maxSpeed,
                Radius = radius,
            });

            Entities.SetComponentData(entity, new PathFollow
            {
                GoalCell = goalCell,
                ArriveDistance = 0.4f,
                WaypointCell = goalCell,
                RoutedGoal = goalCell,
                RoutedChunk = -1,
            });

            Entities.SetComponentEnabled<ArrivedTag>(entity, false);
            return entity;
        }

        public AgentMove AgentOf(Entity entity) => Entities.GetComponentData<AgentMove>(entity);

        public bool IsWalking(Entity entity) => Entities.IsComponentEnabled<PathFollow>(entity);

        public bool HasArrived(Entity entity) => Entities.IsComponentEnabled<ArrivedTag>(entity);

        private T GetSingleton<T>() where T : unmanaged, IComponentData
        {
            using EntityQuery query = Entities.CreateEntityQuery(typeof(T));
            return query.GetSingleton<T>();
        }

        public void Dispose() => World.Dispose();
    }
}
