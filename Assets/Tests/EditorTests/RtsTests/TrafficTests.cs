using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Agents getting in each other's way, and the two things that deal with it (design §8): one-way roads
    /// so traffic cannot meet head-on, and the watchdog for agents that end up stuck anyway.
    /// </summary>
    public class TrafficTests
    {
        private static readonly ItemId Bread = new(1);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 Centre(int2 cell) => GridCoords.CellCenter(cell);

        /// <summary>The mask the one-way brush paints: everything except the reverse of the drag.</summary>
        private static byte OneWay(Direction direction) =>
            DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, DirectionUtils.Opposite(direction));

        [Test]
        public void AOneWayCellForbidsTheReverseAndNothingElse()
        {
            _world.Enqueue(GridEdit.SetExits(new int2(0, 0), OneWay(Direction.East)));
            _world.Tick();

            CellData cell = _world.Map.GetCell(new int2(0, 0));

            cell.CanExit(Direction.East).Should().BeTrue("that is the way the road runs");
            cell.CanExit(Direction.West).Should().BeFalse("and back up it is the whole point");
            cell.CanExit(Direction.North).Should().BeTrue("or nobody could ever get off the road");
            cell.CanExit(Direction.South).Should().BeTrue();
        }

        [Test]
        public void ClearingACellMakesItTwoWayAgain()
        {
            _world.Enqueue(GridEdit.SetExits(new int2(0, 0), OneWay(Direction.East)));
            _world.Tick();

            _world.Enqueue(GridEdit.SetExits(new int2(0, 0), DirectionUtils.ALL_EXITS));
            _world.Tick();

            _world.Map.GetCell(new int2(0, 0)).IsOneWay.Should().BeFalse();
        }

        [Test]
        public void AnAgentThatCanNeverArrive_HasItsTaskDropped()
        {
            var goal = new int2(20, 20);
            foreach (int2 offset in new[] { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) })
            {
                _world.Enqueue(GridEdit.CostDelta(goal + offset, CellData.BLOCKED));
            }

            Entity agent = _world.CreateIdleAgent(Centre(new int2(0, 0)));
            _world.StepsOf(agent).Add(TaskStep.GoTo(goal));

            _world.TickFrames(80, 0.1f);

            _world.StepsOf(agent).IsEmpty.Should()
                  .BeTrue("eight seconds of getting nowhere is the watchdog's business");
            _world.IsWalking(agent).Should().BeFalse("and it stops trying");
        }

        [Test]
        public void AnAgentThatIsGettingSomewhereIsLeftAlone()
        {
            var goal = new int2(-28, 2);
            Entity agent = _world.CreateIdleAgent(Centre(new int2(28, 2)));
            _world.StepsOf(agent).Add(TaskStep.GoTo(goal));

            // Fifty-six cells at four a second is fourteen seconds of walking - far longer than the stall
            // window, so a watchdog that did not reset on progress would cut the journey short.
            _world.TickFrames(500, 0.05f);

            math.distance(_world.AgentOf(agent).Position, Centre(goal)).Should()
                .BeLessThan(0.6f, "a long walk is not a stall");
        }

        [Test]
        public void AStalledHaulerGivesItsReservationBack()
        {
            // A source whose only doorstep is walled in: reachable on paper, unreachable in fact.
            Entity mine = _world.CreateSource(new int2(10, 10), Bread, amount: 40);
            foreach (int2 cell in new[] { new int2(9, 9), new int2(11, 9), new int2(10, 8) })
            {
                _world.Enqueue(GridEdit.CostDelta(cell, CellData.BLOCKED));
            }

            _world.CreateWarehouse(new int2(0, 0), Bread, capacity: 50);
            _world.CreateIdleAgent(Centre(new int2(2, 0)), carryCapacity: 10);

            bool everReserved = false;
            bool givenBack = false;

            for (int i = 0; i < 200; i++)
            {
                _world.TickFrame(0.1f);

                int reserved = _world.SlotOf(mine, Bread).ReservedOut;
                everReserved |= reserved > 0;

                if (everReserved && reserved == 0)
                {
                    givenBack = true;
                    break;
                }
            }

            everReserved.Should().BeTrue("the haul was claimed");
            givenBack.Should().BeTrue("and the stock was un-promised once the trip was given up on");
        }

        [Test]
        public void AClaimWithNoTaskBehindItIsGivenBack()
        {
            // A building with room, deliberately not a shelter, so nothing re-claims the slot afterwards
            // and the release is the only thing the test can be measuring.
            Entity building = _world.CreateBuilding(new int2(10, 10), GridRotation.None, int2.zero);
            _world.AddEntrance(building, int2.zero, Direction.South);
            _world.Entities.AddComponentData(building, new Interior { Capacity = 1, Claimed = 1 });

            Entity agent = _world.CreateIdleAgent(Centre(new int2(0, 0)));
            _world.Entities.SetComponentData(agent, new InteriorClaim { Building = building });
            _world.Entities.SetComponentEnabled<InteriorClaim>(agent, true);

            _world.TickFrame(0.1f);

            _world.InteriorOf(building).Claimed.Should()
                  .Be(0, "a building must not slowly lose its slots to journeys nobody finished");
            _world.HasClaim(agent).Should().BeFalse();
        }
    }
}
