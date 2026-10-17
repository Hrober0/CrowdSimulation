using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using Rts;
using Unity.Entities;

namespace Tests.EditorTests.RtsTests
{
    /// <summary>
    /// The economy's rate manager (Docs/RtsBattleScaling.md P0).
    ///
    /// The bug it exists for was a hang, so the first test would never return without the fix rather than
    /// fail: the stock manager counted the time spent inside a slow tick towards the next one, and a group
    /// whose tick outlasts its period ran again, and again, inside one update.
    /// </summary>
    public partial class RateManagerTests
    {
        private World _world;
        private SlowGroup _group;
        private SystemHandle _counter;

        [SetUp]
        public void Setup()
        {
            _world = new World("RateManagerTests");
            _group = _world.CreateSystemManaged<SlowGroup>();
            _counter = _world.CreateSystem<SlowCountingSystem>();
            _group.AddSystemToUpdateList(_counter);
        }

        [TearDown]
        public void Teardown() => _world.Dispose();

        private int Runs => _world.Unmanaged.GetUnsafeSystemRef<SlowCountingSystem>(_counter).Runs;

        /// <summary>
        /// A tick three times longer than the period still runs once per update of its group. Without the
        /// fix this test does not fail, it never finishes.
        /// </summary>
        [Test]
        public void ATickSlowerThanItsPeriodRunsOncePerUpdate()
        {
            _group.RateManager = new OncePerFrameRateManager(periodMs: 10);

            _group.Update();
            Runs.Should().Be(1);

            _group.Update();
            Runs.Should().Be(2, "a slow tick makes a slow frame, never a second tick inside the same one");
        }

        /// <summary>The period is still honoured: asking again too soon gets nothing.</summary>
        [Test]
        public void AGroupDoesNotRunAgainBeforeItsPeriodIsUp()
        {
            _group.RateManager = new OncePerFrameRateManager(periodMs: 60_000);

            _group.Update();
            _group.Update();
            _group.Update();

            Runs.Should().Be(1);
        }

        /// <summary>Time pushed for the tick is popped after it, so the world's clock is left as it was found.</summary>
        [Test]
        public void TheWorldClockIsRestoredAfterATick()
        {
            _group.RateManager = new OncePerFrameRateManager(periodMs: 10);
            double before = _world.Time.ElapsedTime;

            _group.Update();

            _world.Time.ElapsedTime.Should().Be(before);
        }

        [DisableAutoCreation]
        private partial class SlowGroup : ComponentSystemGroup
        {
        }

        [DisableAutoCreation]
        private partial struct SlowCountingSystem : ISystem
        {
            public int Runs;

            public void OnUpdate(ref SystemState state)
            {
                Runs++;

                // Three periods long, measured by the same wall clock the rate manager reads.
                Thread.Sleep(30);
            }
        }
    }
}
