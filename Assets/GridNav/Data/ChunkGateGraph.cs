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
        public const int GATES_PER_CHUNK = MAX_GATES_PER_BORDER * BORDER_COUNT;

        /// <summary>Own two borders plus the west and south neighbours', which the chunk also touches.</summary>
        public const int MAX_GATES_TOUCHING_CHUNK = GATES_PER_CHUNK * 2;

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

        public int GateIndex(int chunkIndex, GateBorder border, int slot) =>
            chunkIndex * GATES_PER_CHUNK + (int)border * MAX_GATES_PER_BORDER + slot;

        public ChunkGate GetGate(int gateIndex) => _gates[gateIndex];

        public int GateOwnerChunk(int gateIndex) => gateIndex / GATES_PER_CHUNK;

        public GateBorder GateBorderOf(int gateIndex) =>
            (GateBorder)(gateIndex % GATES_PER_CHUNK / MAX_GATES_PER_BORDER);

        /// <summary>The chunk on the far side of the gate from its owner.</summary>
        public int GateNeighbourChunk(int gateIndex)
        {
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
