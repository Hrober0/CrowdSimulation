using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// A 256x256 map - twice the width of a flow field window - so a trip across it cannot be served by the
    /// goal's field alone and has to go gate by gate.
    /// </summary>
    public class LongRangeMovementTests
    {
        private const int MAP_SIZE = 256;

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld(MAP_SIZE);

        [TearDown]
        public void Teardown() => _world.Dispose();

        private PathFollow PathOf(Entity agent) => _world.Entities.GetComponentData<PathFollow>(agent);

        [Test]
        public void AnAgentFarFromItsGoal_SteersAtAGateInstead()
        {
            int2 goal = new(120, 0);
            Entity agent = _world.CreateAgent(GridCoords.CellCenter(new int2(-120, 0)), goal);

            _world.TickFrame(0.1f);

            PathFollow path = PathOf(agent);
            path.WaypointCell.Should().NotBe(goal, "the goal's field does not reach this far");
            _world.Entities.GetBuffer<PathRoute>(agent).Length.Should()
                  .BeGreaterThan(0, "it has a chain of gates to cross");
        }

        [Test]
        public void AnAgentNearItsGoal_IgnoresTheGateGraphEntirely()
        {
            int2 goal = new(10, 0);
            Entity agent = _world.CreateAgent(GridCoords.CellCenter(new int2(-10, 0)), goal);

            _world.TickFrame(0.1f);

            PathOf(agent).WaypointCell.Should().Be(goal);
            _world.Entities.GetBuffer<PathRoute>(agent).Length.Should().Be(0);
        }

        [Test]
        public void TheWaypointIsOnTheFarSideOfTheGate()
        {
            Entity agent = _world.CreateAgent(GridCoords.CellCenter(new int2(-120, 0)), new int2(120, 0));

            _world.TickFrame(0.1f);

            int2 agentCell = GridCoords.CellOf(_world.AgentOf(agent).Position);
            int agentChunk = _world.Map.ChunkCoordOf(agentCell).y * _world.Map.ChunkCount.x
                             + _world.Map.ChunkCoordOf(agentCell).x;

            int2 waypointChunk = _world.Map.ChunkCoordOf(PathOf(agent).WaypointCell);
            int waypoint = waypointChunk.y * _world.Map.ChunkCount.x + waypointChunk.x;

            waypoint.Should().NotBe(agentChunk, "walking to the waypoint has to take the agent through the gate");
        }

        [Test]
        public void AnAgentCrossesTheWholeMapAndArrives()
        {
            int2 goal = new(120, 0);
            Entity agent = _world.CreateAgent(GridCoords.CellCenter(new int2(-120, 0)), goal, maxSpeed: 8f);

            _world.TickFrames(1200, 0.05f);

            _world.HasArrived(agent).Should()
                  .BeTrue($"it should have crossed by now (it got as far as {_world.AgentOf(agent).Position})");
        }

        [Test]
        public void TheRouteIsRecomputedWhenTheGoalChanges()
        {
            Entity agent = _world.CreateAgent(GridCoords.CellCenter(new int2(-120, 0)), new int2(120, 0));
            _world.TickFrame(0.1f);
            int2 firstWaypoint = PathOf(agent).WaypointCell;

            PathFollow path = PathOf(agent);
            path.GoalCell = new int2(-120, 120);
            _world.Entities.SetComponentData(agent, path);

            _world.TickFrame(0.1f);

            PathOf(agent).WaypointCell.Should().NotBe(firstWaypoint);
            PathOf(agent).RoutedGoal.Should().Be(new int2(-120, 120));
        }
    }
}
