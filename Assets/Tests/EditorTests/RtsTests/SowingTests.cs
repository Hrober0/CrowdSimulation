using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Planting: the first task in the game whose subject is a place rather than a thing
    /// (design §14 step 11).
    ///
    /// What a planter makes is an ordinary world object with a yield on it, so the last test here is the one
    /// that matters: a lumber camp cannot tell a grove from a wood that was always there.
    /// </summary>
    public class SowingTests
    {
        private static readonly ItemId Wood = new(4);

        private static readonly int2 PlanterCell = new(8, 8);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private int2 Doorstep => PlanterCell + new int2(0, -1);

        private int CountTrees()
        {
            using EntityQuery query = _world.Entities.CreateEntityQuery(ComponentType.ReadOnly<CellObject>());
            using var objects = query.ToComponentDataArray<CellObject>(Unity.Collections.Allocator.Temp);

            int trees = 0;
            foreach (CellObject o in objects)
            {
                if (o.Kind == ObjectKind.Tree)
                {
                    trees++;
                }
            }

            return trees;
        }

        [Test]
        public void APlanterGrowsAWoodWhereThereWasNone()
        {
            _world.CreatePlanter(PlanterCell, ObjectKind.Tree, Wood);
            _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep));

            CountTrees().Should().Be(0);

            _world.TickFrames(400);

            CountTrees().Should().BeGreaterThan(1, "one worker, going back and forth, should have planted several");
        }

        /// <summary>
        /// A planted tree is a resource node like any other, which is what lets a lumber camp work a grove
        /// with nothing anywhere knowing it was grown rather than found.
        /// </summary>
        [Test]
        public void WhatIsPlantedCanBeFelled()
        {
            _world.CreatePlanter(PlanterCell, ObjectKind.Tree, Wood);
            _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep));

            _world.TickFrames(200);

            using EntityQuery query = _world.Entities.CreateEntityQuery(
                ComponentType.ReadOnly<CellObject>(), ComponentType.ReadOnly<ResourceNode>());

            query.CalculateEntityCount().Should().BeGreaterThan(0, "a sapling is a node from the moment it lands");

            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            DynamicBuffer<StorageSlot> stock = _world.Entities.GetBuffer<StorageSlot>(entities[0]);

            stock.Length.Should().Be(1);
            stock[0].Item.Should().Be(Wood);
            stock[0].Amount.Should().BeGreaterThan(0, "and it is worth felling");
            stock[0].Priority.Should().Be(0, "a pure source, like every other node");
        }

        /// <summary>
        /// Plantings are spaced, and the spacing is not tidiness: without it a planter fills every cell it
        /// can reach and leaves a solid block, slow to cross and awkward to work from the middle. Refusing a
        /// cell with something already beside it leaves lanes by construction.
        /// </summary>
        [Test]
        public void NothingIsPlantedRightNextToSomethingElse()
        {
            _world.CreatePlanter(PlanterCell, ObjectKind.Tree, Wood);
            _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep));

            _world.TickFrames(600);

            using EntityQuery query = _world.Entities.CreateEntityQuery(ComponentType.ReadOnly<CellObject>());
            using var objects = query.ToComponentDataArray<CellObject>(Unity.Collections.Allocator.Temp);

            objects.Length.Should().BeGreaterThan(1);

            foreach (CellObject planted in objects)
            {
                for (int d = 0; d < DirectionUtils.DIRECTION_COUNT; d++)
                {
                    int2 beside = planted.Cell + DirectionUtils.Offset((Direction)d);

                    _world.CellObjects.IsOccupied(beside).Should()
                          .BeFalse($"nothing should stand next to the planting at {planted.Cell}");
                }
            }
        }

        /// <summary>
        /// Ground that is not clear is not planted on. The check is made again at the moment the sapling
        /// would appear, because a planting takes time and the world moves while a worker walks - and a
        /// cell cannot be reserved the way a shelf can.
        /// </summary>
        [Test]
        public void NothingIsPlantedOnGroundThatIsAlreadyTaken()
        {
            _world.CreatePlanter(PlanterCell, ObjectKind.Tree, Wood, range: 2);
            _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep));

            // Every cell the planter could reach already has something standing on it.
            for (int y = -2; y <= 2; y++)
            {
                for (int x = -2; x <= 2; x++)
                {
                    int2 cell = Doorstep + new int2(x, y);
                    if (!_world.CellObjects.IsOccupied(cell))
                    {
                        _world.CreateCellObject(cell, 60, ObjectKind.Rock);
                    }
                }
            }

            _world.Tick();
            int before = CountTrees();

            _world.TickFrames(300);

            CountTrees().Should().Be(before, "there was nowhere left to put one");
        }

        [Test]
        public void APlanterWithNobodyToSendPlantsNothing()
        {
            _world.CreatePlanter(PlanterCell, ObjectKind.Tree, Wood);

            _world.TickFrames(200);

            CountTrees().Should().Be(0);
        }
    }
}
