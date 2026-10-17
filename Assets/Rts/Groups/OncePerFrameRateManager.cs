using System.Diagnostics;
using Unity.Core;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Runs a group at most every <c>period</c>, and **never more than once per update of the group** (design
    /// §13.1, Docs/RtsBattleScaling.md P0).
    ///
    /// Replaces <see cref="RateUtils.VariableRateManager"/>, which had a failure worse than slowness.
    /// <see cref="ComponentSystemGroup"/> runs <c>while (ShouldGroupUpdate) UpdateAllSystems()</c>, and the
    /// stock manager counts the wall-clock time spent *inside* the group's own update towards the next period.
    /// A tick that takes longer than the period has therefore already earned the next one by the time it ends,
    /// and the loop goes round again - for ever, inside one frame. 300 soldiers a side made one tick take
    /// 450 ms against a 100 ms period, and the Editor never came back.
    ///
    /// Here the second question in a frame is always no. A slow tick makes a slow frame, which is a thing
    /// somebody can see and profile; it can no longer make a hang.
    ///
    /// Time is pushed onto the world the same way the stock manager does it, so the group's systems see a
    /// <c>DeltaTime</c> that is the real gap between ticks and an <c>ElapsedTime</c> of their own. Nothing in
    /// the economy counts ticks to measure time, which is what makes skipping a catch-up harmless.
    /// </summary>
    public sealed class OncePerFrameRateManager : IRateManager
    {
        private readonly long _periodTicks;

        private long _lastRun;
        private bool _hasRun;
        private double _elapsed;

        /// <summary>True between a yes and the question that follows it, which is the end of that update.</summary>
        private bool _pushedTime;

        public OncePerFrameRateManager(uint periodMs)
        {
            _periodTicks = periodMs * Stopwatch.Frequency / 1000;
        }

        public float Timestep { get; set; }

        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
            // Asked again straight after running: this update of the group is over, whatever the clock says.
            if (_pushedTime)
            {
                group.World.PopTime();
                _pushedTime = false;
                return false;
            }

            long now = Stopwatch.GetTimestamp();
            if (_hasRun && now - _lastRun < _periodTicks)
            {
                return false;
            }

            Timestep = _hasRun
                ? (float)((double)(now - _lastRun) / Stopwatch.Frequency)
                : (float)((double)_periodTicks / Stopwatch.Frequency);

            _lastRun = now;
            _hasRun = true;
            _elapsed += Timestep;

            group.World.PushTime(new TimeData(elapsedTime: _elapsed, deltaTime: Timestep));
            _pushedTime = true;
            return true;
        }
    }
}
