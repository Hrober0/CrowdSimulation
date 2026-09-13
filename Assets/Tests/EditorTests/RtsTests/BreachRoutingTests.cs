using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Routing that reads the breach cost (design §14.4).
    ///
    /// The map here is a wall of impassable terrain with one building plugging the only gap in it. That shape
    /// is the whole test: the plug is the *only* way through, so "is there a route" has exactly two answers
    /// and which one you get depends entirely on whether the seeker could knock the plug down.
    ///
    /// A field routing through a structure does not mean a body walks through one. The integrator still
    /// clamps agents out of blocked cells, so what the field says is "the cheapest way in is here" - the
    /// walking through happens after the wall comes down.
    /// </summary>
    public class BreachRoutingTests
    {
        private const byte Ours = 0;
        private const byte Theirs = 1;

        /// <summary>The one cell of the wall that is a building rather than rock.</summary>
        private static readonly int2 Plug = new(10, 10);

        private static readonly int2 Beyond = new(20, 10);

        private static readonly int2 ThisSide = new(2, 10);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup()
        {
            _world = new RtsTestWorld();

            // A wall from the bottom of the map to the top, with one cell missing.
            _world.BlockCells(new int2(Plug.x, -32), new int2(0, 1), 42);
            _world.BlockCells(new int2(Plug.x, Plug.y + 1), new int2(0, 1), 21);
            _world.Tick();
        }

        [TearDown]
        public void Teardown() => _world.Dispose();

        private void PlugTheGap(int health = 100, byte owner = Theirs)
        {
            _world.CreateTurret(Plug, range: 0.1f, damage: 0, health: health, faction: owner);
            _world.Tick();
        }

        /// <summary>Builds the field for one traversal and says whether it found a way in.</summary>
        private bool HasRoute(Traversal traversal)
        {
            for (int i = 0; i < 4; i++)
            {
                _world.Fields.Request(Beyond, traversal);
                _world.TickFrame(0.1f);
            }

            return !_world.Fields.IsKnownUnreachable(Beyond, ThisSide, _world.Map, traversal);
        }

        [Test]
        public void TheWallIsSealedToACivilian()
        {
            PlugTheGap();

            HasRoute(Traversal.Civilian).Should().BeFalse("a structure is a wall to anyone who cannot break it");
        }

        [Test]
        public void ABreacherFindsAWayThrough()
        {
            PlugTheGap();

            HasRoute(new Traversal(BreachClass.High, Ours)).Should()
                  .BeTrue("the plug is worth two shots, which is cheaper than the wall it sits in");
        }

        /// <summary>
        /// The rule that made a second flow field unnecessary. The same seeker, the same wall, and the only
        /// difference is whose the wall is.
        /// </summary>
        [Test]
        public void NobodyBreachesTheirOwnWall()
        {
            PlugTheGap(owner: Ours);

            HasRoute(new Traversal(BreachClass.High, Ours)).Should().BeFalse("it is ours");
            HasRoute(new Traversal(BreachClass.High, Theirs)).Should().BeTrue("and theirs to break");
        }

        /// <summary>
        /// Health over damage, at the level that matters: the same plug is a way in for the heavy class and a
        /// wall for the light one, because sixty shots is further than any detour can be priced as.
        /// </summary>
        [Test]
        public void APlugTooToughForTheClassIsStillAWall()
        {
            // Six hundred health is twelve shots for the heavy class and sixty for the light one, and sixty
            // shots is further than a breach can be priced at all - so it saturates and reads as a wall.
            PlugTheGap(health: 600);

            HasRoute(new Traversal(BreachClass.Low, Ours)).Should().BeFalse();
            HasRoute(new Traversal(BreachClass.High, Ours)).Should().BeTrue();
        }

        [Test]
        public void WithNothingInTheGapEverybodyWalksThrough()
        {
            HasRoute(Traversal.Civilian).Should().BeTrue("the gap was never plugged");
        }

        /// <summary>
        /// End to end: a soldier that can breach is given a walk its own cost model says is possible, and
        /// heads for the plug. A hauler given the same walk has nowhere to go and the watchdog drops it.
        /// </summary>
        [Test]
        public void ASoldierHeadsForThePlugAndAHaulerDoesNot()
        {
            PlugTheGap();

            Entity soldier = _world.CreateSoldier(GridCoords.CellCenter(ThisSide), Ours,
                                                  range: 2f, damage: 1, reloadSeconds: 1f, leash: 40f,
                                                  breach: BreachClass.High);
            Entity hauler = _world.CreateIdleAgent(GridCoords.CellCenter(ThisSide + new int2(0, 2)),
                                                   faction: Ours);

            // Armed at creation, so the traversal is already the breaching one.
            _world.Entities.GetComponentData<PathFollow>(soldier).Traversal.CanBreach.Should().BeTrue();
            _world.Entities.GetComponentData<PathFollow>(hauler).Traversal.CanBreach.Should().BeFalse();

            _world.StepsOf(soldier).Add(TaskStep.GoTo(Beyond));
            _world.StepsOf(hauler).Add(TaskStep.GoTo(Beyond));

            float soldierStart = _world.AgentOf(soldier).Position.x;
            float haulerStart = _world.AgentOf(hauler).Position.x;

            _world.TickFrames(120);

            _world.AgentOf(soldier).Position.x.Should()
                  .BeGreaterThan(soldierStart + 3f, "it should have set off for the way in");

            _world.AgentOf(hauler).Position.x.Should()
                  .BeLessThan(haulerStart + 2f, "there is no way through for it and nothing to walk towards");
        }
    }
}
