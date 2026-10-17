using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Soldiers against buildings.
    ///
    /// Shooting a building that happened to be in range always worked. What did not was everything around it:
    /// range was measured to a building's middle, so a sword could not reach anything bigger than a turret;
    /// a building was never a threat, so nothing ever walked at one; and a chase could only follow something
    /// with a body. Each test here fails without one of those three.
    /// </summary>
    public class SiegeTests
    {
        private const byte Ours = 0;
        private const byte Theirs = 1;

        private const float SwordRange = 1.4f;

        private static readonly ItemId Bread = new(3);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 Centre(int2 cell) => GridCoords.CellCenter(cell);

        /// <summary>A square building of a side, tough enough that a test about reaching it is not about razing it.</summary>
        private Entity CreateFort(int2 origin, int size, byte faction, int health = 5000)
        {
            var offsets = new int2[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    offsets[y * size + x] = new int2(x, y);
                }
            }

            Entity fort = _world.CreateBuilding(origin, GridRotation.None, offsets);
            _world.Entities.AddComponentData(fort, Health.Full(health));
            _world.Entities.AddComponentData(fort, new Faction { Id = faction });
            return fort;
        }

        /// <summary>
        /// A sword pressed against the middle of a 3x3 wall is 1.9 from the building's centre and 0.42 from
        /// its edge. Measured to the centre it could never land a blow; measured to the edge it can.
        /// </summary>
        [Test]
        public void ASwordReachesABuildingBiggerThanOneCell()
        {
            Entity fort = CreateFort(new int2(20, 20), size: 3, Theirs);

            // Just west of the fort's middle row, against the wall.
            _world.CreateSoldier(new float2(19.5f, 21.5f), Ours, range: SwordRange, damage: 10,
                                 reloadSeconds: 0.2f);

            _world.TickFrames(10);

            Health health = _world.HealthOf(fort);
            health.Current.Should().BeLessThan(health.Max, "the wall is within a sword's reach of where it stands");
        }

        /// <summary>
        /// An enemy building inside a leash is a threat like an enemy body: posted, claimed, walked at, and
        /// hit. Here by a sword, so the walk has to end against the wall rather than at a gun's distance.
        /// </summary>
        [Test]
        public void ASoldierWalksToAnEnemyBuildingNearItsPostAndAttacksIt()
        {
            Entity fort = CreateFort(new int2(20, 20), size: 2, Theirs);
            Entity soldier = _world.CreateSoldier(Centre(new int2(12, 21)), Ours, range: SwordRange, damage: 10,
                                                  reloadSeconds: 0.2f, leash: 14f);

            _world.TickFrames(120);

            _world.OrderOf(soldier).Target.Should().Be(fort, "a building near the post is somebody's job");

            Health health = _world.HealthOf(fort);
            health.Current.Should().BeLessThan(health.Max, "and the job is to knock it down");
        }

        /// <summary>A building gets a few attackers, not the whole garrison and not just one.</summary>
        [Test]
        public void ABuildingDrawsAFewSoldiersRatherThanOneOrAll()
        {
            Entity fort = CreateFort(new int2(20, 20), size: 2, Theirs);

            var soldiers = new Entity[8];
            for (int i = 0; i < soldiers.Length; i++)
            {
                soldiers[i] = _world.CreateSoldier(Centre(new int2(10, 14 + i)), Ours, range: SwordRange,
                                                   damage: 1, reloadSeconds: 1f, leash: 20f);
            }

            _world.TickFrames(10);

            int attackers = 0;
            foreach (Entity soldier in soldiers)
            {
                if (_world.HasOrder(soldier) && _world.OrderOf(soldier).Target == fort)
                {
                    attackers++;
                }
            }

            attackers.Should().BeGreaterThan(1).And.BeLessThan(soldiers.Length);
        }

        [Test]
        public void NobodyBesiegesTheirOwnBuilding()
        {
            CreateFort(new int2(20, 20), size: 2, Ours);
            Entity soldier = _world.CreateSoldier(Centre(new int2(14, 21)), Ours, leash: 14f);

            _world.TickFrames(10);

            _world.HasOrder(soldier).Should().BeFalse();
        }

        /// <summary>
        /// A crafter's work order and a siege on the same crafter both name the building and no item. Before
        /// the kind was part of an order's key, each system's "post or refresh" found the other's order and
        /// rewrote it.
        /// </summary>
        [Test]
        public void ASiegeAndAWorkOrderOnOneBuildingAreKeptApart()
        {
            Entity camp = _world.CreateCamp(new int2(20, 20), Bread, inputStock: 5, faction: Theirs);
            _world.Entities.AddComponentData(camp, Health.Full(5000));
            _world.CreateSoldier(Centre(new int2(15, 20)), Ours, damage: 1, reloadSeconds: 1f, leash: 14f);

            _world.TickFrame(0.1f);

            _world.Orders.TryFind(camp, ItemId.None, OrderKind.Work, out int work).Should().BeTrue();
            _world.Orders.TryFind(camp, ItemId.None, OrderKind.Fight, out int siege).Should().BeTrue();

            _world.Orders[work].Priority.Should().Be(5, "the camp's own priority, not the siege's");
            _world.Orders[siege].Priority.Should().BeGreaterThan(5);
        }
    }
}
