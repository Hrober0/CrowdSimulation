using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Rts;
using Unity.Entities;
using Unity.Mathematics;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// A camp: a crafter whose batch arms the worker (design §14 step 13).
    ///
    /// The point of these is how little there is. A camp posts a work order, a worker walks in and does a
    /// shift, its input slot asks for bread - all of that is the crafting machinery of step 7 untouched. The
    /// only new fact in the building is that the worker walks back out as a soldier.
    ///
    /// It converts rather than creates, so a camp costs the economy a pair of hands. That is also the whole
    /// of why there is no queue here and no entity ever made: arming is one enableable bit.
    /// </summary>
    public class TrainingCampTests
    {
        private static readonly ItemId Bread = new(3);

        private static readonly int2 CampCell = new(10, 10);

        private RtsTestWorld _world;

        [SetUp]
        public void Setup() => _world = new RtsTestWorld();

        [TearDown]
        public void Teardown() => _world.Dispose();

        private static int2 Doorstep => CampCell + new int2(0, -1);

        private static float2 Centre(int2 cell) => GridCoords.CellCenter(cell);

        [Test]
        public void ACampWithBreadOnTheShelfAsksForAWorker()
        {
            Entity camp = _world.CreateCamp(CampCell, Bread, inputStock: 5);

            _world.TickFrame(0.1f);

            _world.Orders.TryFind(camp, ItemId.None, out int index).Should()
                  .BeTrue("a recipe with no output item is still a recipe");
            _world.Orders[index].Kind.Should().Be(OrderKind.Work);
        }

        /// <summary>
        /// A crafter stops asking when its output shelf is full. A camp has no output shelf, and the check
        /// that would have blocked it asks for room for the outputs a recipe *names* - none, so none needed.
        /// </summary>
        [Test]
        public void ACampIsNeverBlockedByAFullShelf()
        {
            _world.CreateCamp(CampCell, Bread, benches: 2, craftSeconds: 0.5f, inputStock: 100);

            Entity first = _world.CreateIdleAgent(Centre(Doorstep));
            Entity second = _world.CreateIdleAgent(Centre(Doorstep + new int2(2, 0)));
            Entity third = _world.CreateIdleAgent(Centre(Doorstep + new int2(-2, 0)));

            _world.TickFrames(300);

            int armed = 0;
            foreach (Entity worker in new[] { first, second, third })
            {
                if (_world.IsArmed(worker))
                {
                    armed++;
                }
            }

            armed.Should().BeGreaterThan(1, "batch after batch, with nowhere to stack what it makes");
        }

        [Test]
        public void ACampTurnsItsWorkerIntoASoldier()
        {
            _world.CreateCamp(CampCell, Bread, inputStock: 6, craftSeconds: 0.5f);
            Entity worker = _world.CreateIdleAgent(Centre(Doorstep));

            _world.IsArmed(worker).Should().BeFalse();

            _world.TickFrames(300);

            _world.IsArmed(worker).Should().BeTrue("a shift at a camp is an enlistment");
        }

        /// <summary>
        /// Nobody new appears. A camp that produced a second body would make soldiers free in the only
        /// currency that matters, and it would owe an entity creation per batch; taking the worker costs a
        /// pair of hands and costs the simulation nothing at all.
        /// </summary>
        [Test]
        public void TrainingMakesNoNewBodies()
        {
            _world.CreateCamp(CampCell, Bread, inputStock: 6, craftSeconds: 0.5f);
            _world.CreateIdleAgent(Centre(Doorstep));

            int before = _world.CountAgents();

            _world.TickFrames(300);

            _world.CountAgents().Should().Be(before, "the army comes out of the labour pool");
        }

        [Test]
        public void TrainingEatsTheInput()
        {
            Entity camp = _world.CreateCamp(CampCell, Bread, inputStock: 6, craftSeconds: 0.5f);
            _world.CreateIdleAgent(Centre(Doorstep));

            _world.TickFrames(300);

            _world.SlotOf(camp, Bread).Amount.Should().BeLessThan(6);
        }

        [Test]
        public void ACampWithNothingToEatArmsNobody()
        {
            _world.CreateCamp(CampCell, Bread, inputStock: 0);
            Entity worker = _world.CreateIdleAgent(Centre(Doorstep));

            _world.TickFrames(200);

            _world.IsArmed(worker).Should().BeFalse();
        }

        [Test]
        public void ACampWithNobodyToSendArmsNobody()
        {
            Entity camp = _world.CreateCamp(CampCell, Bread, inputStock: 6);

            _world.TickFrames(200);

            _world.SlotOf(camp, Bread).Amount.Should().Be(6, "a batch is a shift somebody has to work");
        }

        /// <summary>
        /// A soldier walks back out. Standing inside forever would hold a bench the camp needs for the next
        /// recruit, and the exit is the ordinary end-of-shift path rather than anything combat knows about.
        /// </summary>
        [Test]
        public void ASoldierLeavesTheCampAfterwards()
        {
            Entity camp = _world.CreateCamp(CampCell, Bread, inputStock: 6, craftSeconds: 0.5f);
            Entity worker = _world.CreateIdleAgent(Centre(Doorstep));

            _world.TickFrames(400);

            _world.IsArmed(worker).Should().BeTrue();
            _world.IsInside(worker).Should().BeFalse("the shift ended when the enlistment did");
            _world.InteriorOf(camp).Occupied.Should().Be(0);
        }

        /// <summary>
        /// A soldier is not labour. The bit that says it is armed is the same bit the order market skips on,
        /// so "is a soldier" and "is not available for hauling" can never come apart.
        /// </summary>
        [Test]
        public void ASoldierIsNoLongerAHauler()
        {
            _world.CreateCamp(CampCell, Bread, inputStock: 6, craftSeconds: 0.5f);
            Entity worker = _world.CreateIdleAgent(Centre(Doorstep));

            _world.TickFrames(400);

            _world.IsArmed(worker).Should().BeTrue();
            _world.CarryOf(worker).Capacity.Should().Be(0, "a soldier carries nothing");
        }
    }
}
