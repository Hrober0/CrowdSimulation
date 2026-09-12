using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Soldiers going after what they see (design §14 step 13).
    ///
    /// Three properties, and each has a test that fails loudly without it:
    /// a threat is **dispatched through the order market**, so one raider pulls one soldier and not ten;
    /// a soldier is **leashed to its post**, so a scout walking past cannot empty a base;
    /// and a soldier **stops at weapon range**, because a ranged unit that closes to its target's cell is a
    /// melee unit.
    ///
    /// Targets here are given far more health than the shooting can get through, so that a test about
    /// walking is never really a test about who died first. A weapon with no damage is not a weapon - see
    /// <see cref="Weapon.IsArmed"/> - so they cannot simply be disarmed.
    /// </summary>
    public class PursuitTests
    {
        private const byte Ours = 0;
        private const byte Theirs = 1;

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 Centre(int2 cell) => GridCoords.CellCenter(cell);

        private static float Distance(in AgentMove a, in AgentMove b) =>
            math.distance(a.Position, b.Position);

        /// <summary>
        /// A threat near a post becomes somebody's job.
        ///
        /// Asserted on the claim rather than on the order, because the order is not something that can be
        /// caught standing still: it is posted, matched and emptied inside one tick, and swept at the end of
        /// the same one. The soldier holding it is the durable record that it existed.
        /// </summary>
        [Test]
        public void AThreatNearAPostIsDealtWith()
        {
            Entity soldier = _world.CreateSoldier(Centre(new int2(10, 10)), Ours, leash: 12f);
            Entity raider = _world.CreateHostileAgent(Centre(new int2(16, 10)), maxHealth: 5000, faction: Theirs);

            _world.TickFrames(3);

            _world.HasOrder(soldier).Should().BeTrue("a threat is a piece of work the world wants doing");
            _world.OrderOf(soldier).Kind.Should().Be(OrderKind.Fight);
            _world.OrderOf(soldier).Target.Should().Be(raider);
        }

        [Test]
        public void NothingOutsideTheLeashIsPostedAtAll()
        {
            _world.CreateSoldier(Centre(new int2(10, 10)), Ours, leash: 5f);
            Entity raider = _world.CreateHostileAgent(Centre(new int2(30, 10)), faction: Theirs);

            _world.TickFrames(5);

            _world.Orders.TryFind(raider, ItemId.None, out int _).Should().BeFalse();
        }

        /// <summary>The headline: a soldier that spots something walks at it rather than watching it pass.</summary>
        [Test]
        public void ASoldierWalksAtAThreatItCannotReach()
        {
            Entity soldier = _world.CreateSoldier(Centre(new int2(10, 10)), Ours, range: 4f,
                                                  damage: 1, reloadSeconds: 1f, leash: 20f);
            Entity raider = _world.CreateHostileAgent(Centre(new int2(24, 10)), maxHealth: 5000,
                                                      faction: Theirs);

            float before = Distance(_world.AgentOf(soldier), _world.AgentOf(raider));

            _world.TickFrames(60);

            float after = Distance(_world.AgentOf(soldier), _world.AgentOf(raider));
            after.Should().BeLessThan(before - 1f, "it should have closed the gap");
        }

        /// <summary>
        /// And stops when it can shoot. Walking on to the target's own cell is what a melee unit does; a
        /// soldier that did it would stand in the middle of the thing it is shooting.
        /// </summary>
        [Test]
        public void ASoldierStopsAtWeaponRange()
        {
            Entity soldier = _world.CreateSoldier(Centre(new int2(10, 10)), Ours,
                                                  range: 6f, damage: 1, reloadSeconds: 1f, leash: 30f);
            Entity raider = _world.CreateHostileAgent(Centre(new int2(26, 10)), maxHealth: 5000, faction: Theirs);

            _world.TickFrames(150);

            float gap = Distance(_world.AgentOf(soldier), _world.AgentOf(raider));

            gap.Should().BeLessOrEqualTo(6f, "it should have got within range");
            gap.Should().BeGreaterThan(1f, "and stopped there rather than walking onto it");
        }

        /// <summary>
        /// The order market is doing the rationing. Without it every soldier that can see the raider walks at
        /// it, which is the thundering herd §8 exists to prevent - and the flank they all left is open.
        /// </summary>
        [Test]
        public void OneThreatPullsOneSoldier()
        {
            for (int i = 0; i < 5; i++)
            {
                _world.CreateSoldier(Centre(new int2(10, 8 + i)), Ours, range: 3f, damage: 1, reloadSeconds: 1f, leash: 20f);
            }

            _world.CreateHostileAgent(Centre(new int2(24, 10)), maxHealth: 5000, faction: Theirs);

            _world.TickFrames(5);

            using EntityQuery query = _world.Entities.CreateEntityQuery(typeof(AssignedOrder));
            query.CalculateEntityCount().Should().Be(1, "one threat is one order and one order is one claim");
        }

        /// <summary>
        /// The leash, and the reason it is mandatory: without it one expendable scout walks a base's whole
        /// garrison off it and the raid that follows meets nobody.
        /// </summary>
        [Test]
        public void ASoldierGivesUpWhenTheChaseLeavesItsPost()
        {
            var home = new int2(10, 10);
            Entity soldier = _world.CreateSoldier(Centre(home), Ours, range: 2f, damage: 1, reloadSeconds: 1f, leash: 6f);

            // Just inside the leash, so the chase starts...
            Entity scout = _world.CreateHostileAgent(Centre(new int2(15, 10)), maxHealth: 5000, faction: Theirs);

            _world.TickFrames(10);
            _world.StepsOf(soldier).IsEmpty.Should().BeFalse("the chase should have started");

            // ...and then the scout runs well outside it.
            AgentMove moved = _world.AgentOf(scout);
            moved.Position = Centre(new int2(40, 10));
            _world.Entities.SetComponentData(scout, moved);

            _world.TickFrames(20);

            _world.StepsOf(soldier).IsEmpty.Should().BeTrue("the chase is off");
            math.distance(_world.AgentOf(soldier).Position, Centre(home)).Should()
                .BeLessOrEqualTo(6f, "and it never left its post");
        }

        [Test]
        public void AChaseEndsWhenTheTargetDies()
        {
            Entity soldier = _world.CreateSoldier(Centre(new int2(10, 10)), Ours,
                                                  range: 5f, damage: 40, reloadSeconds: 0.1f, leash: 20f);

            _world.CreateHostileAgent(Centre(new int2(16, 10)), maxHealth: 60, faction: Theirs);

            _world.TickFrames(100);

            _world.StepsOf(soldier).IsEmpty.Should().BeTrue();
            _world.HasOrder(soldier).Should().BeFalse("and the soldier is free for the next one");
        }

        /// <summary>A worker is never dispatched at a raider, however close it is standing.</summary>
        [Test]
        public void AWorkerIsNeverSentToFight()
        {
            _world.CreateSoldier(Centre(new int2(10, 10)), Ours, range: 3f, damage: 1, reloadSeconds: 1f, leash: 20f);
            Entity hauler = _world.CreateIdleAgent(Centre(new int2(20, 10)), faction: Ours);

            _world.CreateHostileAgent(Centre(new int2(22, 10)), maxHealth: 5000, faction: Theirs);

            _world.TickFrames(10);

            _world.HasOrder(hauler).Should().BeFalse("labour and soldiery are disjoint");
        }
    }
}
