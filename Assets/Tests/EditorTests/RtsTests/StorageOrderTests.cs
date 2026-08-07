using FluentAssertions;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// The storage model and the order market (design §7, §8). A 64x64 map, so one flow field covers all of
    /// it and nothing here is about pathfinding.
    /// </summary>
    public class StorageOrderTests
    {
        private static readonly ItemId Bread = new(1);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 Centre(int2 cell) => GridNav.GridCoords.CellCenter(cell);

        [Test]
        public void ASlotBelowItsThreshold_PostsAnOrder()
        {
            Entity warehouse = _world.CreateWarehouse(new int2(10, 10), Bread, capacity: 50);

            _world.TickFrame(0.1f);

            _world.Orders.TryFind(warehouse, Bread, out int index).Should().BeTrue();
            _world.Orders[index].Amount.Should().Be(50);
            _world.Orders[index].Kind.Should().Be(OrderKind.Haul);
        }

        [Test]
        public void APureSourceNeverAsksForAnything()
        {
            Entity mine = _world.CreateSource(new int2(10, 10), Bread, amount: 40);

            _world.TickFrame(0.1f);

            _world.Orders.TryFind(mine, Bread, out int _).Should()
                  .BeFalse("priority 0 is reserved for pure sources, and they never request");
        }

        [Test]
        public void AFilledSlotStopsAsking()
        {
            Entity warehouse = _world.CreateWarehouse(new int2(10, 10), Bread, capacity: 10);
            _world.TickFrame(0.1f);
            _world.Orders.TryFind(warehouse, Bread, out int _).Should().BeTrue();

            DynamicBuffer<StorageSlot> slots = _world.SlotsOf(warehouse);
            StorageSlot slot = slots[0];
            slot.Amount = 10;
            slots[0] = slot;

            _world.TickFrame(0.1f);

            _world.Orders.TryFind(warehouse, Bread, out int _).Should()
                  .BeFalse("pull means an order exists only while somebody is still asking");
        }

        [Test]
        public void TwoWarehousesNeverTrade()
        {
            _world.CreateWarehouse(new int2(6, 6), Bread, capacity: 50);
            Entity full = _world.CreateWarehouse(new int2(14, 6), Bread, capacity: 50);

            DynamicBuffer<StorageSlot> slots = _world.SlotsOf(full);
            StorageSlot slot = slots[0];
            slot.Amount = 50;
            slots[0] = slot;

            _world.CreateIdleAgent(Centre(new int2(10, 6)));

            _world.TickFrames(5);

            _world.SlotOf(full, Bread).ReservedOut.Should()
                  .Be(0, "equal priority fails the strict inequality, in both directions - no ping-pong");
        }

        [Test]
        public void AWarehouseTakesFromASource()
        {
            Entity mine = _world.CreateSource(new int2(6, 6), Bread, amount: 40);
            Entity warehouse = _world.CreateWarehouse(new int2(14, 6), Bread, capacity: 50);
            Entity hauler = _world.CreateIdleAgent(Centre(new int2(10, 6)), carryCapacity: 10);

            _world.TickFrames(3);

            _world.HasOrder(hauler).Should().BeTrue();

            AssignedOrder order = _world.OrderOf(hauler);
            order.Source.Should().Be(mine);
            order.Target.Should().Be(warehouse);
            order.Amount.Should().Be(10, "a trip is capped by what the hauler can carry");

            _world.SlotOf(mine, Bread).ReservedOut.Should().Be(10, "the stock is promised the moment it is claimed");
            _world.SlotOf(warehouse, Bread).ReservedIn.Should().Be(10, "and so is the room for it");
        }

        [Test]
        public void AHaulerWalksTheGoodsAcrossAndTheyArrive()
        {
            Entity mine = _world.CreateSource(new int2(6, 6), Bread, amount: 40);
            Entity warehouse = _world.CreateWarehouse(new int2(14, 6), Bread, capacity: 50);
            _world.CreateIdleAgent(Centre(new int2(10, 6)), carryCapacity: 10);

            _world.TickFrames(600, 0.05f);

            _world.SlotOf(warehouse, Bread).Amount.Should().BeGreaterThan(0, "the bread got there");
            _world.SlotOf(mine, Bread).Amount.Should().BeLessThan(40, "and left the mine");

            StorageSlot source = _world.SlotOf(mine, Bread);
            StorageSlot destination = _world.SlotOf(warehouse, Bread);
            (source.Amount + destination.Amount).Should().Be(40, "nothing is created and nothing is lost");
        }

        [Test]
        public void AFinishedHaulerLetsGoOfItsOrder()
        {
            _world.CreateSource(new int2(6, 6), Bread, amount: 10);
            _world.CreateWarehouse(new int2(14, 6), Bread, capacity: 50);
            Entity hauler = _world.CreateIdleAgent(Centre(new int2(10, 6)), carryCapacity: 10);

            _world.TickFrames(600, 0.05f);

            _world.CarryOf(hauler).Amount.Should().Be(0, "it put the load down");
        }

        [Test]
        public void ReservationsAreFullyUnwoundOnceTheTripIsDone()
        {
            Entity mine = _world.CreateSource(new int2(6, 6), Bread, amount: 10);
            Entity warehouse = _world.CreateWarehouse(new int2(14, 6), Bread, capacity: 50);
            _world.CreateIdleAgent(Centre(new int2(10, 6)), carryCapacity: 10);

            _world.TickFrames(600, 0.05f);

            _world.SlotOf(mine, Bread).ReservedOut.Should().Be(0);
            _world.SlotOf(warehouse, Bread).ReservedIn.Should().Be(0);
        }

        [Test]
        public void OneSourceCannotBePromisedToMoreHaulersThanItHasStock()
        {
            Entity mine = _world.CreateSource(new int2(6, 6), Bread, amount: 15);
            _world.CreateWarehouse(new int2(14, 6), Bread, capacity: 100);

            for (int i = 0; i < 5; i++)
            {
                _world.CreateIdleAgent(Centre(new int2(9 + i, 8)), carryCapacity: 10);
            }

            _world.TickFrames(3);

            _world.SlotOf(mine, Bread).ReservedOut.Should()
                  .BeLessOrEqualTo(15, "if fifteen loaves exist, only fifteen loaves of orders exist");
        }

        [Test]
        public void AHaulerRestingInAHutComesOutToWork()
        {
            _world.CreateShelter(new int2(20, 20), capacity: 4);
            Entity hauler = _world.CreateIdleAgent(Centre(new int2(20, 18)), carryCapacity: 10);

            // No demand yet, so it goes and sits down.
            _world.TickFrames(400, 0.05f);
            _world.IsInside(hauler).Should().BeTrue("nothing to do, so it rests");

            _world.CreateSource(new int2(6, 6), Bread, amount: 40);
            _world.CreateWarehouse(new int2(10, 6), Bread, capacity: 50);

            _world.TickFrames(5);

            _world.HasOrder(hauler).Should().BeTrue("demand appeared, so it is woken up");
            _world.IsInside(hauler).Should().BeFalse("the task begins with an Exit, so it is out of the door");
            _world.HasClaim(hauler).Should().BeFalse("and its bed goes back to the hut");
        }

        [Test]
        public void AnOrderIsNeverGivenToMoreHaulersThanTheBuildingAllows()
        {
            _world.CreateSource(new int2(6, 6), Bread, amount: 500);
            Entity site = _world.CreateWarehouse(new int2(14, 6), Bread, capacity: 500);
            _world.Entities.AddComponentData(site, new HaulLimit { MaxConcurrent = 2 });

            for (int i = 0; i < 6; i++)
            {
                _world.CreateIdleAgent(Centre(new int2(9 + i, 9)), carryCapacity: 10);
            }

            _world.TickFrames(3);

            int working = 0;
            using (EntityQuery query = _world.Entities.CreateEntityQuery(typeof(AssignedOrder)))
            {
                working = query.CalculateEntityCount();
            }

            working.Should().Be(2, "demand size does not set the number of trips - the cap does");
        }

        [Test]
        public void AgingLetsAWaitingOrderClimb()
        {
            Entity warehouse = _world.CreateWarehouse(new int2(10, 10), Bread, capacity: 50);

            _world.TickFrame(0.1f);
            _world.Orders.TryFind(warehouse, Bread, out int index).Should().BeTrue();
            float first = _world.Orders[index].Effective;

            _world.TickFrames(20, 1f);

            _world.Orders.TryFind(warehouse, Bread, out index).Should().BeTrue();
            _world.Orders[index].Effective.Should()
                  .BeGreaterThan(first, "waiting is itself a claim on attention");
        }
    }
}
