using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace GridNav
{
    /// <summary>
    /// The one grid the game runs on. Navigation and building placement share it, so no code ever converts
    /// between two cell spaces and one cell is exactly one world unit (design §3).
    ///
    /// Storage is chunked SoA over 32x32 chunks: a chunk's 1024 cells are contiguous, which is what the
    /// per-chunk searches of the gate graph and the flow-field window want. Four bytes per cell, so a
    /// 512x512 map is 1 MB.
    ///
    /// Reading is public and safe from parallel jobs. Writing is internal to GridNav: everyone else describes
    /// a change as a <see cref="GridEdit"/> and GridApplySystem applies it (§13.2, invariant 1).
    /// </summary>
    public struct GridMap : IDisposable
    {
        public const int CHUNK_SIZE = 32;
        public const int CELLS_PER_CHUNK = CHUNK_SIZE * CHUNK_SIZE;

        private const int CHUNK_SHIFT = 5; // log2(CHUNK_SIZE)
        private const int CHUNK_MASK = CHUNK_SIZE - 1;

        private NativeArray<ushort> _costSum;
        private NativeArray<byte> _flags;
        private NativeArray<byte> _exits;
        private NativeArray<ChunkVersions> _versions;

        private readonly int2 _minCell;
        private readonly int2 _chunkCount;

        /// <param name="minCell">Cell coordinate of the map's lower-left corner. May be negative.</param>
        /// <param name="chunkCount">Map size in chunks, so the cell size is always a multiple of 32.</param>
        public GridMap(int2 minCell, int2 chunkCount, Allocator allocator)
        {
            _minCell = minCell;
            _chunkCount = math.max(chunkCount, new int2(1, 1));

            int chunks = _chunkCount.x * _chunkCount.y;
            int cells = chunks * CELLS_PER_CHUNK;

            _costSum = new NativeArray<ushort>(cells, allocator);
            _flags = new NativeArray<byte>(cells, allocator);
            _exits = new NativeArray<byte>(cells, allocator);
            _versions = new NativeArray<ChunkVersions>(chunks, allocator);

            for (int i = 0; i < cells; i++)
            {
                _exits[i] = DirectionUtils.ALL_EXITS;
            }
        }

        public bool IsCreated => _costSum.IsCreated;

        /// <summary>Lower-left cell, inclusive.</summary>
        public int2 MinCell => _minCell;

        /// <summary>Upper-right cell, inclusive.</summary>
        public int2 MaxCell => _minCell + SizeInCells - 1;

        public int2 SizeInCells => _chunkCount * CHUNK_SIZE;

        public int2 ChunkCount => _chunkCount;

        public int CellCount => _chunkCount.x * _chunkCount.y * CELLS_PER_CHUNK;

        public bool InBounds(int2 cell) => math.all(cell >= _minCell) && math.all(cell <= MaxCell);

        /// <summary>Map-relative chunk coordinate, 0 .. <see cref="ChunkCount"/> - 1.</summary>
        public int2 ChunkCoordOf(int2 cell) => (cell - _minCell) >> CHUNK_SHIFT;

        public int2 LocalCoordOf(int2 cell) => (cell - _minCell) & CHUNK_MASK;

        /// <summary>Lower-left cell of a map-relative chunk coordinate.</summary>
        public int2 ChunkMinCell(int2 chunkCoord) => _minCell + (chunkCoord << CHUNK_SHIFT);

        public bool ChunkInBounds(int2 chunkCoord) =>
            math.all(chunkCoord >= 0) && math.all(chunkCoord < _chunkCount);

        /// <summary>
        /// Index of a cell in the SoA arrays. Chunk-major, then row-major inside the chunk, so one chunk is
        /// a contiguous run of 1024 entries.
        /// </summary>
        public int CellIndex(int2 cell)
        {
            int2 local = cell - _minCell;
            int2 chunkCoord = local >> CHUNK_SHIFT;
            int2 inChunk = local & CHUNK_MASK;
            return (chunkCoord.y * _chunkCount.x + chunkCoord.x) * CELLS_PER_CHUNK
                   + inChunk.y * CHUNK_SIZE
                   + inChunk.x;
        }

        public int ChunkIndex(int2 chunkCoord) => chunkCoord.y * _chunkCount.x + chunkCoord.x;

        public CellData GetCell(int2 cell)
        {
            if (!InBounds(cell))
            {
                return CellData.OutOfBounds;
            }

            int index = CellIndex(cell);
            return new CellData(_costSum[index], (CellFlags)_flags[index], _exits[index]);
        }

        public ushort GetCost(int2 cell) => InBounds(cell) ? _costSum[CellIndex(cell)] : CellData.BLOCKED;

        public CellFlags GetFlags(int2 cell) => InBounds(cell) ? (CellFlags)_flags[CellIndex(cell)] : CellFlags.None;

        public byte GetExits(int2 cell) => InBounds(cell) ? _exits[CellIndex(cell)] : DirectionUtils.NO_EXITS;

        /// <summary>Outside the map counts as blocked, so no caller needs a bounds check before asking.</summary>
        public bool IsPassable(int2 cell) => InBounds(cell) && _costSum[CellIndex(cell)] < CellData.BLOCKED;

        /// <summary>
        /// Whether an agent standing on <paramref name="from"/> may step to its neighbour in
        /// <paramref name="direction"/>: both cells passable and the mover's exit bit set.
        /// </summary>
        public bool CanTraverse(int2 from, Direction direction)
        {
            if (!InBounds(from))
            {
                return false;
            }

            int index = CellIndex(from);
            return _costSum[index] < CellData.BLOCKED
                   && DirectionUtils.Allows(_exits[index], direction)
                   && IsPassable(from + DirectionUtils.Offset(direction));
        }

        /// <summary>
        /// Whether the neighbour of <paramref name="cell"/> in <paramref name="neighbourDirection"/> may step
        /// onto <paramref name="cell"/>.
        ///
        /// This is the test flow-field generation needs: a field expands *backwards* from the goal, so every
        /// relaxation of "cell -> neighbour" is really the move "neighbour -> cell" and must check the
        /// neighbour's exit bit. Checking the cell's own bit builds a field that sends agents the wrong way
        /// down a one-way road, silently (§3).
        /// </summary>
        public bool CanTraverseFromNeighbour(int2 cell, Direction neighbourDirection) =>
            CanTraverse(cell + DirectionUtils.Offset(neighbourDirection), DirectionUtils.Opposite(neighbourDirection));

        public ChunkVersions GetChunkVersions(int2 chunkCoord) =>
            ChunkInBounds(chunkCoord) ? _versions[ChunkIndex(chunkCoord)] : default;

        public ChunkVersions GetChunkVersionsAtCell(int2 cell) => GetChunkVersions(ChunkCoordOf(cell));

        internal void Apply(in GridEdit edit)
        {
            if (!InBounds(edit.Cell))
            {
                Debug.LogWarning("[GridNav] Dropped a grid edit outside the map bounds.");
                return;
            }

            int index = CellIndex(edit.Cell);
            switch (edit.Op)
            {
                case GridEdit.OpType.CostDelta:
                    ApplyCostDelta(index, edit.Cell, edit.Value);
                    break;

                // Flags annotate a cell, they never change routing, so they bump no version.
                case GridEdit.OpType.AddFlags:
                    _flags[index] |= (byte)edit.Value;
                    break;

                case GridEdit.OpType.RemoveFlags:
                    _flags[index] &= (byte)~edit.Value;
                    break;

                case GridEdit.OpType.SetExits:
                    ApplyExits(index, edit.Cell, (byte)(edit.Value & DirectionUtils.ALL_EXITS));
                    break;
            }
        }

        private void ApplyCostDelta(int index, int2 cell, int delta)
        {
            int current = _costSum[index];
            int next = current + delta;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (next < 0 || next > ushort.MaxValue)
            {
                // The sum has to stay exact for add/remove to be reversible (§3). Reaching either end means a
                // contributor was added twice or removed twice - clamping here only limits the damage.
                Debug.LogError("[GridNav] Cell cost sum under- or overflowed. A cost contributor was added or removed twice.");
            }
#endif

            next = math.clamp(next, 0, ushort.MaxValue);
            if (next == current)
            {
                return;
            }

            _costSum[index] = (ushort)next;

            bool wasPassable = current < CellData.BLOCKED;
            bool isPassable = next < CellData.BLOCKED;
            BumpVersions(cell, wasPassable != isPassable);
        }

        private void ApplyExits(int index, int2 cell, byte exits)
        {
            if (_exits[index] == exits)
            {
                return;
            }

            _exits[index] = exits;

            // An exit mask decides whether a border pair is open, so gates depend on it exactly as they
            // depend on passability (§4.1).
            BumpVersions(cell, true);
        }

        private void BumpVersions(int2 cell, bool passabilityChanged)
        {
            int2 chunkCoord = ChunkCoordOf(cell);
            BumpChunk(chunkCoord, passabilityChanged);

            if (!passabilityChanged)
            {
                return;
            }

            // A border cell is half of a border *pair*, and the pair belongs to the neighbouring chunk's gates
            // as much as to this one's, so that chunk has to re-scan too.
            int2 local = LocalCoordOf(cell);
            if (local.x == 0)
            {
                BumpChunk(chunkCoord + new int2(-1, 0), true);
            }
            else if (local.x == CHUNK_MASK)
            {
                BumpChunk(chunkCoord + new int2(1, 0), true);
            }

            if (local.y == 0)
            {
                BumpChunk(chunkCoord + new int2(0, -1), true);
            }
            else if (local.y == CHUNK_MASK)
            {
                BumpChunk(chunkCoord + new int2(0, 1), true);
            }
        }

        private void BumpChunk(int2 chunkCoord, bool passabilityChanged)
        {
            if (!ChunkInBounds(chunkCoord))
            {
                return;
            }

            int chunkIndex = ChunkIndex(chunkCoord);
            ChunkVersions versions = _versions[chunkIndex];
            versions.CostVersion++;
            if (passabilityChanged)
            {
                versions.PassabilityVersion++;
            }

            _versions[chunkIndex] = versions;
        }

        public void Dispose()
        {
            if (_costSum.IsCreated)
            {
                _costSum.Dispose();
            }

            if (_flags.IsCreated)
            {
                _flags.Dispose();
            }

            if (_exits.IsCreated)
            {
                _exits.Dispose();
            }

            if (_versions.IsCreated)
            {
                _versions.Dispose();
            }
        }
    }
}
