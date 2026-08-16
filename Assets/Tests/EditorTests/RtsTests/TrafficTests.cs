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

        /// <summary>Paints a one-way road along a row, the way the brush of §8 does.</summary>
        private void PaintOneWayRow(int fromX, int toX, int y, Direction direction)
        {
            for (int x = fromX; x <= toX; x++)
            {
                _world.Enqueue(GridEdit.SetExits(new int2(x, y), OneWay(direction)));
            }

            _world.Tick();
        }

        /// <summary>
        /// A door on a one-way road, and two agents either side of it. The one that is *nearer* has to walk the
        /// long way round because the last step towards the door is the one the road forbids, so it is not the
        /// one that should be at the front of the queue - and making it the front was what left everybody
        /// bunched up beside a door none of them went through.
        ///
        /// The queue asks the flow field how far the rest of the walk is, which answers this correctly without
        /// knowing anything about roads, doors or directions.
        /// </summary>
        [Test]
        public void AQueueIsOrderedByTheWayInRatherThanTheWayItLooks()
        {
            var door = new int2(10, 9);
            _world.CreateWarehouse(new int2(10, 10), Bread, capacity: 50);
            PaintOneWayRow(6, 14, y: 9, Direction.East);

            // Two cells out, but on the wrong side: every route in from here goes round the block.
            Entity wrongSide = _world.CreateIdleAgent(Centre(new int2(12, 9)));

            // Three cells out, and pointing the way the road runs.
            Entity onTheWayIn = _world.CreateIdleAgent(Centre(new int2(7, 9)));

            _world.StepsOf(wrongSide).Add(TaskStep.GoTo(door));
            _world.StepsOf(onTheWayIn).Add(TaskStep.GoTo(door));

            _world.TickFrames(4);

            _world.FollowOf(onTheWayIn).Holding.Should()
                  .BeFalse("the front of the queue is whoever can actually get in");
            _world.FollowOf(wrongSide).Holding.Should()
                  .BeTrue("and the one that has to go round waits its turn out of the way");
        }

        /// <summary>
        /// The whole reported failure in one test: a one-way road between the source and the destination, so
        /// every delivery has to leave the road and come at the door from another side, and the road is the fast
        /// way home again. Nothing is authored to say any of that - the field expands backwards through the exit
        /// masks and the route falls out.
        /// </summary>
        [Test]
        public void HaulersDeliverToADoorTheyCanOnlyReachByGoingRound()
        {
            Entity mine = _world.CreateSource(new int2(16, 10), Bread, amount: 100, capacity: 100);
            Entity warehouse = _world.CreateWarehouse(new int2(6, 10), Bread, capacity: 100);

            // Both doorsteps sit on a road that only runs east: the loaded leg is the illegal direction.
            PaintOneWayRow(4, 18, y: 9, Direction.East);

            for (int i = 0; i < 4; i++)
            {
                _world.CreateIdleAgent(Centre(new int2(11 + i, 6)), carryCapacity: 10);
            }

            _world.TickFrames(1200, 0.05f);

            _world.SlotOf(warehouse, Bread).Amount.Should()
                  .BeGreaterThan(60, "going round is longer, not impossible");
            _world.SlotOf(mine, Bread).Amount.Should().BeLessThan(40);
        }

        /// <summary>
        /// Leaves a cell one open neighbour and paints that neighbour so the step onto the cell is forbidden:
        /// reachable on paper, unreachable in fact. This is what a one-way road drawn past a doorstep does,
        /// and it is the shape of the failure that had agents standing still next to a door.
        /// </summary>
        private void SealApproach(int2 cell)
        {
            _world.Tick();

            foreach (int2 side in new[] { new int2(-1, 0), new int2(1, 0), new int2(0, 1) })
            {
                if (_world.Map.IsPassable(cell + side))
                {
                    _world.Enqueue(GridEdit.CostDelta(cell + side, CellData.BLOCKED));
                }
            }

            // The only way in would be from the south, and that cell may not be left northwards.
            _world.Enqueue(GridEdit.SetExits(cell + new int2(0, -1), OneWay(Direction.South)));
            _world.Tick();
        }

        /// <summary>
        /// A stall is a guess worth five seconds; no route is a fact worth half of one. The difference matters
        /// because the task is handed straight back afterwards, so a slow answer here is an agent that spends
        /// its life in a five-second loop, standing still, looking permanently frozen.
        /// </summary>
        [Test]
        public void AWalkWithNoRouteIsGivenUpOnAtOnceRatherThanWaitedOut()
        {
            var goal = new int2(10, 10);
            SealApproach(goal);

            Entity agent = _world.CreateIdleAgent(Centre(new int2(10, 4)));
            _world.StepsOf(agent).Add(TaskStep.GoTo(goal));

            // Two seconds: well inside the stall window, so only a positive "there is no way in" ends this.
            _world.TickFrames(20, 0.1f);

            _world.StepsOf(agent).IsEmpty.Should().BeTrue("the field says there is no way in, so there is");
            _world.IsWalking(agent).Should().BeFalse();
        }

        /// <summary>
        /// Dropping the task is only half of it. The other half is not being handed the same impossible walk
        /// on the next tick - which is what turned one sealed door into an agent that never did anything
        /// again, and never moved while not doing it.
        /// </summary>
        [Test]
        public void AnIdleAgentIsNotSentToAShelterItCannotReach()
        {
            Entity sealedHut = _world.CreateShelter(new int2(10, 10), capacity: 4);
            Entity openHut = _world.CreateShelter(new int2(20, 10), capacity: 4);
            SealApproach(new int2(10, 9));

            // Nearer to the sealed hut, so straight-line distance alone would send it there forever.
            Entity agent = _world.CreateIdleAgent(Centre(new int2(12, 5)));

            _world.TickFrames(400, 0.05f);

            _world.IsInside(agent).Should().BeTrue("there is a hut it can walk into, and it should be in it");
            _world.Entities.GetComponentData<InsideBuilding>(agent).Building.Should().Be(openHut);
            _world.InteriorOf(sealedHut).Claimed.Should().Be(0, "and the bed it could not reach is not held");
        }

        /// <summary>
        /// The same rule on the hauling side: one wasted attempt is the price of finding out, and there is no
        /// second one. The first try is always allowed - that is what builds the field that answers the
        /// question - so what this really asserts is that the answer is used once it exists.
        /// </summary>
        [Test]
        public void AHaulToAnUnreachableBuildingIsNotHandedOutForever()
        {
            _world.CreateSource(new int2(4, 10), Bread, amount: 100, capacity: 100);
            Entity warehouse = _world.CreateWarehouse(new int2(14, 10), Bread, capacity: 100);
            SealApproach(new int2(14, 9));

            Entity hauler = _world.CreateIdleAgent(Centre(new int2(8, 6)), carryCapacity: 10);

            _world.TickFrames(300, 0.1f);

            _world.HasOrder(hauler).Should()
                  .BeFalse("nobody should still be walking a delivery that cannot be delivered");
            _world.SlotOf(warehouse, Bread).ReservedIn.Should()
                  .Be(0, "and the room held for it is given back rather than promised forever");
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
