using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// A maximal run of consecutive crossable cell pairs along the border between two chunks (design §4.1).
    /// Named a gate, not a portal: <c>Navigation.Portal</c> already means a navmesh edge with Left/Right
    /// corners for the funnel algorithm, and reusing the word across two modules invites confusion.
    ///
    /// A run is broken by a closed pair *and* by a change of crossing direction, so every gate is uniformly
    /// crossable - no gate is half one-way.
    /// </summary>
    public struct ChunkGate
    {
        /// <summary>Representative cell on the owning chunk's side, at the middle of the run.</summary>
        public int2 CellA;

        /// <summary>Its partner across the border, in the neighbouring chunk.</summary>
        public int2 CellB;

        /// <summary>Pairs in the run. Zero means the slot is empty.</summary>
        public byte Length;

        public GateCrossing Crossing;

        public bool IsValid => Length > 0 && Crossing != GateCrossing.None;

        /// <summary>Midpoint of the border the gate spans, for search heuristics.</summary>
        public float2 Center => (GridCoords.CellCenter(CellA) + GridCoords.CellCenter(CellB)) * 0.5f;

        public override string ToString() => $"Gate({CellA} <-> {CellB}, {Crossing}, {Length} wide)";
    }
}
