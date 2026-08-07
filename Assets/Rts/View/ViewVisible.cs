using Unity.Entities;

namespace Rts
{
    /// <summary>
    /// Whether this entity should be shown at all (design §10).
    ///
    /// The simulation owns the answer and the view layer only obeys it. An agent that steps inside a building
    /// has this disabled and its pooled GameObject goes straight back to the pool (§6) - which is what makes
    /// "an idle agent in a hut costs nothing per frame" true of the view as well as of the simulation.
    ///
    /// This is *not* a culling flag. Whether a visible agent is close enough to the camera to be worth a
    /// GameObject is the view layer's own decision, changes with the camera rather than with the simulation,
    /// and is deliberately not simulation state.
    /// </summary>
    public struct ViewVisible : IComponentData, IEnableableComponent
    {
    }
}
