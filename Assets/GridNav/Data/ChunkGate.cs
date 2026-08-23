using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// A way out of one chunk into another: usually a maximal run of consecutive crossable cell pairs along the
    /// border between the two (design §4.1), and sometimes a <see cref="NavLink"/> that leaves the chunk.
    ///
    /// Named a gate, not a portal: <c>Navigation.Portal</c> already means a navmesh edge with Left/Right
    /// corners for the funnel algorithm, and reusing the word across two modules invites confusion.
    ///
    /// A run is broken by a closed pair *and* by a change of crossing direction, so every gate is uniformly
    /// crossable - no gate is half one-way. A link gate is one way by construction.
    /// </summary>
    public struct ChunkGate
    {
        /// <summary>Representative cell on the owning chunk's side, at the middle of the run.</summary>
        public int2 CellA;

        /// <summary>
        /// Its partner in the chunk on the other side. Adjacent to <see cref="CellA"/> for a border gate; the
        /// far bank of the crossing for a link gate, which may be anywhere on the map.
        /// </summary>
        public int2 CellB;

        /// <summary>Pairs in the run, or 1 for a link. Zero means the slot is empty.</summary>
        public byte Length;

        public GateCrossing Crossing;

        /// <summary>
        /// Chunk <see cref="CellB"/> lives in. Only meaningful for a link gate, where it cannot be derived:
        /// a border gate's far chunk is the neighbour on that side, and a link's is wherever the bridge lands.
        /// </summary>
        public int FarChunk;

        /// <summary>
        /// What crossing costs on top of stepping onto the far cell. Zero for a border gate, where the two
        /// cells are neighbours and the crossing is the step.
        /// </summary>
        public ushort CrossCost;

        public bool IsValid => Length > 0 && Crossing != GateCrossing.None;

        /// <summary>Midpoint of the border the gate spans, for search heuristics.</summary>
        public float2 Center => (GridCoords.CellCenter(CellA) + GridCoords.CellCenter(CellB)) * 0.5f;

        public override string ToString() => $"Gate({CellA} <-> {CellB}, {Crossing}, {Length} wide)";
    }
}
