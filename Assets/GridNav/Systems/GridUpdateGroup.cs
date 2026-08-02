using Unity.Entities;

namespace GridNav
{
    /// <summary>
    /// The grid write phase (design §13.1, listed there as RtsGridGroup).
    ///
    /// Everything that wants to change the grid runs here and enqueues into <see cref="GridEditQueue"/>;
    /// <see cref="GridApplySystem"/>, ordered last, applies the queue. After this group the grid is immutable
    /// for the rest of the frame, which is what lets pathfinding, steering and integration read it from
    /// parallel jobs without a single sync point.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class GridUpdateGroup : ComponentSystemGroup
    {
    }
}
