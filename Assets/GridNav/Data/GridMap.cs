using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

        /// <summary>
        /// How many <see cref="NavLink"/>s the map can hold at once.
        ///
        /// Fixed and indexed, like the gate graph, so a slot can be overwritten in place and nothing that
        /// holds an index goes stale. Five kilobytes for the lot, and a map with more than this many bridges
        /// on it has a design problem rather than a capacity one.
        /// </summary>
        public const int MAX_LINKS = 256;

        private const int CHUNK_SHIFT = 5; // log2(CHUNK_SIZE)
        private const int CHUNK_MASK = CHUNK_SIZE - 1;

        // Parallel jobs read the whole map - a flow field looks at cells all over its window, not at "its"
        // index - so the per-index restriction of IJobParallelFor does not apply here. Writing stays safe
        // through the assembly boundary: only GridApplySystem can write, from a single job.
        [NativeDisableParallelForRestriction] private NativeArray<ushort> _costSum;
        [NativeDisableParallelForRestriction] private NativeArray<byte> _flags;
        [NativeDisableParallelForRestriction] private NativeArray<byte> _exits;

        // What is standing on the cell, for the seekers that can knock it down. Health zero means nothing is
        // there, which is why a structure is never stored with none left - it is cleared instead.
        [NativeDisableParallelForRestriction] private NativeArray<ushort> _structureHealth;
        [NativeDisableParallelForRestriction] private NativeArray<byte> _structureOwner;
        [NativeDisableParallelForRestriction] private NativeArray<ChunkVersions> _versions;
        [NativeDisableParallelForRestriction] private NativeArray<NavLink> _links;

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
            _structureHealth = new NativeArray<ushort>(cells, allocator);
            _structureOwner = new NativeArray<byte>(cells, allocator);
            _versions = new NativeArray<ChunkVersions>(chunks, allocator);
            _links = new NativeArray<NavLink>(MAX_LINKS, allocator);

            LowDamage = 0;
            HighDamage = 0;

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

        /// <summary>
        /// Damage per shot for <see cref="BreachClass.Low"/> and <see cref="BreachClass.High"/>.
        ///
        /// On the map rather than on the agent, and that is deliberate. What a class *means* has to be one
        /// answer for the whole world, because it is what the shared fields were built with - if two units
        /// in the same class disagreed about it they would need two fields, which is the thing classes exist
        /// to prevent. An individual unit's weapon decides which class it belongs to, never what the class
        /// is worth.
        /// </summary>
        public ushort LowDamage;

        public ushort HighDamage;

        /// <summary>What one shot is worth in walking, in cost units. See <see cref="BreachCost"/>.</summary>
        public const int COST_PER_SHOT = NavCost.STEP;

        /// <summary>
        /// What a structure standing on a cell contributes to <see cref="GetCost(int2)"/>.
        ///
        /// A contract rather than an observation: whoever puts a building down adds exactly this, so that
        /// a seeker able to break it can take exactly this back out and substitute the breach price. If a
        /// placement ever adds some other amount, the breach cost is wrong by the difference and nothing
        /// says so.
        /// </summary>
        public const ushort STRUCTURE_BLOCK = CellData.BLOCKED;

        /// <summary>
        /// What it costs to go *through* a structure instead of round it: how many shots it takes, priced as
        /// that many cells of walking.
        ///
        /// Health over damage is the whole model, and the reason it is the right one is that it comes out in
        /// the same currency as the rest of the field - time. A route is then a straight comparison between
        /// seconds spent walking and seconds spent hitting, and the pathfinder picks the cheaper without
        /// anybody weighting anything.
        ///
        /// It saturates rather than growing without bound, and it has to: costs live under
        /// <see cref="CellData.BLOCKED"/>, so the most a breach can ever be worth is about twenty-five cells
        /// of detour. Past that the answer is "go round", which is what returning BLOCKED says.
        /// </summary>
        public ushort BreachCost(ushort health, BreachClass breach)
        {
            int damage = breach switch
            {
                BreachClass.Low => LowDamage,
                BreachClass.High => HighDamage,
                _ => 0,
            };

            if (damage <= 0 || health == 0)
            {
                return CellData.BLOCKED;
            }

            // Rounded up: half a shot still costs a whole one.
            int shots = (health + damage - 1) / damage;
            int cost = shots * COST_PER_SHOT;

            return cost >= CellData.BLOCKED ? CellData.BLOCKED : (ushort)cost;
        }

        /// <summary>Health of the structure on the cell, or zero if there is none.</summary>
        public ushort GetStructureHealth(int2 cell) =>
            InBounds(cell) ? _structureHealth[CellIndex(cell)] : (ushort)0;

        /// <summary>Which faction owns the structure on the cell. Meaningless where health is zero.</summary>
        public byte GetStructureOwner(int2 cell) =>
            InBounds(cell) ? _structureOwner[CellIndex(cell)] : (byte)0;

        /// <summary>
        /// What the cell costs *this* seeker (design §14 step 13, amended).
        ///
        /// Three rules, and between them they replace the two-field design this started as:
        ///
        /// - a seeker that cannot break anything pays the stored cost, so a structure is a wall — this is the
        ///   civilian case, and it is the same arithmetic rather than a separate path;
        /// - a seeker standing in front of **its own** structure pays the stored cost too, so an army never
        ///   routes through its own bakery;
        /// - anyone else pays the walk without the wall, plus the price of knocking it down.
        /// </summary>
        public ushort GetCost(int2 cell, Traversal traversal)
        {
            if (!InBounds(cell))
            {
                return CellData.BLOCKED;
            }

            int index = CellIndex(cell);
            ushort stored = _costSum[index];
            ushort health = _structureHealth[index];

            if (health == 0 || !traversal.CanBreach || _structureOwner[index] == traversal.Faction)
            {
                return stored;
            }

            ushort breach = BreachCost(health, traversal.Breach);
            if (breach >= CellData.BLOCKED)
            {
                return stored;
            }

            // The wall comes out of the sum and the breach goes in. Whatever else is on the cell - the
            // terrain underneath, a road, a tree that grew against it - is still paid for.
            int without = stored - STRUCTURE_BLOCK;
            return (ushort)math.clamp(without + breach, 0, CellData.BLOCKED);
        }

        /// <summary>Whether this seeker may enter at all. See <see cref="GetCost(int2, Traversal)"/>.</summary>
        public bool IsPassable(int2 cell, Traversal traversal) => GetCost(cell, traversal) < CellData.BLOCKED;

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
        public bool CanTraverse(int2 from, Direction direction, Traversal traversal = default)
        {
            if (!InBounds(from))
            {
                return false;
            }

            int index = CellIndex(from);
            return IsPassable(from, traversal)
                   && DirectionUtils.Allows(_exits[index], direction)
                   && IsPassable(from + DirectionUtils.Offset(direction), traversal);
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
        public bool CanTraverseFromNeighbour(int2 cell, Direction neighbourDirection,
                                             Traversal traversal = default) =>
            CanTraverse(cell + DirectionUtils.Offset(neighbourDirection),
                        DirectionUtils.Opposite(neighbourDirection), traversal);

        public ChunkVersions GetChunkVersions(int2 chunkCoord) =>
            ChunkInBounds(chunkCoord) ? _versions[ChunkIndex(chunkCoord)] : default;

        public ChunkVersions GetChunkVersionsAtCell(int2 cell) => GetChunkVersions(ChunkCoordOf(cell));

        /// <summary>Number of link slots, valid or not. Iterate this to find every link on the map.</summary>
        public int LinkSlotCount => MAX_LINKS;

        public NavLink GetLink(int slot) => _links[slot];

        /// <summary>
        /// The link that starts on this cell, if any.
        ///
        /// The flag test in front of the scan is the whole reason this is cheap enough to call from inside a
        /// Dijkstra: almost every cell answers no on one byte it was going to read anyway, and only the mouth
        /// of a bridge pays for the walk over the table.
        /// </summary>
        public bool TryGetLinkFrom(int2 cell, out NavLink link)
        {
            link = default;

            if ((GetFlags(cell) & CellFlags.LinkEntry) == 0)
            {
                return false;
            }

            for (int slot = 0; slot < MAX_LINKS; slot++)
            {
                NavLink candidate = _links[slot];
                if (candidate.IsValid && candidate.From.Equals(cell))
                {
                    link = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>The link that ends on this cell, if any. What a backwards search needs.</summary>
        public bool TryGetLinkTo(int2 cell, out NavLink link)
        {
            link = default;

            if ((GetFlags(cell) & CellFlags.LinkExit) == 0)
            {
                return false;
            }

            for (int slot = 0; slot < MAX_LINKS; slot++)
            {
                NavLink candidate = _links[slot];
                if (candidate.IsValid && candidate.To.Equals(cell))
                {
                    link = candidate;
                    return true;
                }
            }

            return false;
        }

        internal void Apply(in GridEdit edit)
        {
            // Two-cell operations before the single-cell index, because neither end is "the" cell.
            switch (edit.Op)
            {
                case GridEdit.OpType.AddLink:
                    ApplyAddLink(edit.Cell, edit.Other, (ushort)math.clamp(edit.Value, 1, ushort.MaxValue));
                    return;

                case GridEdit.OpType.RemoveLink:
                    ApplyRemoveLink(edit.Cell, edit.Other);
                    return;
            }

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

                case GridEdit.OpType.SetStructure:
                    ApplyStructure(index, edit.Cell, (byte)(edit.Value & 0xFF),
                                   (ushort)math.clamp(edit.Value >> 8, 0, ushort.MaxValue));
                    break;
            }
        }

        /// <summary>
        /// Records what is standing here. Bumps only <see cref="ChunkVersions.StructureVersion"/>, because
        /// nothing about a structure's health changes what the cell costs anybody who cannot break it - and
        /// the whole point of the separate counter is that a fight leaves the economy's fields alone.
        /// </summary>
        private void ApplyStructure(int index, int2 cell, byte owner, ushort health)
        {
            if (_structureHealth[index] == health && _structureOwner[index] == owner)
            {
                return;
            }

            _structureHealth[index] = health;
            _structureOwner[index] = health == 0 ? (byte)0 : owner;

            BumpStructureVersions(cell);
        }

        /// <summary>
        /// Marks the structure change on the cell's chunk **and on the chunk across any border it sits on**.
        ///
        /// The neighbour is not an afterthought: a border cell is half of a border *pair*, and the gate that
        /// pair forms is owned by whichever chunk's east or north edge it is. A wall battered down in the
        /// first column of one chunk opens a gate that belongs to the chunk to its west, and bumping only its
        /// own chunk leaves that gate shut - a hole in the wall the long-range router cannot see. This is the
        /// same rule cost changes follow, for the same reason.
        /// </summary>
        private void BumpStructureVersions(int2 cell)
        {
            int2 chunkCoord = ChunkCoordOf(cell);
            BumpStructureChunk(chunkCoord);

            int2 local = LocalCoordOf(cell);
            if (local.x == 0)
            {
                BumpStructureChunk(chunkCoord + new int2(-1, 0));
            }
            else if (local.x == CHUNK_MASK)
            {
                BumpStructureChunk(chunkCoord + new int2(1, 0));
            }

            if (local.y == 0)
            {
                BumpStructureChunk(chunkCoord + new int2(0, -1));
            }
            else if (local.y == CHUNK_MASK)
            {
                BumpStructureChunk(chunkCoord + new int2(0, 1));
            }
        }

        private void BumpStructureChunk(int2 chunkCoord)
        {
            if (!ChunkInBounds(chunkCoord))
            {
                return;
            }

            int chunkIndex = ChunkIndex(chunkCoord);
            ChunkVersions versions = _versions[chunkIndex];
            versions.StructureVersion++;
            _versions[chunkIndex] = versions;
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

        /// <summary>
        /// Records a one-way crossing and marks both its mouths.
        ///
        /// **A cell may be the mouth of one link only**, and the refusal is loud rather than silent. Allowing
        /// two would mean every lookup returns a set, every search has to try each of them, and "which bridge
        /// is this agent standing on" stops having an answer - all to support two bridges sharing a doorstep,
        /// which is an authoring mistake in every case anyone has thought of.
        ///
        /// Both ends bump <c>PassabilityVersion</c>, which is what re-scans the gates of the two chunks a
        /// bridge joins. A link is passability in every sense that matters: it changes where an agent standing
        /// on one cell can get to.
        /// </summary>
        private void ApplyAddLink(int2 from, int2 to, ushort cost)
        {
            if (!InBounds(from) || !InBounds(to) || from.Equals(to))
            {
                Debug.LogWarning("[GridNav] Dropped a link whose ends are off the map or the same cell.");
                return;
            }

            if ((GetFlags(from) & CellFlags.LinkEntry) != 0 || (GetFlags(to) & CellFlags.LinkExit) != 0)
            {
                Debug.LogWarning("[GridNav] Dropped a link: a cell may be the mouth of one link only.");
                return;
            }

            int slot = FreeLinkSlot();
            if (slot < 0)
            {
                Debug.LogWarning("[GridNav] Dropped a link: the map already holds as many as it can.");
                return;
            }

            _links[slot] = new NavLink { From = from, To = to, Cost = cost };

            _flags[CellIndex(from)] |= (byte)CellFlags.LinkEntry;
            _flags[CellIndex(to)] |= (byte)CellFlags.LinkExit;

            BumpVersions(from, true);
            BumpVersions(to, true);
        }

        private void ApplyRemoveLink(int2 from, int2 to)
        {
            for (int slot = 0; slot < MAX_LINKS; slot++)
            {
                NavLink link = _links[slot];
                if (!link.IsValid || !link.From.Equals(from) || !link.To.Equals(to))
                {
                    continue;
                }

                _links[slot] = default;

                if (InBounds(from))
                {
                    int index = CellIndex(from);
                    _flags[index] = Without(_flags[index], CellFlags.LinkEntry);
                    BumpVersions(from, true);
                }

                if (InBounds(to))
                {
                    int index = CellIndex(to);
                    _flags[index] = Without(_flags[index], CellFlags.LinkExit);
                    BumpVersions(to, true);
                }

                return;
            }
        }

        private static byte Without(byte flags, CellFlags remove) => (byte)(flags & ~(byte)remove);

        private int FreeLinkSlot()
        {
            for (int slot = 0; slot < MAX_LINKS; slot++)
            {
                if (!_links[slot].IsValid)
                {
                    return slot;
                }
            }

            return -1;
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

            if (_structureHealth.IsCreated)
            {
                _structureHealth.Dispose();
            }

            if (_structureOwner.IsCreated)
            {
                _structureOwner.Dispose();
            }

            if (_versions.IsCreated)
            {
                _versions.Dispose();
            }

            if (_links.IsCreated)
            {
                _links.Dispose();
            }
        }
    }
}
