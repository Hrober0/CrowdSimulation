using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Sides, soldiers, and what each costs the other (design §14 step 13).
    ///
    /// A <see cref="Faction"/> is a byte because the template is built for two *or more* sides, so nothing
    /// here is written for exactly two - the last test puts three on the map and expects each of them to mind
    /// its own business.
    ///
    /// A soldier is an agent whose <see cref="Weapon"/> is switched on, and that one bit is doing three jobs:
    /// it is what makes the agent shoot, what keeps it out of the labour market, and what keeps it out of a
    /// hut's beds. Three rules that cannot disagree because there is only one fact.
    /// </summary>
    public class FactionTests
    {
        private const byte Ours = 0;
        private const byte Theirs = 1;
        private const byte AThirdParty = 2;

        private static readonly ItemId Grain = new(1);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 Centre(int2 cell) => GridCoords.CellCenter(cell);

        // ---- shooting ----------------------------------------------------------------------------------

        [Test]
        public void ASoldierShootsAnEnemyInRange()
        {
            _world.CreateSoldier(Centre(new int2(10, 10)), Ours, range: 6f);
            Entity raider = _world.CreateHostileAgent(Centre(new int2(13, 10)), faction: Theirs);

            _world.TickFrames(6);

            Health health = _world.HealthOf(raider);
            health.Current.Should().BeLessThan(health.Max);
        }

        [Test]
        public void ASoldierDoesNotShootItsOwnSide()
        {
            _world.CreateSoldier(Centre(new int2(10, 10)), Ours, range: 6f);
            Entity friend = _world.CreateIdleAgent(Centre(new int2(12, 10)), faction: Ours);

            _world.TickFrames(20);

            Health health = _world.HealthOf(friend);
            health.Current.Should().Be(health.Max);
        }

        /// <summary>
        /// A soldier is an agent with a weapon and a turret is a footprint with a weapon, and one system
        /// serves both - so this is the same rule as the turret's, asked of the other carrier.
        /// </summary>
        [Test]
        public void ASoldierShootsAnEnemyBuilding()
        {
            Entity theirCamp = _world.CreateTurret(new int2(20, 20), range: 0.1f, damage: 0,
                                                   health: 200, faction: Theirs);

            _world.CreateSoldier(Centre(new int2(17, 20)), Ours, range: 8f, damage: 15);

            _world.TickFrames(20);

            Health health = _world.HealthOf(theirCamp);
            health.Current.Should().BeLessThan(health.Max, "an enemy structure is a target like any other");
        }

        [Test]
        public void NobodyShootsTheirOwnBuildings()
        {
            Entity ourTurret = _world.CreateTurret(new int2(20, 20), range: 0.1f, damage: 0,
                                                   health: 200, faction: Ours);

            _world.CreateSoldier(Centre(new int2(17, 20)), Ours, range: 8f, damage: 15);

            _world.TickFrames(20);

            Health health = _world.HealthOf(ourTurret);
            health.Current.Should().Be(health.Max);
        }

        /// <summary>
        /// An armed raider walking past a turret shoots back, which is the whole reason a turret has health.
        /// Tough enough to survive the window, because what is being tested is that it is hit at all.
        /// </summary>
        [Test]
        public void ARaiderShootsBackAtATurret()
        {
            Entity turret = _world.CreateTurret(new int2(10, 10), range: 8f, damage: 5,
                                                reloadSeconds: 1f, health: 5000, faction: Ours);

            _world.CreateSoldier(Centre(new int2(13, 10)), Theirs, maxHealth: 5000,
                                 range: 8f, damage: 20, reloadSeconds: 0.2f);

            _world.TickFrames(30);

            Health health = _world.HealthOf(turret);
            health.Current.Should().BeLessThan(health.Max);
        }

        /// <summary>
        /// A building shot to nothing is demolished, and demolition is a path that already existed: the
        /// cleanup buffers give the cells back on the next grid phase exactly as they do when the player
        /// clicks the demolish tool. Nothing in combat knows what a cell is.
        /// </summary>
        [Test]
        public void ABuildingShotToNothingGivesItsCellsBack()
        {
            var cell = new int2(20, 20);
            Entity theirs = _world.CreateTurret(cell, range: 0.1f, damage: 0, health: 60, faction: Theirs);

            _world.CreateSoldier(Centre(new int2(17, 20)), Ours, range: 8f, damage: 30, reloadSeconds: 0.1f);

            _world.Tick();
            _world.Map.GetFlags(cell).HasFlag(CellFlags.Building).Should().BeTrue();

            _world.TickFrames(30);

            _world.Entities.Exists(theirs).Should().BeFalse();
            _world.Map.GetFlags(cell).HasFlag(CellFlags.Building).Should()
                  .BeFalse("the ground it stood on comes back");
        }

        /// <summary>
        /// Nothing is written for two sides. A third faction is an enemy of both of the others, and that
        /// falls out of "the ids differ" without a relationship table anywhere.
        ///
        /// Two pairs far enough apart not to see each other, because a weapon shoots the *nearest* enemy -
        /// putting all four in a row would test the tie-break rather than the rule.
        /// </summary>
        [Test]
        public void AThirdSideIsEverybodyElsesEnemyToo()
        {
            _world.CreateSoldier(Centre(new int2(10, 10)), AThirdParty, range: 6f, reloadSeconds: 0.2f);
            Entity ourAgent = _world.CreateHostileAgent(Centre(new int2(12, 10)), maxHealth: 500, faction: Ours);

            _world.CreateSoldier(Centre(new int2(40, 40)), AThirdParty, range: 6f, reloadSeconds: 0.2f);
            Entity theirAgent = _world.CreateHostileAgent(Centre(new int2(42, 40)), maxHealth: 500, faction: Theirs);

            _world.TickFrames(20);

            _world.HealthOf(ourAgent).Current.Should().BeLessThan(500, "a third side is our enemy");
            _world.HealthOf(theirAgent).Current.Should().BeLessThan(500, "and theirs as well");
        }

        // ---- the economy -------------------------------------------------------------------------------

        /// <summary>
        /// Nobody works for the other side. Without this a raider standing still next to a warehouse is idle
        /// by every test the market applies, and would be handed a crate of grain.
        /// </summary>
        [Test]
        public void NobodyWorksForTheOtherSide()
        {
            Entity source = _world.CreateSource(new int2(4, 4), Grain, amount: 50);
            Entity store = _world.CreateWarehouse(new int2(20, 4), Grain);

            _world.Entities.AddComponentData(source, new Faction { Id = Ours });
            _world.Entities.AddComponentData(store, new Faction { Id = Ours });

            Entity raider = _world.CreateHostileAgent(Centre(new int2(12, 4)), faction: Theirs);

            _world.TickFrames(10);

            _world.HasOrder(raider).Should().BeFalse();
            _world.StepsOf(raider).IsEmpty.Should().BeTrue("nothing should have given it anywhere to be");
        }

        [Test]
        public void OurOwnHaulerStillTakesTheJob()
        {
            Entity source = _world.CreateSource(new int2(4, 4), Grain, amount: 50);
            Entity store = _world.CreateWarehouse(new int2(20, 4), Grain);

            _world.Entities.AddComponentData(source, new Faction { Id = Ours });
            _world.Entities.AddComponentData(store, new Faction { Id = Ours });

            Entity hauler = _world.CreateIdleAgent(Centre(new int2(12, 4)), faction: Ours);

            _world.TickFrames(10);

            _world.HasOrder(hauler).Should().BeTrue("the faction filter must not break the ordinary case");
        }

        [Test]
        public void ASoldierIsNeverGivenAHaul()
        {
            Entity source = _world.CreateSource(new int2(4, 4), Grain, amount: 50);
            Entity store = _world.CreateWarehouse(new int2(20, 4), Grain);

            _world.Entities.AddComponentData(source, new Faction { Id = Ours });
            _world.Entities.AddComponentData(store, new Faction { Id = Ours });

            Entity soldier = _world.CreateSoldier(Centre(new int2(12, 4)), Ours);

            _world.TickFrames(10);

            _world.HasOrder(soldier).Should().BeFalse("a soldier is not labour");
        }

        // ---- resting -----------------------------------------------------------------------------------

        [Test]
        public void AnAgentRestsInItsOwnFactionsHut()
        {
            Entity hut = _world.CreateShelter(new int2(10, 10), capacity: 2);
            _world.Entities.AddComponentData(hut, new Faction { Id = Ours });

            Entity hauler = _world.CreateIdleAgent(Centre(new int2(10, 6)), faction: Ours);

            _world.TickFrames(60);

            _world.IsInside(hauler).Should().BeTrue();
        }

        [Test]
        public void NobodySleepsInTheEnemysHut()
        {
            Entity hut = _world.CreateShelter(new int2(10, 10), capacity: 2);
            _world.Entities.AddComponentData(hut, new Faction { Id = Ours });

            Entity raider = _world.CreateHostileAgent(Centre(new int2(10, 6)), faction: Theirs);

            _world.TickFrames(60);

            _world.IsInside(raider).Should().BeFalse();
            _world.InteriorOf(hut).Claimed.Should().Be(0);
        }

        /// <summary>
        /// A hut's beds are the hauling economy's throughput. An army resting in them would starve it, so an
        /// armed agent falls through to standing about instead.
        /// </summary>
        [Test]
        public void ASoldierTakesNoBed()
        {
            Entity hut = _world.CreateShelter(new int2(10, 10), capacity: 2);
            _world.Entities.AddComponentData(hut, new Faction { Id = Ours });

            Entity soldier = _world.CreateSoldier(Centre(new int2(10, 6)), Ours);

            _world.TickFrames(60);

            _world.IsInside(soldier).Should().BeFalse();
            _world.InteriorOf(hut).Claimed.Should().Be(0);
        }
    }
}
