using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Shape and constants of one flow field (design §4, tier 2).
    ///
    /// A field belongs to a destination, not to an agent: two hundred haulers walking to one warehouse share
    /// a single field. That is the whole reason this tier exists - per-agent paths would repeat the same
    /// search two hundred times.
    /// </summary>
    public static class FlowField
    {
        /// <summary>
        /// Side of the square window a field covers, centred on the destination. Big enough to hold the
        /// crowded approach, small enough that a field is cheap - the whole map would be 786 KB and a
        /// Dijkstra over 262k cells per destination.
        /// </summary>
        public const int WINDOW_SIZE = 128;

        public const int WINDOW_CELLS = WINDOW_SIZE * WINDOW_SIZE;

        /// <summary>No route to the destination from this cell, or the cell is outside the map.</summary>
        public const ushort UNREACHABLE = ushort.MaxValue;

        /// <summary>Stored where there is nowhere better to step: the destination itself, and dead ends.</summary>
        public const byte NO_DIRECTION = 255;

        /// <summary>
        /// Stored where the route carries on over a <see cref="NavLink"/> rather than to a neighbour.
        ///
        /// It is a direction value rather than something the rules layer works out for itself, because the
        /// integration pass is the only thing that knows whether the link *won*. An agent standing on a bridge
        /// mouth whose route does not use the bridge, and one whose route does, are the same agent on the same
        /// cell; only the field can tell them apart, and it already had to decide in order to price the cell.
        /// </summary>
        public const byte LINK_STEP = 254;

        /// <summary>
        /// Where the window sits for a destination: centred on it, then slid to stay on the map. A map
        /// smaller than the window simply starts at its own corner and the overhang reads as unreachable.
        /// </summary>
        public static int2 WindowMinFor(in GridMap map, int2 goalCell)
        {
            int2 centred = goalCell - WINDOW_SIZE / 2;
            int2 lastFit = map.MaxCell - (WINDOW_SIZE - 1);
            return math.max(math.min(centred, lastFit), map.MinCell);
        }

        public static bool Contains(int2 windowMin, int2 cell)
        {
            int2 local = cell - windowMin;
            return math.all(local >= 0) && math.all(local < WINDOW_SIZE);
        }

        public static int LocalIndexOf(int2 windowMin, int2 cell)
        {
            int2 local = cell - windowMin;
            return local.y * WINDOW_SIZE + local.x;
        }

        public static int2 CellOf(int2 windowMin, int localIndex) =>
            windowMin + new int2(localIndex % WINDOW_SIZE, localIndex / WINDOW_SIZE);

        /// <summary>
        /// Sum of the cost versions of every chunk the window touches. Any change under the window moves a
        /// counter up, so the sum moves too and the field is known to be stale (§3).
        /// </summary>
        public static uint VersionStampOf(in GridMap map, int2 windowMin)
        {
            int2 first = map.ChunkCoordOf(math.clamp(windowMin, map.MinCell, map.MaxCell));
            int2 last = map.ChunkCoordOf(math.clamp(windowMin + (WINDOW_SIZE - 1), map.MinCell, map.MaxCell));

            uint stamp = 0;
            for (int y = first.y; y <= last.y; y++)
            {
                for (int x = first.x; x <= last.x; x++)
                {
                    stamp += map.GetChunkVersions(new int2(x, y)).CostVersion;
                }
            }

            return stamp;
        }
    }

    /// <summary>One cached field: which destination it serves, where its window sits, and how fresh it is.</summary>
    public struct FlowFieldSlot
    {
        public int2 GoalCell;
        public int2 WindowMin;
        public uint VersionStamp;
        public int LastUsed;
        public bool Built;
    }
}
