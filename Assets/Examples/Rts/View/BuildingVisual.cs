using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Rts
{
    /// <summary>
    /// How a building should look (design §10). The colour lives on the entity rather than in a lookup from
    /// building type to prefab, so the view layer needs to know nothing about farms, bakeries or huts - it
    /// draws a box of the right size in the right colour and stops there.
    ///
    /// A building without one still works; it simply gets no view.
    /// </summary>
    public struct BuildingVisual : IComponentData
    {
        public float4 Tint;
    }

    /// <summary>
    /// Marks a building whose view has been made, so the view system can tell a new building from one it has
    /// already dealt with without keeping a second list of its own.
    ///
    /// Cleanup data, so it outlives the entity: a demolished building has to have its GameObject taken away,
    /// and by then everything else about it is gone.
    /// </summary>
    public struct BuildingViewMade : ICleanupComponentData
    {
    }
}
