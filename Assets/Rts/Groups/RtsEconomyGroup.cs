using GridNav;
using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Storage requests, order posting, aging and assignment (design §13.1).
    ///
    /// Runs at 10 Hz. Global matching is single-threaded by design - reservation-with-rollback across two
    /// entities has no clean parallel form (§13.2, invariant 4) - so the rate is what keeps the most expensive
    /// work in the frame affordable. At play speed the difference against 60 Hz is not perceptible.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PathfindingGroup))]
    public partial class RtsEconomyGroup : ComponentSystemGroup
    {
        private const uint UPDATE_PERIOD_MS = 100;

        protected override void OnCreate()
        {
            base.OnCreate();

            RateManager = new RateUtils.VariableRateManager(UPDATE_PERIOD_MS);
        }
    }
}
