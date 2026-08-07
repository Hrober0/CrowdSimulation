using Unity.Mathematics;

namespace Examples.Rts
{
    /// <summary>
    /// Which part of the world is worth a GameObject this frame (design §10).
    ///
    /// A circle around a focus point rather than the camera frustum: it is one distance test per agent, it
    /// reads the same whether <see cref="SimToWorld"/> maps onto XY or XZ, and it needs to know nothing about
    /// the projection. A frustum would be tighter, but the pool exists to bound the *number* of GameObjects,
    /// and a radius bounds that just as well.
    /// </summary>
    public readonly struct ViewCulling
    {
        public readonly float2 Center;

        /// <summary>Zero or less means "do not cull" - every visible agent gets a view.</summary>
        public readonly float Radius;

        public ViewCulling(float2 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public bool IsVisible(float2 position) =>
            Radius <= 0f || math.distancesq(position, Center) <= Radius * Radius;
    }
}
