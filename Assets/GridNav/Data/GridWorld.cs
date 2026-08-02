using Unity.Entities;

namespace GridNav
{
    /// <summary>
    /// Singleton handle to the grid and its pending changes.
    ///
    /// Read it with <c>SystemAPI.GetSingleton</c> to path, steer or draw. Take it read-write only to enqueue
    /// into <see cref="Edits"/> - the map itself cannot be written from outside GridNav, which is what keeps
    /// the single-writer invariant (§13.2) true by construction rather than by discipline.
    /// </summary>
    public struct GridWorld : IComponentData
    {
        public GridMap Map;
        public GridEditQueue Edits;
    }
}
