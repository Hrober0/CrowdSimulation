using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// A 64x64 map, so one flow field window covers all of it and every test is about the local tier.
    /// </summary>
    public class AgentMovementTests
    {
        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 CentreOf(int2 cell) => GridCoords.CellCenter(cell);

        [Test]
        public void AnAgentWithNoFieldYet_StaysPut()
        {
            Entity agent = _world.CreateAgent(CentreOf(new int2(10, 0)), new int2(0, 0));
            float2 start = _world.AgentOf(agent).Position;

            _world.TickFrame(0.1f);

            _world.AgentOf(agent).Position.Should().Be(start,
                "the field is only asked for on this frame; it is served on the next");
        }

        [Test]
        public void OnceTheFieldIsThere_TheAgentWalksTowardsTheGoal()
        {
            Entity agent = _world.CreateAgent(CentreOf(new int2(10, 0)), new int2(0, 0));

            _world.TickFrames(5);

            float2 position = _world.AgentOf(agent).Position;
            position.x.Should().BeLessThan(10f, "the goal is to the west");
            math.abs(position.y).Should().BeLessThan(1f, "and straight ahead, so there is no reason to drift");
        }

        [Test]
        public void TheAgentArrivesAndStops()
        {
            Entity agent = _world.CreateAgent(CentreOf(new int2(6, 0)), new int2(0, 0));

            _world.TickFrames(60);

            _world.HasArrived(agent).Should().BeTrue();
            _world.IsWalking(agent).Should().BeFalse("an arrived agent leaves the movement systems entirely");
            _world.AgentOf(agent).Velocity.Should().Be(float2.zero);

            math.distance(_world.AgentOf(agent).Position, CentreOf(new int2(0, 0)))
                .Should().BeLessThan(0.5f);
        }

        [Test]
        public void AnArrivedAgentStaysWhereItStopped()
        {
            Entity agent = _world.CreateAgent(CentreOf(new int2(3, 0)), new int2(0, 0));
            _world.TickFrames(60);
            float2 restingPlace = _world.AgentOf(agent).Position;

            _world.TickFrames(10);

            _world.AgentOf(agent).Position.Should().Be(restingPlace);
        }

        [Test]
        public void AnAgentNeverStepsIntoABlockedCell()
        {
            // A wall right next to the agent, with the goal straight through it.
            for (int y = -32; y <= 31; y++)
            {
                _world.Enqueue(GridEdit.CostDelta(new int2(5, y), CellData.BLOCKED));
            }

            Entity agent = _world.CreateAgent(CentreOf(new int2(8, 0)), new int2(0, 0));

            for (int i = 0; i < 60; i++)
            {
                _world.TickFrame(0.1f);

                int2 cell = GridCoords.CellOf(_world.AgentOf(agent).Position);
                _world.Map.IsPassable(cell).Should().BeTrue($"the agent must never stand in a blocked cell (frame {i})");
            }
        }

        [Test]
        public void AnAgentWalksRoundAWallToReachItsGoal()
        {
            // A wall from the south edge up to y = 2, so the way round is over the top.
            for (int y = -32; y <= 2; y++)
            {
                _world.Enqueue(GridEdit.CostDelta(new int2(5, y), CellData.BLOCKED));
            }

            Entity agent = _world.CreateAgent(CentreOf(new int2(8, 0)), new int2(0, 0));

            _world.TickFrames(400, 0.05f);

            _world.AgentOf(agent).Position.x.Should().BeLessThan(5f, "it got past the wall");
            _world.HasArrived(agent).Should().BeTrue();
        }

        [Test]
        public void AnAgentWalksAroundATree()
        {
            _world.CreateCellObject(new int2(5, 0), CellData.BLOCKED);
            Entity agent = _world.CreateAgent(CentreOf(new int2(8, 0)), new int2(0, 0));

            _world.TickFrames(300, 0.05f);

            _world.HasArrived(agent).Should().BeTrue();
        }

        [Test]
        public void AnAgentWithAnUnreachableGoal_GoesNowhere()
        {
            int2 goal = new(20, 20);
            foreach (int2 offset in new[] { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) })
            {
                _world.Enqueue(GridEdit.CostDelta(goal + offset, CellData.BLOCKED));
            }

            Entity agent = _world.CreateAgent(CentreOf(new int2(0, 0)), goal);
            float2 start = _world.AgentOf(agent).Position;

            _world.TickFrames(20);

            _world.AgentOf(agent).Position.Should().Be(start, "there is no gradient to follow");
            _world.HasArrived(agent).Should().BeFalse();
        }

        [Test]
        public void TwoAgentsHeadingTheSameWay_ShareOneField()
        {
            _world.CreateAgent(CentreOf(new int2(10, 0)), new int2(0, 0));
            _world.CreateAgent(CentreOf(new int2(10, 3)), new int2(0, 0));

            _world.TickFrames(3);

            _world.Fields.TryGetSlot(new int2(0, 0), out int _).Should().BeTrue();
        }

        [Test]
        public void CrowdedAgentsPushPastEachOtherWithoutOverlapping()
        {
            Entity first = _world.CreateAgent(CentreOf(new int2(6, 0)), new int2(0, 0));
            Entity second = _world.CreateAgent(CentreOf(new int2(7, 0)), new int2(0, 0));

            for (int i = 0; i < 80; i++)
            {
                _world.TickFrame(0.05f);

                AgentMove a = _world.AgentOf(first);
                AgentMove b = _world.AgentOf(second);
                math.distance(a.Position, b.Position).Should()
                    .BeGreaterThan(0.3f, $"avoidance has to keep them apart (frame {i})");
            }
        }

        [Test]
        public void AOneWayRoadIsWalkedInItsDirectionOnly()
        {
            // Nothing may step west across x = 5.
            for (int y = -32; y <= 31; y++)
            {
                _world.Enqueue(GridEdit.SetExits(
                    new int2(5, y),
                    DirectionUtils.Forbid(DirectionUtils.ALL_EXITS, Direction.West)
                ));
            }

            Entity blocked = _world.CreateAgent(CentreOf(new int2(8, 0)), new int2(0, 0));
            Entity allowed = _world.CreateAgent(CentreOf(new int2(0, 10)), new int2(8, 10));

            _world.TickFrames(400, 0.05f);

            _world.HasArrived(blocked).Should().BeFalse("westwards across that line is not allowed");
            _world.AgentOf(blocked).Position.x.Should().BeGreaterThan(5f);

            _world.HasArrived(allowed).Should()
                  .BeTrue($"eastwards is fine (it got as far as {_world.AgentOf(allowed).Position})");
        }
    }
}
