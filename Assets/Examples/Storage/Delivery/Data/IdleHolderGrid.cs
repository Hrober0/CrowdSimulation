using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Examples.Storage
{
    // ─────────────────────────────────────────────────────────────────────────
    // Singleton component — lives on a single entity created by the system
    // ─────────────────────────────────────────────────────────────────────────

    public struct HolderSpatilEntry
    {
        public Entity Entity;
        public float3 Position;
    }

    public struct IdleHolderGridSingleton : IComponentData
    {
        public NativeParallelMultiHashMap<int2, HolderSpatilEntry> Cells;
        public float CellSize;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Resumable ring-expansion iterator
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Iterates idle holders in ring-expansion order (ring 0 = origin cell, ring 1 = 8
    /// perimeter cells, ring 2 = 16, …).  Only the exact perimeter cells of each ring
    /// are visited — O(8r) per ring, not O((2r+1)²).
    ///
    /// Usage:
    ///   var cursor = new GridSearchCursor();
    ///   cursor.Init(in grid, sourcePos);
    ///   while (cursor.MoveNext(out HolderSpatilEntry e))
    ///       // process e.Entity / e.Position
    /// </summary>
    public struct GridSearchCursor
    {
        /// <summary>Ring that produced the most recent MoveNext result.</summary>
        public int Ring;

        private int2 _targetCell;
        private int _maxRing;
        private NativeParallelMultiHashMap<int2, HolderSpatilEntry> _cells;

        // Position within the current ring's perimeter sequence
        private int _perimStep;
        private int _perimLen; // 1 for ring 0, 8*ring otherwise

        // Current cell's multi-hash iteration state
        private bool _inCell;
        private HolderSpatilEntry _buffered;
        private NativeParallelMultiHashMapIterator<int2> _cellIt;

        public void Init(in IdleHolderGridSingleton grid, float3 targetPos, float maxRange = 200f)
        {
            _targetCell = new int2(
                (int)math.floor(targetPos.x / grid.CellSize),
                (int)math.floor(targetPos.y / grid.CellSize));
            _maxRing = (int)math.floor(maxRange / grid.CellSize);
            _cells = grid.Cells;
            Ring = 0;
            _perimStep = 0;
            _perimLen = 1; // ring 0 has exactly 1 cell
            _inCell = false;
            _buffered = default;
            _cellIt = default;
        }

        /// <summary>
        /// Advances to the next idle holder entry.
        /// Returns false when the search range is exhausted.
        /// </summary>
        public bool MoveNext(out HolderSpatilEntry entry)
        {
            // Continue walking the entries inside the current cell.
            if (_inCell)
            {
                if (_cells.TryGetNextValue(out _buffered, ref _cellIt))
                {
                    entry = _buffered;
                    return true;
                }

                _inCell = false;
                _perimStep++; // finished this cell — move to the next perimeter step
            }

            // Walk perimeter cells until we find a non-empty one or exhaust the range.
            while (true)
            {
                // Advance to the next ring when the current ring's perimeter is done.
                if (_perimStep >= _perimLen)
                {
                    Ring++;
                    if (Ring > _maxRing)
                    {
                        entry = default;
                        return false;
                    }

                    _perimStep = 0;
                    _perimLen = Ring == 0 ? 1 : 8 * Ring;
                }

                int2 cell = _targetCell + PerimStepToOffset(Ring, _perimStep);
                if (_cells.TryGetFirstValue(cell, out _buffered, out _cellIt))
                {
                    _inCell = true;
                    entry = _buffered;
                    return true;
                }

                _perimStep++;
            }
        }

        // ── Perimeter mapping ─────────────────────────────────────────────────
        // Ring r has 8r perimeter cells traversed clockwise:
        //   Top    (2r+1): dx = -r..r,   dz =  r
        //   Right  (2r)  : dx =  r,      dz =  r-1..-r
        //   Bottom (2r)  : dx =  r-1..-r,dz = -r
        //   Left   (2r-1): dx = -r,      dz = -r+1..r-1
        private static int2 PerimStepToOffset(int ring, int step)
        {
            if (ring == 0) return int2.zero;
            int r = ring;

            if (step <= 2 * r) return new int2(step - r, r); // top
            step -= 2 * r + 1;
            if (step < 2 * r) return new int2(r, r - 1 - step); // right
            step -= 2 * r;
            if (step < 2 * r) return new int2(r - 1 - step, -r); // bottom
            step -= 2 * r;
            /* left */
            return new int2(-r, -r + 1 + step);
        }
    }
}