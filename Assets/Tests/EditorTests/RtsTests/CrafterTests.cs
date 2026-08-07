using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>Recipes, work slots and the worker's shift (design §9, §14 step 7).</summary>
    public class CrafterTests
    {
        private static readonly ItemId Grain = new(1);
        private static readonly ItemId Bread = new(2);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static float2 Centre(int2 cell) => GridCoords.CellCenter(cell);

        [Test]
        public void ACrafterWithInputsAsksForAWorker()
        {
            Entity bakery = _world.CreateCrafter(new int2(10, 10), Grain, Bread, inputStock: 5);

            _world.TickFrame(0.1f);

            _world.Orders.TryFind(bakery, ItemId.None, out int index).Should().BeTrue();
            _world.Orders[index].Kind.Should().Be(OrderKind.Work);
        }

        [Test]
        public void ACrafterWithNoInputsAsksForNobody()
        {
            Entity bakery = _world.CreateCrafter(new int2(10, 10), Grain, Bread, inputStock: 0);

            _world.TickFrame(0.1f);

            _world.Orders.TryFind(bakery, ItemId.None, out int _).Should()
                  .BeFalse("pull means a building with nothing to do asks for nothing");
        }

        [Test]
        public void ACrafterStillAsksForItsInputs()
        {
            Entity bakery = _world.CreateCrafter(new int2(10, 10), Grain, Bread, inputStock: 0);

            _world.TickFrame(0.1f);

            _world.Orders.TryFind(bakery, Grain, out int index).Should()
                  .BeTrue("its input slot is an ordinary requesting slot");
            _world.Orders[index].Kind.Should().Be(OrderKind.Haul);
        }

        [Test]
        public void AWorkerIsSentAndClaimsABenchBeforeSettingOff()
        {
            Entity bakery = _world.CreateCrafter(new int2(10, 10), Grain, Bread, benches: 1, inputStock: 5);
            Entity worker = _world.CreateIdleAgent(Centre(new int2(6, 10)));

            _world.TickFrames(2);

            _world.HasOrder(worker).Should().BeTrue();
            _world.OrderOf(worker).Kind.Should().Be(OrderKind.Work);
            _world.OrderOf(worker).Target.Should().Be(bakery);
            _world.InteriorOf(bakery).Claimed.Should().Be(1, "the bench is held before the walk begins");
        }

        [Test]
        public void ABenchIsNeverPromisedTwice()
        {
            _world.CreateCrafter(new int2(10, 10), Grain, Bread, benches: 1, inputStock: 50);

            for (int i = 0; i < 4; i++)
            {
                _world.CreateIdleAgent(Centre(new int2(5 + i, 12)));
            }

            _world.TickFrames(3);

            using EntityQuery query = _world.Entities.CreateEntityQuery(typeof(AssignedOrder));
            query.CalculateEntityCount().Should().Be(1, "one bench, one worker");
        }

        [Test]
        public void AWorkerWalksIn_AndTheCrafterProduces()
        {
            Entity bakery = _world.CreateCrafter(
                new int2(10, 10), Grain, Bread, benches: 1, craftSeconds: 0.5f, inputStock: 3);
            Entity worker = _world.CreateIdleAgent(Centre(new int2(6, 10)));

            _world.TickFrames(600, 0.05f);

            _world.SlotOf(bakery, Bread).Amount.Should().Be(3, "three grain in, three bread out");
            _world.SlotOf(bakery, Grain).Amount.Should().Be(0);
            _world.IsInside(worker).Should().BeFalse("with nothing left to make, it walks back out");
        }

        [Test]
        public void AWorkerStaysInsideBetweenBatches()
        {
            Entity bakery = _world.CreateCrafter(
                new int2(10, 10), Grain, Bread, benches: 1, craftSeconds: 1f, inputStock: 20);
            Entity worker = _world.CreateIdleAgent(Centre(new int2(6, 10)));

            _world.TickFrames(300, 0.05f);

            _world.IsInside(worker).Should()
                  .BeTrue("Interact(inf) is a shift - it does not walk out and back in per loaf");
            _world.SlotOf(bakery, Bread).Amount.Should().BeGreaterThan(1, "and it has been busy");
        }

        [Test]
        public void TheBenchGoesBackWhenTheWorkerLeaves()
        {
            Entity bakery = _world.CreateCrafter(
                new int2(10, 10), Grain, Bread, benches: 1, craftSeconds: 0.5f, inputStock: 2);
            Entity worker = _world.CreateIdleAgent(Centre(new int2(6, 10)));

            _world.TickFrames(600, 0.05f);

            Interior interior = _world.InteriorOf(bakery);
            interior.Occupied.Should().Be(0);
            interior.Claimed.Should().Be(0, "a bench that is never given back is a bench lost forever");
            _world.HasClaim(worker).Should().BeFalse();
            _world.HasOrder(worker).Should().BeFalse();
        }

        [Test]
        public void ACrafterWithAFullOutputShelfStopsWorking()
        {
            Entity bakery = _world.CreateCrafter(
                new int2(10, 10), Grain, Bread, benches: 1, craftSeconds: 0.2f, inputStock: 20);

            DynamicBuffer<StorageSlot> slots = _world.SlotsOf(bakery);
            StorageSlot output = slots[1];
            output.Amount = output.Capacity - 1;
            slots[1] = output;

            _world.CreateIdleAgent(Centre(new int2(6, 10)));
            _world.TickFrames(400, 0.05f);

            _world.SlotOf(bakery, Bread).Amount.Should()
                  .Be(20, "it fills the last space and stops, rather than destroying what it makes");
            _world.SlotOf(bakery, Grain).Amount.Should().Be(19, "exactly one batch was consumed");
        }

        [Test]
        public void HaulersFeedACrafterAndTheCrafterWorks()
        {
            Entity farm = _world.CreateSource(new int2(4, 10), Grain, amount: 10);
            Entity bakery = _world.CreateCrafter(
                new int2(12, 10), Grain, Bread, benches: 1, craftSeconds: 0.3f, inputStock: 0);

            _world.CreateIdleAgent(Centre(new int2(8, 12)), carryCapacity: 5);
            _world.CreateIdleAgent(Centre(new int2(8, 8)));

            _world.TickFrames(1200, 0.05f);

            _world.SlotOf(bakery, Bread).Amount.Should()
                  .BeGreaterThan(0, "grain was hauled in and turned into bread with no connection authored anywhere");
            _world.SlotOf(farm, Grain).Amount.Should().BeLessThan(10);
        }

        [Test]
        public void ACrafterInputIsNeverRaidedByAWarehouse()
        {
            Entity bakery = _world.CreateCrafter(new int2(10, 10), Grain, Bread, inputStock: 10);
            _world.CreateWarehouse(new int2(16, 10), Grain, capacity: 50);
            _world.CreateIdleAgent(Centre(new int2(13, 10)), carryCapacity: 10);

            _world.TickFrames(10);

            _world.SlotOf(bakery, Grain).ReservedOut.Should()
                  .Be(0, "in equals out on a crafter input, so it has nothing to give");
        }
    }
}
