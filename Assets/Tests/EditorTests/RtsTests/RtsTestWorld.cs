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

        private readonly SystemHandle _storageRequestSystem;
        private readonly SystemHandle _workRequestSystem;
        private readonly SystemHandle _orderAgingSystem;
        private readonly SystemHandle _orderAssignSystem;
        private readonly SystemHandle _idleAssignSystem;

        private readonly SystemHandle _agentSpatialHashSystem;
        private readonly SystemHandle _taskStepSystem;
        private readonly SystemHandle _pathRouteSystem;
        private readonly SystemHandle _pathRequestSystem;
        private readonly SystemHandle _pathFollowSystem;
        private readonly SystemHandle _avoidanceSystem;
        private readonly SystemHandle _integrateSystem;
        private readonly SystemHandle _interiorTransitionSystem;
        private readonly SystemHandle _interactionSystem;
        private readonly SystemHandle _orderCompletionSystem;
        private readonly SystemHandle _watchdogSystem;

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

            _storageRequestSystem = World.CreateSystem<StorageRequestSystem>();
            _workRequestSystem = World.CreateSystem<WorkRequestSystem>();
            _orderAgingSystem = World.CreateSystem<OrderAgingSystem>();
            _orderAssignSystem = World.CreateSystem<OrderAssignSystem>();
            _idleAssignSystem = World.CreateSystem<IdleAssignSystem>();

            _agentSpatialHashSystem = World.CreateSystem<AgentSpatialHashSystem>();
            _taskStepSystem = World.CreateSystem<TaskStepSystem>();
            _pathRouteSystem = World.CreateSystem<PathRouteSystem>();
            _pathRequestSystem = World.CreateSystem<PathRequestSystem>();
            _pathFollowSystem = World.CreateSystem<PathFollowSystem>();
            _avoidanceSystem = World.CreateSystem<AgentAvoidanceSystem>();
            _integrateSystem = World.CreateSystem<AgentIntegrateSystem>();
            _interiorTransitionSystem = World.CreateSystem<InteriorTransitionSystem>();
            _interactionSystem = World.CreateSystem<InteractionSystem>();
            _orderCompletionSystem = World.CreateSystem<OrderCompletionSystem>();
            _watchdogSystem = World.CreateSystem<WatchdogSystem>();

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

            // The economy group runs at 10 Hz in the game; here it runs every frame, so a test never has to
            // count ticks to find out whether the matching pass has happened yet.
            _storageRequestSystem.Update(World.Unmanaged);
            _workRequestSystem.Update(World.Unmanaged);
            _orderAgingSystem.Update(World.Unmanaged);
            _orderAssignSystem.Update(World.Unmanaged);
            _idleAssignSystem.Update(World.Unmanaged);

            _agentSpatialHashSystem.Update(World.Unmanaged);
            _taskStepSystem.Update(World.Unmanaged);
            _pathRouteSystem.Update(World.Unmanaged);
            _pathRequestSystem.Update(World.Unmanaged);
            _pathFollowSystem.Update(World.Unmanaged);
            _avoidanceSystem.Update(World.Unmanaged);
            _integrateSystem.Update(World.Unmanaged);
            _interiorTransitionSystem.Update(World.Unmanaged);
            _interactionSystem.Update(World.Unmanaged);
            _orderCompletionSystem.Update(World.Unmanaged);
            _watchdogSystem.Update(World.Unmanaged);

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

        public void AddEntrance(Entity building, int2 wallOffset, Direction side)
        {
            DynamicBuffer<BuildingEntranceOffset> entrances =
                Entities.HasBuffer<BuildingEntranceOffset>(building)
                    ? Entities.GetBuffer<BuildingEntranceOffset>(building)
                    : Entities.AddBuffer<BuildingEntranceOffset>(building);

            entrances.Add(new BuildingEntranceOffset { Offset = wallOffset, Side = side });
        }

        /// <summary>A building agents can rest in: one cell, one door on its south wall, and room inside.</summary>
        public Entity CreateShelter(int2 cell, int capacity)
        {
            Entity building = CreateBuilding(cell, GridRotation.None, int2.zero);
            AddEntrance(building, int2.zero, Direction.South);
            Entities.AddComponentData(building, new Interior { Capacity = capacity });
            Entities.AddComponent<IdleShelter>(building);
            return building;
        }

        /// <summary>
        /// An agent walking straight at a goal, with no task behind it. The archetype is the production one
        /// (§9), so the step machine and idle claiming see exactly what they would in the game.
        /// </summary>
        public Entity CreateAgent(float2 position, int2 goalCell, float maxSpeed = 4f, float radius = 0.35f)
        {
            Entity entity = CreateIdleAgent(position, maxSpeed, radius);

            Entities.SetComponentData(entity, new PathFollow
            {
                GoalCell = goalCell,
                ArriveDistance = 0.4f,
                WaypointCell = goalCell,
                RoutedGoal = goalCell,
                RoutedChunk = -1,
            });

            Entities.SetComponentEnabled<PathFollow>(entity, true);
            return entity;
        }

        /// <summary>An agent with nowhere to be, which is what <see cref="IdleAssignSystem"/> is looking for.</summary>
        public Entity CreateIdleAgent(float2 position, float maxSpeed = 4f, float radius = 0.35f, int carryCapacity = 10)
        {
            Entity entity = Entities.CreateEntity(
                typeof(AgentMove), typeof(PathFollow), typeof(ArrivedTag),
                typeof(InsideBuilding), typeof(InteriorClaim),
                typeof(Carry), typeof(AssignedOrder), typeof(MovementWatchdog), typeof(ViewVisible)
            );

            Entities.AddBuffer<PathRoute>(entity);
            Entities.AddBuffer<TaskStep>(entity);

            Entities.SetComponentData(entity, new AgentMove
            {
                Entity = entity,
                Position = position,
                MaxSpeed = maxSpeed,
                Radius = radius,
            });

            Entities.SetComponentData(entity, new PathFollow { ArriveDistance = 0.4f, RoutedChunk = -1 });
            Entities.SetComponentData(entity, new Carry { Capacity = carryCapacity });
            Entities.SetComponentData(entity, new MovementWatchdog { LastProgressPosition = position });

            Entities.SetComponentEnabled<PathFollow>(entity, false);
            Entities.SetComponentEnabled<ArrivedTag>(entity, false);
            Entities.SetComponentEnabled<InsideBuilding>(entity, false);
            Entities.SetComponentEnabled<InteriorClaim>(entity, false);
            Entities.SetComponentEnabled<AssignedOrder>(entity, false);
            return entity;
        }

        /// <summary>A building with one storage slot and a door on its south wall.</summary>
        public Entity CreateStore(int2 cell, StorageSlot slot)
        {
            Entity building = CreateBuilding(cell, GridRotation.None, int2.zero);
            AddEntrance(building, int2.zero, Direction.South);
            Entities.AddBuffer<StorageSlot>(building).Add(slot);
            return building;
        }

        /// <summary>Holds stock and never asks for any: the pure source of §7.</summary>
        public Entity CreateSource(int2 cell, ItemId item, int amount, int capacity = 100) =>
            CreateStore(cell, new StorageSlot
            {
                Item = item,
                Amount = amount,
                Capacity = capacity,
                DeliverInUpTo = 0,
                DeliverOutDownTo = 0,
                Priority = 0,
            });

        /// <summary>Always wants more, and gives to anyone who wants it more.</summary>
        public Entity CreateWarehouse(int2 cell, ItemId item, int capacity = 100, byte priority = 1) =>
            CreateStore(cell, new StorageSlot
            {
                Item = item,
                Amount = 0,
                Capacity = capacity,
                DeliverInUpTo = capacity,
                DeliverOutDownTo = 0,
                Priority = priority,
            });

        /// <summary>
        /// A workshop: a door, work benches, an input slot that asks and never gives back, an output slot
        /// that gives everything away, and a recipe joining them.
        /// </summary>
        public Entity CreateCrafter(
            int2 cell,
            ItemId input,
            ItemId output,
            int benches = 1,
            float craftSeconds = 1f,
            int inputStock = 0)
        {
            Entity building = CreateBuilding(cell, GridRotation.None, int2.zero);
            AddEntrance(building, int2.zero, Direction.South);
            Entities.AddComponentData(building, new Interior { Capacity = benches });

            DynamicBuffer<StorageSlot> slots = Entities.AddBuffer<StorageSlot>(building);
            slots.Add(new StorageSlot
            {
                Item = input,
                Amount = inputStock,
                Capacity = 20,
                DeliverInUpTo = 20,
                DeliverOutDownTo = 20,
                Priority = 5,
            });
            slots.Add(new StorageSlot
            {
                Item = output,
                Amount = 0,
                Capacity = 20,
                DeliverInUpTo = 0,
                DeliverOutDownTo = 0,
                Priority = 0,
            });

            Entities.AddComponentData(building, new Recipe { CraftSeconds = craftSeconds, Priority = 5 });
            Entities.AddBuffer<RecipeInput>(building).Add(new RecipeInput { Item = input, Amount = 1 });
            Entities.AddBuffer<RecipeOutput>(building).Add(new RecipeOutput { Item = output, Amount = 1 });

            return building;
        }

        public DynamicBuffer<StorageSlot> SlotsOf(Entity building) => Entities.GetBuffer<StorageSlot>(building);

        public StorageSlot SlotOf(Entity building, ItemId item)
        {
            DynamicBuffer<StorageSlot> slots = SlotsOf(building);
            StorageSlotUtils.TryGetSlotIndex(slots, item, out int index);
            return index >= 0 ? slots[index] : default;
        }

        public Carry CarryOf(Entity agent) => Entities.GetComponentData<Carry>(agent);

        public bool HasOrder(Entity agent) => Entities.IsComponentEnabled<AssignedOrder>(agent);

        public AssignedOrder OrderOf(Entity agent) => Entities.GetComponentData<AssignedOrder>(agent);

        public OrderBook Orders => GetSingleton<OrderBook>();

        public DynamicBuffer<TaskStep> StepsOf(Entity entity) => Entities.GetBuffer<TaskStep>(entity);

        public Interior InteriorOf(Entity building) => Entities.GetComponentData<Interior>(building);

        public bool IsInside(Entity agent) => Entities.IsComponentEnabled<InsideBuilding>(agent);

        public bool HasClaim(Entity agent) => Entities.IsComponentEnabled<InteriorClaim>(agent);

        public bool IsVisible(Entity agent) => Entities.IsComponentEnabled<ViewVisible>(agent);

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
