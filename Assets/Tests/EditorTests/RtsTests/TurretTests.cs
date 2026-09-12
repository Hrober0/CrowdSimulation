using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// A turret, and with it the first thing in the game that can be destroyed (design §14 step 12).
    ///
    /// The turret itself is thin on purpose - find something in range, hurt it. What these are really about
    /// is the two rules underneath it: damage goes through one queue with one applier, and a building with no
    /// door, no shelf and nobody inside is still an ordinary building everywhere else.
    ///
    /// Step 13 made the weapon a component two different things carry; the soldier's half of that, and the
    /// faction rules both halves share, are in <see cref="FactionTests"/>.
    /// </summary>
    public class TurretTests
    {
        private static readonly int2 TurretCell = new(10, 10);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 Centre(int2 cell) => GridCoords.CellCenter(cell);

        [Test]
        public void ATurretShootsWhatComesIntoRange()
        {
            _world.CreateTurret(TurretCell, range: 8f, damage: 20);
            Entity raider = _world.CreateHostileAgent(Centre(TurretCell + new int2(4, 0)));

            _world.TickFrames(4);

            _world.HealthOf(raider).Current.Should().BeLessThan(_world.HealthOf(raider).Max);
        }

        [Test]
        public void NothingOutOfRangeIsShotAt()
        {
            _world.CreateTurret(TurretCell, range: 4f, damage: 20);
            Entity raider = _world.CreateHostileAgent(Centre(TurretCell + new int2(12, 0)));

            _world.TickFrames(10);

            Health health = _world.HealthOf(raider);
            health.Current.Should().Be(health.Max);
        }

        /// <summary>The one rule a turret has that is not about geometry: whose side the target is on.</summary>
        [Test]
        public void ATurretDoesNotShootItsOwnSide()
        {
            _world.CreateTurret(TurretCell, range: 8f, damage: 20);
            Entity hauler = _world.CreateIdleAgent(Centre(TurretCell + new int2(3, 0)));

            _world.TickFrames(10);

            Health health = _world.HealthOf(hauler);
            health.Current.Should().Be(health.Max);
        }

        [Test]
        public void WhatRunsOutOfHealthIsGone()
        {
            _world.CreateTurret(TurretCell, range: 8f, damage: 20, reloadSeconds: 0.1f);
            Entity raider = _world.CreateHostileAgent(Centre(TurretCell + new int2(3, 0)), maxHealth: 40);

            _world.TickFrames(20);

            _world.Entities.Exists(raider).Should().BeFalse();
        }

        /// <summary>
        /// Rate of fire is a property of the turret, not of the tick rate. The economy runs at 10 Hz in the
        /// game and every frame here, so a turret that fired once per update would be four times deadlier in
        /// a test than in the game it is tuned for.
        /// </summary>
        [Test]
        public void TheReloadPacesTheShots()
        {
            _world.CreateTurret(TurretCell, range: 8f, damage: 10, reloadSeconds: 1f);
            Entity raider = _world.CreateHostileAgent(Centre(TurretCell + new int2(3, 0)), maxHealth: 1000);

            // Ten frames of a tenth of a second: one second of play, and at most two shots in it.
            _world.TickFrames(10, deltaTime: 0.1f);

            Health health = _world.HealthOf(raider);
            (health.Max - health.Current).Should().BeLessOrEqualTo(20);
            health.Current.Should().BeLessThan(health.Max, "it should have fired at least once");
        }

        /// <summary>
        /// Two turrets on one raider is the right answer - neither claims it - and the queue is what makes
        /// the pair safe: the applier is the only thing that writes health, so the second shot to land on a
        /// raider that is already dead does nothing rather than destroying it twice.
        /// </summary>
        [Test]
        public void TwoTurretsMayShootTheSameThing()
        {
            _world.CreateTurret(TurretCell, range: 8f, damage: 30, reloadSeconds: 0.1f);
            _world.CreateTurret(TurretCell + new int2(0, 4), range: 8f, damage: 30, reloadSeconds: 0.1f);

            Entity raider = _world.CreateHostileAgent(Centre(TurretCell + new int2(3, 2)), maxHealth: 40);

            _world.TickFrames(20);

            _world.Entities.Exists(raider).Should().BeFalse();
        }

        /// <summary>
        /// The catalog half of this step. A turret has none of the five things every other building has, and
        /// the one that is load-bearing is the door: it is the first building that is not a place anyone goes.
        /// </summary>
        [Test]
        public void ATurretIsABuildingWithNoDoor()
        {
            Entity turret = _world.CreateTurret(TurretCell);

            _world.Tick();

            _world.Entities.HasBuffer<BuildingFootprintCell>(turret).Should()
                  .BeTrue("it takes its cell like any other building");

            _world.Map.GetFlags(TurretCell).HasFlag(CellFlags.Building).Should().BeTrue();

            bool hasDoorstep = _world.Entities.HasBuffer<BuildingEntranceCell>(turret)
                               && !_world.Entities.GetBuffer<BuildingEntranceCell>(turret).IsEmpty;

            hasDoorstep.Should().BeFalse("nobody ever goes in");
        }

        /// <summary>
        /// Destroying a turret is the demolish path, which already gives cells back - so a turret that is
        /// shot down needs no cleanup of its own.
        /// </summary>
        [Test]
        public void ADestroyedTurretGivesItsCellBack()
        {
            Entity turret = _world.CreateTurret(TurretCell);

            _world.Tick();
            _world.Map.GetFlags(TurretCell).HasFlag(CellFlags.Building).Should().BeTrue();

            _world.Entities.DestroyEntity(turret);
            _world.Tick();

            _world.Map.GetFlags(TurretCell).HasFlag(CellFlags.Building).Should().BeFalse();
        }
    }
}
