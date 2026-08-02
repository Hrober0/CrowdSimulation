using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    public class CellObjectRegistrationSystemTests
    {
        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static int CountObjectsAt(CellObjectMap map, int2 cell)
        {
            int count = 0;
            foreach (Entity _ in map.GetObjectsAt(cell))
            {
                count++;
            }

            return count;
        }

        [Test]
        public void AnAddedObject_BlocksItsCellAndIsFoundOnIt()
        {
            int2 cell = new(4, 6);
            Entity tree = _world.CreateCellObject(cell, CellData.BLOCKED);

            _world.Map.IsPassable(cell).Should().BeTrue("nothing is registered before the phase runs");

            _world.Tick();

            _world.Map.IsPassable(cell).Should().BeFalse();
            _world.CellObjects.TryGetFirst(cell, out Entity found).Should().BeTrue();
            found.Should().Be(tree);
        }

        [Test]
        public void RegistrationHappensOnce_HoweverManyTimesThePhaseRuns()
        {
            int2 cell = new(0, 0);
            _world.CreateCellObject(cell, 10);

            _world.Tick();
            _world.Tick();
            _world.Tick();

            _world.Map.GetCost(cell).Should().Be(10);
            _world.CellObjects.CountAt(cell).Should().Be(1);
        }

        [Test]
        public void ObjectsSharingACell_AreAllListedAndTheirCostsSum()
        {
            int2 cell = new(-3, 2);
            _world.CreateCellObject(cell, 120);
            _world.CreateCellObject(cell, 120);
            _world.CreateCellObject(cell, 120);

            _world.Tick();

            _world.Map.GetCost(cell).Should().Be(360);
            _world.CellObjects.CountAt(cell).Should().Be(3);
            CountObjectsAt(_world.CellObjects, cell).Should().Be(3);
        }

        [Test]
        public void HarvestingOneOfThreeBlockers_LeavesTheCellBlocked()
        {
            int2 cell = new(7, 7);
            Entity first = _world.CreateCellObject(cell, CellData.BLOCKED);
            _world.CreateCellObject(cell, CellData.BLOCKED);
            _world.CreateCellObject(cell, CellData.BLOCKED);
            _world.Tick();

            _world.Entities.DestroyEntity(first);
            _world.Tick();

            _world.Map.IsPassable(cell).Should().BeFalse("two trees are still standing there");
            _world.CellObjects.CountAt(cell).Should().Be(2);
        }

        [Test]
        public void RemovingEveryObject_ReopensTheCellAtExactlyZeroCost()
        {
            int2 cell = new(1, -1);
            Entity a = _world.CreateCellObject(cell, CellData.BLOCKED);
            Entity b = _world.CreateCellObject(cell, 40);
            _world.Tick();

            _world.Entities.DestroyEntity(a);
            _world.Entities.DestroyEntity(b);
            _world.Tick();

            _world.Map.GetCost(cell).Should().Be(0);
            _world.Map.IsPassable(cell).Should().BeTrue();
            _world.CellObjects.IsOccupied(cell).Should().BeFalse();
        }

        [Test]
        public void ADestroyedObject_IsGoneCompletelyAfterThePhase()
        {
            Entity tree = _world.CreateCellObject(new int2(2, 2), CellData.BLOCKED);
            _world.Tick();

            _world.Entities.DestroyEntity(tree);
            _world.Entities.Exists(tree).Should().BeTrue("cleanup data keeps the entity alive for one phase");

            _world.Tick();

            _world.Entities.Exists(tree).Should().BeFalse();
        }

        [Test]
        public void DroppingTheComponentWithoutDestroying_AlsoReleasesTheCell()
        {
            int2 cell = new(5, -5);
            Entity rock = _world.CreateCellObject(cell, CellData.BLOCKED, ObjectKind.Rock);
            _world.Tick();

            _world.Entities.RemoveComponent<CellObject>(rock);
            _world.Tick();

            _world.Map.IsPassable(cell).Should().BeTrue();
            _world.CellObjects.IsOccupied(cell).Should().BeFalse();
            _world.Entities.Exists(rock).Should().BeTrue("only the component went away, not the entity");
        }

        [Test]
        public void ObjectsOnDifferentCells_DoNotAffectEachOther()
        {
            int2 occupied = new(3, 3);
            int2 empty = new(3, 4);
            _world.CreateCellObject(occupied, CellData.BLOCKED);

            _world.Tick();

            _world.Map.IsPassable(empty).Should().BeTrue();
            _world.CellObjects.IsOccupied(empty).Should().BeFalse();
            _world.CellObjects.TryGetFirst(empty, out Entity _).Should().BeFalse();
        }

        [Test]
        public void TheMapAndTheGridAreDisposedWithTheWorld()
        {
            _world.CreateCellObject(new int2(0, 0), CellData.BLOCKED);
            _world.Tick();

            // A leaked native container fails the run through the Collections leak detector.
            _world.Dispose();
            _world = new RtsTestWorld();
        }
    }
}
