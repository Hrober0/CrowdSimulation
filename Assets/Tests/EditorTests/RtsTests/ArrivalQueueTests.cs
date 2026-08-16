using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Several agents wanting the same cell (design §8). The cell is usually a doorway - a hauler standing on
    /// a doorstep through a pickup, a resident stepping inside - and the failure it used to produce was a scrum
    /// that let nobody in at all.
    /// </summary>
    public class ArrivalQueueTests
    {
        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 CentreOf(int2 cell) => GridCoords.CellCenter(cell);

        [Test]
        public void OnlyTheNearestAgentApproachesAContestedCell()
        {
            var destination = new int2(0, 0);

            Entity first = _world.CreateAgent(CentreOf(new int2(2, 0)), destination);
            Entity second = _world.CreateAgent(CentreOf(new int2(3, 0)), destination);
            Entity third = _world.CreateAgent(CentreOf(new int2(4, 0)), destination);

            _world.TickFrames(3);

            _world.FollowOf(first).Holding.Should().BeFalse("the front of the queue is the one that walks in");
            _world.FollowOf(second).Holding.Should().BeTrue("and everyone behind it waits its turn");
            _world.FollowOf(third).Holding.Should().BeTrue();
        }

        [Test]
        public void AnAgentWithACellToItselfIsNeverHeld()
        {
            Entity alone = _world.CreateAgent(CentreOf(new int2(3, 0)), new int2(0, 0));

            _world.TickFrames(3);

            _world.FollowOf(alone).Holding.Should().BeFalse();
        }

        /// <summary>
        /// The queue has to drain, not merely form. Nothing is reserved, so the only thing that promotes the
        /// next agent is the one in front stopping being the nearest - if that did not happen the whole line
        /// would stand still forever, which is the bug this replaced rather than a fix for it.
        /// </summary>
        [Test]
        public void AQueueLetsEveryAgentReachTheCellInTurn()
        {
            var destination = new int2(0, 0);

            Entity[] agents =
            {
                _world.CreateAgent(CentreOf(new int2(2, 0)), destination),
                _world.CreateAgent(CentreOf(new int2(3, 0)), destination),
                _world.CreateAgent(CentreOf(new int2(4, 0)), destination),
            };

            var reached = new bool[agents.Length];

            for (int frame = 0; frame < 400; frame++)
            {
                _world.TickFrame(0.05f);

                for (int i = 0; i < agents.Length; i++)
                {
                    float distance = math.distance(_world.AgentOf(agents[i]).Position, CentreOf(destination));
                    reached[i] |= distance <= 1.2f;
                }
            }

            reached.Should().AllBeEquivalentTo(true, "every agent in the queue has to get its turn");
        }

        /// <summary>
        /// The tail of a long queue is where the places stop being handed out by hand: rank four and beyond are
        /// further from the destination than the radius the queue *forms* in, so a ranking that only looked at
        /// agents inside that radius stopped maintaining them - and an agent still holding a place nobody is
        /// maintaining waits for a turn that can never come, exempt from the watchdog because it thinks it is
        /// queueing. That is what "agents stood around far from the door and never delivered" was.
        /// </summary>
        [Test]
        public void ALongQueueDrainsInsteadOfStrandingItsTail()
        {
            var destination = new int2(0, 0);

            var agents = new Entity[8];
            for (int i = 0; i < agents.Length; i++)
            {
                agents[i] = _world.CreateAgent(CentreOf(new int2(2 + i, 0)), destination);
            }

            var reached = new bool[agents.Length];

            for (int frame = 0; frame < 1200; frame++)
            {
                _world.TickFrame(0.05f);

                for (int i = 0; i < agents.Length; i++)
                {
                    float distance = math.distance(_world.AgentOf(agents[i]).Position, CentreOf(destination));
                    reached[i] |= distance <= 1.2f;
                }
            }

            reached.Should().AllBeEquivalentTo(true, "a queue eight deep still has to be a queue");
        }

        /// <summary>
        /// Rank is the cost of the way in, so without something to weigh against it a queue is a race that the
        /// nearest agent wins every time it is run: a trickle of arrivals nearer than whoever is waiting keeps
        /// taking the front, and the agent already there is overtaken forever. It is never *stuck* - it is
        /// holding a place, politely, in a line that never reaches it.
        /// </summary>
        [Test]
        public void AnAgentThatHasBeenWaitingIsNotOvertakenByOneArrivingNearer()
        {
            var destination = new int2(10, 10);

            // Standing on the cell for the whole test, so nobody can ever arrive and the queue never drains.
            Entity occupant = _world.CreateIdleAgent(CentreOf(destination));
            _world.StepsOf(occupant).Add(TaskStep.GoTo(destination));
            _world.StepsOf(occupant).Add(TaskStep.Interact(Entity.Null, 60f));

            Entity waiting = _world.CreateIdleAgent(CentreOf(new int2(14, 10)));
            _world.StepsOf(waiting).Add(TaskStep.GoTo(destination));

            _world.TickFrames(60, 0.1f);

            // Six seconds later, and a good deal nearer than the one that has been waiting all that time.
            Entity newcomer = _world.CreateIdleAgent(CentreOf(destination) + new float2(1.2f, 0f));
            _world.StepsOf(newcomer).Add(TaskStep.GoTo(destination));

            _world.TickFrames(10, 0.1f);

            _world.FollowOf(waiting).HoldDistance.Should()
                  .BeLessThan(_world.FollowOf(newcomer).HoldDistance,
                              "a queue is a queue - the wait already served is what a newcomer has to beat");
        }

        /// <summary>
        /// A held agent is exempt from the watchdog, which is only safe because the agent at the front is not.
        /// Here the front can never arrive - the goal is walled in - so the watchdog has to drop *it* and let
        /// the queue behind it move up, rather than the whole line waiting on it forever.
        /// </summary>
        [Test]
        public void AQueueBehindAnAgentThatCanNeverArriveStillMovesOn()
        {
            var goal = new int2(20, 20);
            foreach (int2 offset in new[] { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) })
            {
                _world.Enqueue(GridEdit.CostDelta(goal + offset, CellData.BLOCKED));
            }

            Entity front = _world.CreateIdleAgent(CentreOf(new int2(18, 20)));
            Entity behind = _world.CreateIdleAgent(CentreOf(new int2(17, 20)));

            _world.StepsOf(front).Add(TaskStep.GoTo(goal));
            _world.StepsOf(behind).Add(TaskStep.GoTo(goal));

            // Longer than the watchdog's stall window, so the give-up has happened by the end of it.
            _world.TickFrames(120, 0.1f);

            _world.StepsOf(front).IsEmpty.Should()
                  .BeTrue("the front of a queue is still watched, so a hopeless walk is still given up on");
            _world.FollowOf(behind).Holding.Should()
                  .BeFalse("and with it gone the one behind is free to try");
        }
    }
}
