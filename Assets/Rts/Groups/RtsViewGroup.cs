using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// One-way sync of simulation state onto the pooled GameObject views, plus debug overlays
    /// (design §10, §13.1). Nothing here writes simulation state.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class RtsViewGroup : ComponentSystemGroup
    {
    }
}
