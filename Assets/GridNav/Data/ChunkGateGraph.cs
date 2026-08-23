using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// The coarse graph a long-range query runs on (design §4.1): gates are nodes, and inside each chunk
    /// every reachable gate pair is an edge whose cost is the length of the path between them *within that
    /// chunk*. Gates cross the map; flow fields handle the crowded last stretch.
    ///
    /// Everything is fixed-size and indexed, so a rebuild overwrites a chunk's slots in place and no index
    /// held anywhere else goes stale. Placing a building dirties one to four chunks and only those are
    /// re-scanned.
    ///
    /// **Edges are directed.** With one-way exits, P -> Q inside a chunk can be walkable while Q -> P is not;
    /// a symmetric graph would report routes the fine layer cannot walk.
    /// </summary>
    public struct ChunkGateGraph : IComponentData, IDisposable
    {
        /// <summary>
        /// A 32-cell border cannot hold more than 16 separate runs, but that needs a picket fence. Eight is
        /// the practical ceiling; anything past it is dropped with a warning rather than silently.
        /// </summary>
        public const int MAX_GATES_PER_BORDER = 8;

        public const int BORDER_COUNT = 2;

        /// <summary>
        /// How many bridges may leave one chunk. Deliberately small: a link gate costs a Dijkstra over the
        /// chunk in every rebuild that touches it, and a chunk with five bridges out of it is a map that wants
        /// a road.
        /// </summary>
        public const int MAX_LINK_GATES_PER_CHUNK = 4;

        public const int GATES_PER_CHUNK = MAX_GATES_PER_BORDER * BORDER_COUNT + MAX_LINK_GATES_PER_CHUNK;

        /// <summary>
        /// Own borders and links, plus the west and south neighbours' borders, plus the links from elsewhere
        /// that land here - which is the one part that cannot be found by looking at a fixed set of neighbours,
        /// since a bridge may come from any chunk on the map.
        /// </summary>
        public const int MAX_GATES_TOUCHING_CHUNK =
            GATES_PER_CHUNK + MAX_GATES_PER_BORDER * 2 + MAX_LINK_GATES_PER_CHUNK;

        public const ushort UNREACHABLE = ushort.MaxValue;

        private const uint NEVER_BUILT = uint.MaxValue;

        // A rebuild is parallel over dirty chunks and each one writes only its own slots, but those slots are
        // spread across these arrays rather than sitting at the job index, so the per-index restriction has
        // to be lifted. The disjointness is guaranteed by the indexing scheme, not by the job system.
        [NativeDisableParallelForRestriction] private NativeArray<ChunkGate> _gates;
        [NativeDisableParallelForRestriction] private NativeArray<int> _touching;
        [NativeDisableParallelForRestriction] private NativeArray<byte> _touchingCount;
        [NativeDisableParallelForRestriction] private NativeArray<ushort> _edgeCosts;
        [NativeDisableParallelForRestriction] private NativeArray<uint> _builtVersion;

        private readonly int2 _chunkCount;

        public ChunkGateGraph(int2 chunkCount, Allocator allocator)
        {
            _chunkCount = math.max(chunkCount, new int2(1, 1));
            int chunks = _chunkCount.x * _chunkCount.y;

            _gates = new NativeArray<ChunkGate>(chunks * GATES_PER_CHUNK, allocator);
            _touching = new NativeArray<int>(chunks * MAX_GATES_TOUCHING_CHUNK, allocator);
            _touchingCount = new NativeArray<byte>(chunks, allocator);
            _edgeCosts = new NativeArray<ushort>(
                chunks * MAX_GATES_TOUCHING_CHUNK * MAX_GATES_TOUCHING_CHUNK,
                allocator
            );
            _builtVersion = new NativeArray<uint>(chunks, allocator);

            for (int i = 0; i < chunks; i++)
            {
                _builtVersion[i] = NEVER_BUILT;
            }
        }

        public bool IsCreated => _gates.IsCreated;

        public int2 ChunkCount => _chunkCount;

        public int ChunkTotal => _chunkCount.x * _chunkCount.y;

        /// <summary>Number of gate slots, valid or not - the bound a search sizes its state arrays by.</summary>
        public int GateSlotTotal => ChunkTotal * GATES_PER_CHUNK;

        public int ChunkIndex(int2 chunkCoord) => chunkCoord.y * _chunkCount.x + chunkCoord.x;

        public int2 ChunkCoordOf(int chunkIndex) =>
            new(chunkIndex % _chunkCount.x, chunkIndex / _chunkCount.x);

        public bool ChunkInBounds(int2 chunkCoord) =>
            math.all(chunkCoord >= 0) && math.all(chunkCoord < _chunkCount);

        /// <summary>
        /// How many slots a kind of gate gets per chunk. Links get fewer than borders, so the bases below are
        /// not a simple multiple and have to be asked for rather than computed inline.
        /// </summary>
        public static int SlotsOf(GateBorder border) =>
            border == GateBorder.Link ? MAX_LINK_GATES_PER_CHUNK : MAX_GATES_PER_BORDER;

        private static int BaseOf(GateBorder border) => border switch
        {
            GateBorder.East => 0,
            GateBorder.North => MAX_GATES_PER_BORDER,
            _ => MAX_GATES_PER_BORDER * BORDER_COUNT,
        };

        public int GateIndex(int chunkIndex, GateBorder border, int slot) =>
            chunkIndex * GATES_PER_CHUNK + BaseOf(border) + slot;

        public ChunkGate GetGate(int gateIndex) => _gates[gateIndex];

        public int GateOwnerChunk(int gateIndex) => gateIndex / GATES_PER_CHUNK;

        public GateBorder GateBorderOf(int gateIndex)
        {
            int within = gateIndex % GATES_PER_CHUNK;

            if (within >= BaseOf(GateBorder.Link))
            {
                return GateBorder.Link;
            }

            return within >= MAX_GATES_PER_BORDER ? GateBorder.North : GateBorder.East;
        }

        /// <summary>What crossing this gate costs on top of stepping onto the far cell. Zero at a border.</summary>
        public ushort CrossCostOf(int gateIndex) => _gates[gateIndex].CrossCost;

        /// <summary>The chunk on the far side of the gate from its owner.</summary>
        public int GateNeighbourChunk(int gateIndex)
        {
            // A link's far chunk is wherever the bridge lands, so it is recorded rather than derived; a
            // border's is the neighbour on that side, which is the whole meaning of the border.
            if (GateBorderOf(gateIndex) == GateBorder.Link)
            {
                int far = _gates[gateIndex].FarChunk;
                return far >= 0 && far < ChunkTotal ? far : -1;
            }

            int2 owner = ChunkCoordOf(GateOwnerChunk(gateIndex));
            int2 neighbour = owner + (GateBorderOf(gateIndex) == GateBorder.East
                ? new int2(1, 0)
                : new int2(0, 1));

            return ChunkInBounds(neighbour) ? ChunkIndex(neighbour) : -1;
        }

        /// <summary>The chunk a gate leads to when entered from <paramref name="chunkIndex"/>.</summary>
        public int OtherChunkOf(int gateIndex, int chunkIndex)
        {
            int owner = GateOwnerChunk(gateIndex);
            return chunkIndex == owner ? GateNeighbourChunk(gateIndex) : owner;
        }

        /// <summary>The gate's cell that lies inside <paramref name="chunkIndex"/>.</summary>
        public int2 CellInChunk(int gateIndex, int chunkIndex)
        {
            ChunkGate gate = _gates[gateIndex];
            return chunkIndex == GateOwnerChunk(gateIndex) ? gate.CellA : gate.CellB;
        }

        /// <summary>Whether the gate may be crossed when leaving <paramref name="chunkIndex"/>.</summary>
        public bool CanCrossFrom(int gateIndex, int chunkIndex)
        {
            ChunkGate gate = _gates[gateIndex];
            GateCrossing needed = chunkIndex == GateOwnerChunk(gateIndex)
                ? GateCrossing.AToB
                : GateCrossing.BToA;

            return (gate.Crossing & needed) != 0;
        }

        public int TouchingCount(int chunkIndex) => _touchingCount[chunkIndex];

        public int TouchingGate(int chunkIndex, int localIndex) =>
            _touching[chunkIndex * MAX_GATES_TOUCHING_CHUNK + localIndex];

        public int LocalIndexOf(int chunkIndex, int gateIndex)
        {
            int count = _touchingCount[chunkIndex];
            int start = chunkIndex * MAX_GATES_TOUCHING_CHUNK;
            for (int i = 0; i < count; i++)
            {
                if (_touching[start + i] == gateIndex)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Cost of walking inside the chunk from one of its gates to another, or <see cref="UNREACHABLE"/>.
        /// Both indices are positions in the chunk's touching list, not gate indices.
        /// </summary>
        public ushort EdgeCost(int chunkIndex, int fromLocal, int toLocal) =>
            _edgeCosts[EdgeIndex(chunkIndex, fromLocal, toLocal)];

        public bool NeedsRebuild(int chunkIndex, uint passabilityVersion) =>
            _builtVersion[chunkIndex] != passabilityVersion;

        private int EdgeIndex(int chunkIndex, int fromLocal, int toLocal) =>
            (chunkIndex * MAX_GATES_TOUCHING_CHUNK + fromLocal) * MAX_GATES_TOUCHING_CHUNK + toLocal;

        internal void ClearGates(int chunkIndex)
        {
            int start = chunkIndex * GATES_PER_CHUNK;
            for (int i = 0; i < GATES_PER_CHUNK; i++)
            {
                _gates[start + i] = default;
            }
        }

        internal void SetGate(int gateIndex, ChunkGate gate) => _gates[gateIndex] = gate;

        internal void SetTouching(int chunkIndex, int localIndex, int gateIndex) =>
            _touching[chunkIndex * MAX_GATES_TOUCHING_CHUNK + localIndex] = gateIndex;

        internal void SetTouchingCount(int chunkIndex, int count) => _touchingCount[chunkIndex] = (byte)count;

        internal void SetEdgeCost(int chunkIndex, int fromLocal, int toLocal, ushort cost) =>
            _edgeCosts[EdgeIndex(chunkIndex, fromLocal, toLocal)] = cost;

        internal void SetBuiltVersion(int chunkIndex, uint version) => _builtVersion[chunkIndex] = version;

        public void Dispose()
        {
            if (_gates.IsCreated)
            {
                _gates.Dispose();
            }

            if (_touching.IsCreated)
            {
                _touching.Dispose();
            }

            if (_touchingCount.IsCreated)
            {
                _touchingCount.Dispose();
            }

            if (_edgeCosts.IsCreated)
            {
                _edgeCosts.Dispose();
            }

            if (_builtVersion.IsCreated)
            {
                _builtVersion.Dispose();
            }
        }
    }
}
