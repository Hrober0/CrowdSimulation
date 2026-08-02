using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Unity.Mathematics;

namespace Tests.EditorTests.GridNavTests
{
    /// <summary>
    /// A 128x128 map, which is exactly one field window, so every cell is inside the field under test.
    /// </summary>
    public class FlowFieldCacheTests
    {
        private const int MAP_SIZE = 128;
        private static readonly int2 Goal = new(0, 0);

        private GridNavTestWorld _world;

        [SetUp]
        public void Setup() => _world = new GridNavTestWorld(MAP_SIZE);

        [TearDown]
        public void Teardown() => _world.Dispose();

        private int BuildFieldFor(int2 goal)
        {
            _world.Fields.Request(goal);
            _world.Tick();

            _world.Fields.TryGetSlot(goal, out int slot).Should().BeTrue();
            return slot;
        }

        [Test]
        public void AFieldIsBuiltForARequestedDestination()
        {
            _world.Tick(); // brings the cache into existence

            _world.Fields.TryGetSlot(Goal, out int _).Should().BeFalse("nothing has asked for it yet");

            int slot = BuildFieldFor(Goal);

            _world.Fields.GetSlot(slot).Built.Should().BeTrue();
            _world.Fields.GetSlot(slot).GoalCell.Should().Be(Goal);
        }

        [Test]
        public void EveryAgentHeadingSomewhere_SharesTheOneField()
        {
            int first = BuildFieldFor(Goal);

            _world.Fields.Request(Goal);
            _world.Tick();

            _world.Fields.TryGetSlot(Goal, out int second).Should().BeTrue();
            second.Should().Be(first, "a destination is a field, however many agents want it");
        }

        [Test]
        public void TwoDestinationsAskedForTogether_GetSeparateFields()
        {
            int2 west = new(-20, 0);
            int2 east = new(20, 0);

            _world.Fields.Request(west);
            _world.Fields.Request(east);
            _world.Tick();

            _world.Fields.TryGetSlot(west, out int westSlot).Should().BeTrue();
            _world.Fields.TryGetSlot(east, out int eastSlot).Should().BeTrue();

            eastSlot.Should().NotBe(westSlot, "a slot claimed this frame is not free, however unbuilt it is");
            _world.Fields.GetSlot(westSlot).GoalCell.Should().Be(west);
            _world.Fields.GetSlot(eastSlot).GoalCell.Should().Be(east);

            _world.Fields.IntegrationAt(westSlot, west).Should().Be(0);
            _world.Fields.IntegrationAt(eastSlot, east).Should().Be(0);
        }

        [Test]
        public void IntegrationGrowsByOneStepPerCell()
        {
            int slot = BuildFieldFor(Goal);

            _world.Fields.IntegrationAt(slot, Goal).Should().Be(0);
            _world.Fields.IntegrationAt(slot, new int2(1, 0)).Should().Be(NavCost.STEP);
            _world.Fields.IntegrationAt(slot, new int2(5, 0)).Should().Be(5 * NavCost.STEP);
            _world.Fields.IntegrationAt(slot, new int2(3, 4)).Should().Be(7 * NavCost.STEP);
        }

        /// <summary>A whole column, so that going around it is not an option and the cost has to be paid.</summary>
        private void MakeColumnExpensive(int x, int extraCost)
        {
            for (int y = -64; y <= 63; y++)
            {
                _world.Enqueue(GridEdit.CostDelta(new int2(x, y), extraCost));
            }
        }

        [Test]
        public void ExpensiveGroundShowsUpInTheIntegration()
        {
            MakeColumnExpensive(1, 25);
            int slot = BuildFieldFor(Goal);

            _world.Fields.IntegrationAt(slot, new int2(1, 0)).Should().Be(NavCost.STEP,
                "reaching this cell only pays for entering the goal");

            _world.Fields.IntegrationAt(slot, new int2(2, 0)).Should().Be(2 * NavCost.STEP + 25,
                "stepping off it pays for the expensive cell");
        }

        [Test]
        public void DirectionsPointDownhillTowardsTheDestination()
        {
            int slot = BuildFieldFor(Goal);

            _world.Fields.TryGetDirection(slot, new int2(5, 0), out Direction fromEast).Should().BeTrue();
            fromEast.Should().Be(Direction.West);

            _world.Fields.TryGetDirection(slot, new int2(0, 5), out Direction fromNorth).Should().BeTrue();
            fromNorth.Should().Be(Direction.South);

            _world.Fields.TryGetDirection(slot, new int2(-5, 0), out Direction fromWest).Should().BeTrue();
            fromWest.Should().Be(Direction.East);
        }

        [Test]
        public void TheDestinationItselfHasNowhereToGo()
        {
            int slot = BuildFieldFor(Goal);

            _world.Fields.TryGetDirection(slot, Goal, out Direction _).Should().BeFalse();
        }

        [Test]
        public void AWalledOffCellIsUnreachableAndPointsNowhere()
        {
            int2 prison = new(20, 20);
            foreach (int2 offset in new[] { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) })
            {
                _world.Enqueue(GridEdit.CostDelta(prison + offset, CellData.BLOCKED));
            }

            int slot = BuildFieldFor(Goal);

            _world.Fields.IntegrationAt(slot, prison).Should().Be(FlowField.UNREACHABLE);
            _world.Fields.TryGetDirection(slot, prison, out Direction _).Should().BeFalse();
        }

        [Test]
        public void AFieldRoutesAroundAWall()
        {
            // A wall on x = 10 with a gap at the top of the map.
            for (int y = -64; y <= 40; y++)
            {
                _world.Enqueue(GridEdit.CostDelta(new int2(10, y), CellData.BLOCKED));
            }

            int slot = BuildFieldFor(Goal);

            int2 behindTheWall = new(11, 0);
            _world.Fields.IntegrationAt(slot, behindTheWall).Should().NotBe(FlowField.UNREACHABLE);
            _world.Fields.IntegrationAt(slot, behindTheWall).Should()
                  .BeGreaterThan(11 * NavCost.STEP, "the straight line through the wall is not available");

            _world.Fields.TryGetDirection(slot, behindTheWall, out Direction step).Should().BeTrue();
            step.Should().Be(Direction.North, "the way round is over the top");
        }

        [Test]
        public void AOneWayLineCutsOffEverythingBehindIt()
        {
            // Cells on x = 10 may not step west, so nothing east of it can ever reach a goal to the west.
            for (int y = -64; y <= 63; y++)
            {
                _world.Enqueue(GridEdit.SetExits(
                    new int2(10, y),
                    DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, Direction.West)
                ));
            }

            int slot = BuildFieldFor(Goal);

            _world.Fields.IntegrationAt(slot, new int2(9, 0)).Should().Be(9 * NavCost.STEP,
                "the near side is unaffected");

            _world.Fields.IntegrationAt(slot, new int2(10, 0)).Should().Be(FlowField.UNREACHABLE,
                "expanding backwards has to test the neighbour's exit bit, not the cell's own");

            _world.Fields.IntegrationAt(slot, new int2(20, 0)).Should().Be(FlowField.UNREACHABLE);
        }

        [Test]
        public void AFieldGoesStaleWhenTheGroundUnderItChanges()
        {
            int slot = BuildFieldFor(Goal);
            _world.Fields.IsFresh(slot, _world.Map).Should().BeTrue();

            _world.Enqueue(GridEdit.CostDelta(new int2(4, 4), 30));
            _world.Tick();

            _world.Fields.IsFresh(slot, _world.Map).Should().BeFalse();
        }

        [Test]
        public void AStaleFieldIsRebuiltInPlaceWhenAskedForAgain()
        {
            int slot = BuildFieldFor(Goal);
            ushort before = _world.Fields.IntegrationAt(slot, new int2(2, 0));

            MakeColumnExpensive(1, 40);
            _world.Tick();

            int rebuilt = BuildFieldFor(Goal);

            rebuilt.Should().Be(slot, "the destination keeps its slot");
            _world.Fields.IsFresh(rebuilt, _world.Map).Should().BeTrue();
            _world.Fields.IntegrationAt(rebuilt, new int2(2, 0)).Should().Be((ushort)(before + 40));
        }

        [Test]
        public void TheLeastRecentlyAskedForFieldIsTheOneEvicted()
        {
            int2 firstGoal = new(-60, -60);
            BuildFieldFor(firstGoal);

            // Fill every remaining slot, then one more, without ever asking for the first goal again.
            for (int i = 0; i < FlowFieldCache.CAPACITY; i++)
            {
                BuildFieldFor(new int2(i * 2, 40));
            }

            _world.Fields.TryGetSlot(firstGoal, out int _).Should()
                  .BeFalse("it went longest without being asked for");
        }

        [Test]
        public void KeepingAskingForAFieldKeepsIt()
        {
            int2 busyGoal = new(-60, -60);
            BuildFieldFor(busyGoal);

            for (int i = 0; i < FlowFieldCache.CAPACITY; i++)
            {
                _world.Fields.Request(busyGoal);
                BuildFieldFor(new int2(i * 2, 40));
            }

            _world.Fields.TryGetSlot(busyGoal, out int _).Should().BeTrue();
        }
    }
}
