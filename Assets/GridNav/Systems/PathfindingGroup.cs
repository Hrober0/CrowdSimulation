using Unity.Entities;

namespace GridNav
{
    /// <summary>
    /// Where the navigation data is built from the grid (design §13.1, listed there as RtsNavGroup):
    /// the chunk gate graph and the flow field cache. Bounded work per frame - a dirty-chunk rebuild and a
    /// capped number of fields - so a burst of new destinations cannot spike a frame.
    ///
    /// Runs first in the simulation, before the gameplay groups that consume paths.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class PathfindingGroup : ComponentSystemGroup
    {
    }
}
