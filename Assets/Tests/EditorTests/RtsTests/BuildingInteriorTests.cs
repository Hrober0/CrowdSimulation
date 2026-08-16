using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Entrances, interiors and idle claiming (design §6). A 64x64 map, so one flow field window covers all
    /// of it and nothing here is about the long-range tier.
    /// </summary>
    public class BuildingInteriorTests
    {
        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 CentreOf(int2 cell) => GridCoords.CellCenter(cell);

        [Test]
        public void AnEntranceCellIsOutsideTheFootprintAndWalkable()
        {
            Entity building = _world.CreateBuilding(new int2(4, 4), GridRotation.None, int2.zero);
            _world.AddEntrance(building, int2.zero, Direction.South);

            _world.Tick();

            _world.Map.IsPassable(new int2(4, 4)).Should().BeFalse("the footprint cell itself is blocked");

            var doorstep = new int2(4, 3);
            _world.Map.IsPassable(doorstep).Should().BeTrue("an entrance nobody can stand on is not an entrance");
            _world.Map.GetCell(doorstep).Has(CellFlags.Entrance).Should().BeTrue();
            _world.Map.GetCell(doorstep).Has(CellFlags.NoIdle).Should()
                  .BeTrue("a doorway agents may loiter in is a doorway that gets blocked");
        }

        [Test]
        public void RotatingABuildingRotatesItsEntrance()
        {
            Entity building = _world.CreateBuilding(new int2(4, 4), GridRotation.Clockwise90, int2.zero);
            _world.AddEntrance(building, int2.zero, Direction.South);

            _world.Tick();

            // South turned clockwise is west, so the doorstep moves round with the building.
            _world.Map.GetCell(new int2(3, 4)).Has(CellFlags.Entrance).Should().BeTrue();
            _world.Map.GetCell(new int2(4, 3)).Has(CellFlags.Entrance).Should().BeFalse();
        }

        [Test]
        public void DemolishingABuildingGivesBackItsEntranceCell()
        {
            Entity building = _world.CreateBuilding(new int2(4, 4), GridRotation.None, int2.zero);
            _world.AddEntrance(building, int2.zero, Direction.South);
            _world.Tick();

            _world.Entities.DestroyEntity(building);
            _world.Tick();

            _world.Map.GetCell(new int2(4, 3)).Flags.Should().Be(CellFlags.None);
            _world.Map.IsPassable(new int2(4, 4)).Should().BeTrue("and the footprint comes back too");
        }

        [Test]
        public void AnIdleAgentClaimsASlotBeforeItStartsWalking()
        {
            _world.CreateShelter(new int2(10, 10), capacity: 1);
            Entity agent = _world.CreateIdleAgent(CentreOf(new int2(2, 2)));

            _world.TickFrame(0.1f);

            _world.HasClaim(agent).Should().BeTrue("claim before approach is the admission control of §6");
            _world.InteriorOf(_world.Entities.GetComponentData<InteriorClaim>(agent).Building)
                  .Claimed.Should().Be(1);
        }

        [Test]
        public void AShelterNeverPromisesMoreSlotsThanItHas()
        {
            _world.CreateShelter(new int2(10, 10), capacity: 2);

            Entity[] agents =
            {
                _world.CreateIdleAgent(CentreOf(new int2(2, 2))),
                _world.CreateIdleAgent(CentreOf(new int2(3, 2))),
                _world.CreateIdleAgent(CentreOf(new int2(4, 2))),
                _world.CreateIdleAgent(CentreOf(new int2(5, 2))),
            };

            _world.TickFrames(3);

            int claimed = 0;
            foreach (Entity agent in agents)
            {
                if (_world.HasClaim(agent))
                {
                    claimed++;
                }
            }

            claimed.Should().Be(2, "two beds, so two of the four are told to go and two are not");
        }

        [Test]
        public void AnAgentWalksToTheShelterAndGoesInside()
        {
            Entity shelter = _world.CreateShelter(new int2(8, 8), capacity: 4);
            Entity agent = _world.CreateIdleAgent(CentreOf(new int2(2, 2)));

            _world.TickFrames(400, 0.05f);

            _world.IsInside(agent).Should().BeTrue();
            _world.Entities.GetComponentData<InsideBuilding>(agent).Building.Should().Be(shelter);
            _world.InteriorOf(shelter).Occupied.Should().Be(1);
        }

        [Test]
        public void AnAgentInsideABuildingCostsNothingPerFrame()
        {
            _world.CreateShelter(new int2(8, 8), capacity: 4);
            Entity agent = _world.CreateIdleAgent(CentreOf(new int2(2, 2)));

            _world.TickFrames(400, 0.05f);

            _world.IsInside(agent).Should().BeTrue("the rest of this test is about what that costs");

            _world.Entities.IsComponentEnabled<AgentMove>(agent).Should()
                  .BeFalse("no spatial hash entry, no avoidance neighbour query, no integration");
            _world.IsWalking(agent).Should().BeFalse("no path and no steering");
            _world.IsVisible(agent).Should().BeFalse("and the GameObject goes back to the pool");
        }

        [Test]
        public void LeavingABuildingPutsTheAgentBackOnItsDoorstep()
        {
            Entity shelter = _world.CreateShelter(new int2(8, 8), capacity: 4);
            Entity agent = _world.CreateIdleAgent(CentreOf(new int2(2, 2)));
            _world.TickFrames(400, 0.05f);
            _world.IsInside(agent).Should().BeTrue();

            // One frame only. Walking out through the door takes time (§15), but the agent is put back on the
            // map at the *start* of it - visible, standing on its doorstep, and no longer an occupant - so
            // everything below is settled by the first frame. Only the step retires later.
            var doorstep = new int2(8, 7);
            _world.StepsOf(agent).Add(TaskStep.Exit(shelter, doorstep));
            _world.TickFrame(0.1f);

            _world.IsInside(agent).Should().BeFalse();
            _world.IsVisible(agent).Should().BeTrue();
            _world.HasClaim(agent).Should().BeFalse("the slot is given back on the way out");
            _world.AgentOf(agent).Position.Should().Be(CentreOf(doorstep));

            Interior interior = _world.InteriorOf(shelter);
            interior.Occupied.Should().Be(0);
            interior.Claimed.Should().Be(0);
        }

        [Test]
        public void WithNoShelterAnywhere_AnAgentStandingInADoorwayMovesOffIt()
        {
            Entity building = _world.CreateBuilding(new int2(4, 4), GridRotation.None, int2.zero);
            _world.AddEntrance(building, int2.zero, Direction.South);
            _world.Tick();

            var doorstep = new int2(4, 3);
            Entity agent = _world.CreateIdleAgent(CentreOf(doorstep));

            _world.TickFrames(200, 0.05f);

            GridCoords.CellOf(_world.AgentOf(agent).Position).Should()
                      .NotBe(doorstep, "roads and doorways carry NoIdle and idle agents must clear them");
        }

        /// <summary>
        /// A map with roads painted over most of it, which is what a player does. A road carries
        /// <see cref="CellFlags.NoIdle"/>, so "somewhere an idle agent may stand" becomes a handful of cells
        /// that every idle agent on the map is sent to at once.
        ///
        /// The property that has to hold is quiescence: agents with nothing to do must end up doing nothing.
        /// Whether they all fit on the island is not the question - the ones that do not have to *stop*, not
        /// keep being handed somewhere new to be.
        /// </summary>
        [Test]
        public void IdleAgentsOnARoadedMapSettleInsteadOfCirclingForever()
        {
            // Everything is NoIdle except a 3x3 island at (6,0) - far too small for the crowd sent to it.
            for (int y = -12; y <= 12; y++)
            {
                for (int x = -12; x <= 12; x++)
                {
                    if (math.abs(x - 6) > 1 || math.abs(y) > 1)
                    {
                        _world.Enqueue(GridEdit.AddFlags(new int2(x, y), CellFlags.NoIdle));
                    }
                }
            }

            _world.Tick();

            var agents = new Entity[10];
            for (int i = 0; i < agents.Length; i++)
            {
                agents[i] = _world.CreateIdleAgent(CentreOf(new int2(i % 5, i / 5)));
            }

            _world.TickFrames(600, 0.05f);

            foreach (Entity agent in agents)
            {
                _world.StepsOf(agent).IsEmpty.Should()
                      .BeTrue("half a minute is long enough for an agent with nothing to do to settle");
            }

            // And they stay settled: an agent being handed a new place to stand every few seconds looks
            // exactly like one that has settled, until you watch it for another five.
            _world.TickFrames(200, 0.05f);

            foreach (Entity agent in agents)
            {
                _world.StepsOf(agent).IsEmpty.Should()
                      .BeTrue("nothing should still be sending idle agents somewhere");
            }
        }

        [Test]
        public void AnIdleAgentOnAHarmlessCellIsLeftAlone()
        {
            Entity agent = _world.CreateIdleAgent(CentreOf(new int2(20, 20)));
            float2 start = _world.AgentOf(agent).Position;

            _world.TickFrames(10);

            _world.AgentOf(agent).Position.Should().Be(start, "there is nowhere it needs to be and nowhere it is in the way");
            _world.StepsOf(agent).IsEmpty.Should().BeTrue();
        }

        [Test]
        public void TheStepMachineRunsATaskThroughToItsEnd()
        {
            Entity agent = _world.CreateIdleAgent(CentreOf(new int2(2, 2)));

            DynamicBuffer<TaskStep> steps = _world.StepsOf(agent);
            steps.Add(TaskStep.GoTo(new int2(6, 2)));
            steps.Add(TaskStep.Interact(Entity.Null, 0.5f));
            steps.Add(TaskStep.GoTo(new int2(2, 2)));

            _world.TickFrames(400, 0.05f);

            _world.StepsOf(agent).IsEmpty.Should().BeTrue("every step ran and was retired");
            math.distance(_world.AgentOf(agent).Position, CentreOf(new int2(2, 2))).Should().BeLessThan(0.6f);
        }

        [Test]
        public void AnInteractStepHoldsTheAgentStillForItsDuration()
        {
            Entity agent = _world.CreateIdleAgent(CentreOf(new int2(20, 20)));
            _world.StepsOf(agent).Add(TaskStep.Interact(Entity.Null, 1f));

            _world.TickFrames(5, 0.1f);

            _world.StepsOf(agent).IsEmpty.Should().BeFalse("half a second in, it is still working");

            _world.TickFrames(8, 0.1f);

            _world.StepsOf(agent).IsEmpty.Should().BeTrue();
        }
    }
}
