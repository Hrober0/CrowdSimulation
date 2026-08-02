using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.GridNavTests
{
    /// <summary>
    /// The write path end to end: settings -> allocated grid -> queued edits -> applied by the one writer.
    /// </summary>
    public class GridApplySystemTests
    {
        private World _world;
        private SystemHandle _gridMapSystem;
        private SystemHandle _gridApplySystem;

        [SetUp]
        public void Setup()
        {
            _world = new World("GridNavTests");
            _gridMapSystem = _world.CreateSystem<GridMapSystem>();
            _gridApplySystem = _world.CreateSystem<GridApplySystem>();
        }

        [TearDown]
        public void Teardown()
        {
            // Disposes the grid through GridMapSystem.OnDestroy - a leak here fails the run.
            _world.Dispose();
        }

        private void CreateGrid()
        {
            _world.EntityManager.CreateSingleton(GridSettings.FromCells(new int2(64, 64), centerOnOrigin: true));
            _gridMapSystem.Update(_world.Unmanaged);
        }

        private GridWorld GetGridWorld()
        {
            using EntityQuery query = _world.EntityManager.CreateEntityQuery(typeof(GridWorld));
            query.TryGetSingleton(out GridWorld gridWorld).Should().BeTrue();
            return gridWorld;
        }

        private void Apply()
        {
            _gridApplySystem.Update(_world.Unmanaged);
            _world.EntityManager.CompleteAllTrackedJobs();
        }

        [Test]
        public void WithoutSettings_NoGridIsAllocated()
        {
            _gridMapSystem.Update(_world.Unmanaged);

            using EntityQuery query = _world.EntityManager.CreateEntityQuery(typeof(GridWorld));
            query.IsEmpty.Should().BeTrue();
        }

        [Test]
        public void Settings_AllocateAGridOfWholeChunks()
        {
            CreateGrid();

            GridMap map = GetGridWorld().Map;
            map.IsCreated.Should().BeTrue();
            map.ChunkCount.Should().Be(new int2(2, 2));
            map.MinCell.Should().Be(new int2(-32, -32));
        }

        [Test]
        public void TheGridIsAllocatedOnce()
        {
            CreateGrid();
            GridMap first = GetGridWorld().Map;

            _gridMapSystem.Update(_world.Unmanaged);

            GetGridWorld().Map.CellIndex(int2.zero).Should().Be(first.CellIndex(int2.zero));
            _world.EntityManager.CreateEntityQuery(typeof(GridWorld)).CalculateEntityCount().Should().Be(1);
        }

        [Test]
        public void QueuedEdits_AreAppliedByTheSystemAndDrained()
        {
            CreateGrid();
            GridWorld gridWorld = GetGridWorld();
            int2 cell = new(3, -7);

            gridWorld.Edits.Enqueue(GridEdit.CostDelta(cell, CellData.BLOCKED));
            gridWorld.Edits.Enqueue(GridEdit.AddFlags(cell, CellFlags.Building));
            gridWorld.Map.IsPassable(cell).Should().BeTrue("nothing is written before the apply system runs");

            Apply();

            gridWorld.Edits.Count.Should().Be(0);
            gridWorld.Map.IsPassable(cell).Should().BeFalse();
            gridWorld.Map.GetFlags(cell).Should().Be(CellFlags.Building);
        }

        [Test]
        public void EditsQueuedAcrossFrames_AccumulateExactly()
        {
            CreateGrid();
            GridWorld gridWorld = GetGridWorld();
            int2 cell = new(0, 0);

            gridWorld.Edits.Enqueue(GridEdit.CostDelta(cell, CellData.BLOCKED));
            Apply();

            gridWorld.Edits.Enqueue(GridEdit.CostDelta(cell, CellData.BLOCKED));
            Apply();

            gridWorld.Edits.Enqueue(GridEdit.CostDelta(cell, -CellData.BLOCKED));
            Apply();

            gridWorld.Map.GetCost(cell).Should().Be(CellData.BLOCKED);
            gridWorld.Map.IsPassable(cell).Should().BeFalse();
        }

        [Test]
        public void AnEmptyQueue_IsNotWorthAJob()
        {
            CreateGrid();

            Apply();

            GetGridWorld().Map.GetChunkVersionsAtCell(int2.zero).CostVersion.Should().Be(0);
        }
    }
}
