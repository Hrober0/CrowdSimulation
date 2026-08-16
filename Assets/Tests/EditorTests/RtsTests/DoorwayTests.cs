using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Doorways as a resource held for a known time (design §15). Going in and coming out used to be
    /// instantaneous, which made an agent leaving a building materialise inside whatever was standing outside
    /// and left the queue of §8 unable to see an occupied step at all.
    /// </summary>
    public class DoorwayTests
    {
        private static readonly ItemId Bread = new(1);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 Centre(int2 cell) => GridCoords.CellCenter(cell);

        /// <summary>Frames of 0.1s that comfortably outlast one door transition.</summary>
        private const int DOOR_FRAMES = 6;

        [Test]
        public void GoingInTakesTime_AndTheAgentIsStillOnTheMapForIt()
        {
            var door = new int2(10, 9);
            Entity shelter = _world.CreateShelter(new int2(10, 10), capacity: 1);

            Entity agent = _world.CreateIdleAgent(Centre(door));
            _world.StepsOf(agent).Add(TaskStep.Enter(shelter, door));

            _world.TickFrame(0.1f);

            _world.IsInDoorway(agent).Should().BeTrue("the door was free, so it started going through");
            _world.IsInside(agent).Should().BeFalse("but a threshold is not crossed in a frame any more");
            _world.IsVisible(agent).Should()
                  .BeTrue("half in and half out is on the map: something the crowd can see and react to");
            _world.InteriorOf(shelter).Occupied.Should().Be(0, "it is not an occupant until it is inside");

            _world.TickFrames(DOOR_FRAMES, 0.1f);

            _world.IsInDoorway(agent).Should().BeFalse();
            _world.IsInside(agent).Should().BeTrue();
            _world.IsVisible(agent).Should().BeFalse();
            _world.InteriorOf(shelter).Occupied.Should().Be(1);
            _world.StepsOf(agent).IsEmpty.Should().BeTrue("and the step is retired by the transition, not before it");
        }

        [Test]
        public void ComingOutPutsTheAgentOnItsDoorstepBeforeTheAnimationHasFinished()
        {
            var door = new int2(10, 9);

            // A building with room, deliberately not a shelter, so nothing sends the agent back inside the
            // moment it is out and the only thing this test can be measuring is the door.
            Entity building = _world.CreateBuilding(new int2(10, 10), GridRotation.None, int2.zero);
            _world.AddEntrance(building, int2.zero, Direction.South);
            _world.Entities.AddComponentData(building, new Interior { Capacity = 1, Occupied = 1, Claimed = 1 });

            Entity agent = _world.CreateResident(building, door);
            _world.StepsOf(agent).Add(TaskStep.Exit(building, door));

            _world.TickFrame(0.1f);

            _world.IsInDoorway(agent).Should().BeTrue();
            _world.IsInside(agent).Should()
                  .BeFalse("an agent on its way out is out: visible and avoidable for the whole of it");
            _world.IsVisible(agent).Should().BeTrue();
            _world.AgentOf(agent).Position.Should().Be(Centre(door));
            _world.InteriorOf(building).Occupied.Should().Be(0, "the slot is given back as it leaves");

            _world.TickFrames(DOOR_FRAMES, 0.1f);

            _world.IsInDoorway(agent).Should().BeFalse();

            // Not "the buffer is empty": a doorstep is NoIdle, so the first thing the agent is told once it
            // has nothing to do is to step off it (§6). What has to be gone is the Exit itself.
            HasStep(agent, TaskStepKind.Exit).Should()
                  .BeFalse("the step is retired by the transition, and not before it");
        }

        private bool HasStep(Entity agent, TaskStepKind kind)
        {
            foreach (TaskStep step in _world.StepsOf(agent))
            {
                if (step.Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void OnlyOneAgentIsInADoorwayAtATime()
        {
            var door = new int2(10, 9);
            Entity shelter = _world.CreateShelter(new int2(10, 10), capacity: 2);

            Entity first = _world.CreateIdleAgent(Centre(door));
            Entity second = _world.CreateIdleAgent(Centre(new int2(10, 8)));

            _world.StepsOf(first).Add(TaskStep.Enter(shelter, door));
            _world.StepsOf(second).Add(TaskStep.Enter(shelter, door));

            _world.TickFrame(0.1f);

            (_world.IsInDoorway(first) ^ _world.IsInDoorway(second)).Should()
                .BeTrue("a door is one agent wide, whatever the room behind it holds");

            // Both get through: the one that waited is let in as soon as the first is clear.
            _world.TickFrames(2 * DOOR_FRAMES + 2, 0.1f);

            _world.IsInside(first).Should().BeTrue();
            _world.IsInside(second).Should().BeTrue();
        }

        /// <summary>
        /// §15: an agent coming out has nowhere else to be and blocking it stalls everything the building is
        /// doing, while one going in can wait a turn. The entering agent is created *first* on purpose, so that
        /// entity order cannot be what decides this.
        /// </summary>
        [Test]
        public void ComingOutGoesBeforeGoingIn()
        {
            var door = new int2(10, 9);
            Entity shelter = _world.CreateShelter(new int2(10, 10), capacity: 2);
            _world.Entities.SetComponentData(shelter, new Interior { Capacity = 2, Occupied = 1, Claimed = 1 });

            Entity entering = _world.CreateIdleAgent(Centre(new int2(10, 8)));
            _world.StepsOf(entering).Add(TaskStep.Enter(shelter, door));

            Entity leaving = _world.CreateResident(shelter, door);
            _world.StepsOf(leaving).Add(TaskStep.Exit(shelter, door));

            _world.TickFrame(0.1f);

            _world.IsInDoorway(leaving).Should().BeTrue("the way out wins the doorway");
            _world.DoorOf(leaving).Kind.Should().Be(DoorUseKind.Exit);
            _world.IsInDoorway(entering).Should().BeFalse("and the way in waits one turn");
        }

        /// <summary>
        /// The hole §15 left open: an agent standing on a destination is not walking to it, so the queue could
        /// not see it and the next agent walked in on top of it. Any reason to be standing there counts - a
        /// pickup, a door transition, or a step yet to start.
        /// </summary>
        [Test]
        public void AnAgentStandingOnADestinationIsQueuedForRatherThanWalkedInto()
        {
            var cell = new int2(10, 10);

            Entity standing = _world.CreateIdleAgent(Centre(cell));
            _world.StepsOf(standing).Add(TaskStep.GoTo(cell));
            _world.StepsOf(standing).Add(TaskStep.Interact(Entity.Null, 5f));

            Entity walking = _world.CreateIdleAgent(Centre(new int2(13, 10)));
            _world.StepsOf(walking).Add(TaskStep.GoTo(cell));

            _world.TickFrames(4);

            _world.IsWalking(standing).Should().BeFalse("it is there, and staying there for a while");
            _world.FollowOf(walking).Holding.Should()
                  .BeTrue("so the cell is taken, and the queue forms behind it instead of on it");
        }

        /// <summary>
        /// The end-to-end reason all of this exists: one door, more haulers than the old cap of four, and
        /// everything gets delivered. A queue that stalls anywhere in the line shows up here as stock left on
        /// the shelf.
        /// </summary>
        [Test]
        public void EightHaulersDeliverThroughOneDoor()
        {
            Entity mine = _world.CreateSource(new int2(4, 10), Bread, amount: 200, capacity: 200);
            Entity warehouse = _world.CreateWarehouse(new int2(20, 10), Bread, capacity: 200);

            for (int i = 0; i < 8; i++)
            {
                _world.CreateIdleAgent(Centre(new int2(10 + i % 4, 14 + i / 4)), carryCapacity: 10);
            }

            _world.TickFrames(1200, 0.05f);

            // Eight haulers over a minute cover the sixteen cells between the two doors several times each,
            // so this is a low bar on purpose: it is a stall detector, not a throughput benchmark.
            _world.SlotOf(warehouse, Bread).Amount.Should()
                  .BeGreaterThan(120, "every hauler has to keep getting its turn at the door");

            _world.SlotOf(mine, Bread).Amount.Should().BeLessThan(80);
        }
    }
}
