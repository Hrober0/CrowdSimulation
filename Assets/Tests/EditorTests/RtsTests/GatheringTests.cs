using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// Mines and lumber camps: a building that sends its workers out to the map (design §14 step 10).
    ///
    /// The whole of gathering is an ordinary work order whose task happens to be a round trip, so most of
    /// what these tests check is that the machinery underneath - claims, reservations, pickup, deposit -
    /// carries a gatherer without having been told about one.
    /// </summary>
    public class GatheringTests
    {
        private static readonly ItemId Ore = new(6);

        private static readonly int2 MineCell = new(6, 6);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private int2 Doorstep => MineCell + new int2(0, -1);

        [Test]
        public void AMineFillsItselfFromASeamNearby()
        {
            _world.CreateGatherer(MineCell, ObjectKind.Ore, Ore);
            _world.CreateResourceNode(new int2(10, 6), ObjectKind.Ore, Ore, 40, cost: 8);
            _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep));

            _world.TickFrames(400);

            _world.Entities.GetBuffer<StorageSlot>(FindMine())[0].Amount.Should()
                  .BeGreaterThan(0, "a miner should have walked to the seam and brought some back");
        }

        /// <summary>
        /// The trip repeats. Nothing keeps the miner employed between trips - its claim is given back when
        /// the steps run out - but it is standing on the doorstep it just delivered to, so it is the nearest
        /// free pair of hands when the mine asks again. Employment falls out of geometry.
        /// </summary>
        [Test]
        public void AMinerKeepsGoingBackRatherThanStoppingAfterOneLoad()
        {
            // Deep enough that running out cannot be mistaken for stopping: one agent carries ten, and the
            // shelf is never the limit either.
            _world.CreateGatherer(MineCell, ObjectKind.Ore, Ore, capacity: 400);
            _world.CreateResourceNode(new int2(10, 6), ObjectKind.Ore, Ore, 400, cost: 8);
            Entity miner = _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep));

            _world.TickFrames(60);
            int afterFirst = _world.Entities.GetBuffer<StorageSlot>(FindMine())[0].Amount;

            _world.TickFrames(300);
            int afterMore = _world.Entities.GetBuffer<StorageSlot>(FindMine())[0].Amount;

            afterFirst.Should().BeGreaterThan(0, "a first load should be home");
            afterMore.Should().BeGreaterThan(afterFirst, "and the same miner should have gone back for more");

            int carryCapacity = _world.Entities.GetComponentData<Carry>(miner).Capacity;
            afterMore.Should()
                     .BeGreaterThan(carryCapacity,
                                    "more than one load, which is the whole claim - a gatherer that stopped "
                                    + "after one trip would sit at exactly a carry capacity for ever");
        }

        /// <summary>
        /// A seam is emptied and taken away, and the mine keeps what it fetched. This is the whole loop:
        /// nothing is created, the ore is moved.
        /// </summary>
        [Test]
        public void ASeamIsWorkedOutAndThenGoes()
        {
            _world.CreateGatherer(MineCell, ObjectKind.Ore, Ore, workers: 2);
            Entity seam = _world.CreateResourceNode(new int2(9, 6), ObjectKind.Ore, Ore, 12, cost: 8);
            _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep));
            _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep + new int2(1, 0)));

            _world.TickFrames(900);

            _world.Entities.Exists(seam).Should().BeFalse("there was twelve ore in it and it has all been taken");
            _world.Entities.GetBuffer<StorageSlot>(FindMine())[0].Amount.Should()
                  .Be(12, "and all twelve are on the mine's shelf - none of it was lost or invented");
        }

        /// <summary>
        /// Range is a real limit and it is safe to have one, unlike the order market's (§8): a mine with
        /// nothing in reach has nothing to do, which is a different thing from refusing a job nobody else
        /// will take.
        /// </summary>
        [Test]
        public void AMineDoesNotReachASeamOutsideItsRange()
        {
            _world.CreateGatherer(MineCell, ObjectKind.Ore, Ore, range: 4);
            _world.CreateResourceNode(MineCell + new int2(20, 0), ObjectKind.Ore, Ore, 40, cost: 8);
            _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep));

            _world.TickFrames(300);

            _world.Entities.GetBuffer<StorageSlot>(FindMine())[0].Amount.Should()
                  .Be(0, "the seam is twenty cells away and the mine works four");
        }

        /// <summary>
        /// How many gatherers a building may have out at once is <see cref="Interior.Capacity"/> - the same
        /// benches a crafter counts, claimed the same way. A miner never goes inside, so what caps them is
        /// <see cref="Interior.Claimed"/> rather than <see cref="Interior.Occupied"/>.
        /// </summary>
        [Test]
        public void NoMoreMinersGoOutThanTheMineHasRoomFor()
        {
            Entity mine = _world.CreateGatherer(MineCell, ObjectKind.Ore, Ore, workers: 1);
            _world.CreateResourceNode(new int2(10, 6), ObjectKind.Ore, Ore, 200, cost: 8);

            for (int i = 0; i < 4; i++)
            {
                _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep + new int2(i, 0)));
            }

            _world.TickFrames(120);

            _world.Entities.GetComponentData<Interior>(mine).Claimed.Should()
                  .BeLessOrEqualTo(1, "the mine has one bench, so it may have one miner out");
        }

        /// <summary>
        /// A gatherer's shelf must never ask the map to deliver what the building fetches for itself. If it
        /// did, a mine would post a haul order for the ore it is standing on top of, and haulers would spend
        /// the game shuttling ore between the mine and the yard.
        /// </summary>
        [Test]
        public void AMineNeverAsksToBeDeliveredWhatItGathers()
        {
            _world.CreateGatherer(MineCell, ObjectKind.Ore, Ore);
            _world.CreateResourceNode(new int2(10, 6), ObjectKind.Ore, Ore, 40, cost: 8);
            _world.CreateIdleAgent(GridCoords.CellCenter(Doorstep));

            _world.TickFrames(200);

            using EntityQuery query = _world.Entities.CreateEntityQuery(ComponentType.ReadOnly<OrderBook>());
            OrderBook book = query.GetSingleton<OrderBook>();

            for (int i = 0; i < book.Length; i++)
            {
                (book[i].Kind == OrderKind.Haul && book[i].Item == Ore).Should()
                    .BeFalse("nothing should be asking for ore to be brought to it");
            }
        }

        private Entity FindMine()
        {
            using EntityQuery query = _world.Entities.CreateEntityQuery(ComponentType.ReadOnly<Reaps>());
            return query.GetSingletonEntity();
        }
    }
}
