using Unity.Entities;
using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Baked description of the map. <c>GridMapSystem</c> turns the first one it finds into the
    /// <see cref="GridWorld"/> singleton.
    /// </summary>
    public struct GridSettings : IComponentData
    {
        /// <summary>Cell coordinate of the lower-left corner. Negative to centre the map on the origin.</summary>
        public int2 MinCell;

        /// <summary>Map size in 32x32 chunks.</summary>
        public int2 ChunkCount;

        /// <summary>
        /// What a <see cref="BreachClass.Low"/> and a <see cref="BreachClass.High"/> seeker take off a
        /// structure per shot.
        ///
        /// A property of the map rather than of any unit, because it is what the shared fields are built
        /// with: two units in one class have to agree about it or they would need two fields, which is what
        /// classes exist to avoid. A unit's weapon decides which class it is in; it never decides what the
        /// class is worth. Zero leaves that class unable to break anything, which is a way of switching
        /// breaching off entirely.
        /// </summary>
        public ushort LowDamage;

        public ushort HighDamage;

        /// <summary>
        /// Settings for a map of at least <paramref name="sizeInCells"/> cells, rounded up to whole chunks.
        /// </summary>
        public static GridSettings FromCells(int2 sizeInCells, bool centerOnOrigin)
        {
            int2 chunkCount = math.max((sizeInCells + GridMap.CHUNK_SIZE - 1) / GridMap.CHUNK_SIZE, new int2(1, 1));
            int2 cells = chunkCount * GridMap.CHUNK_SIZE;
            return new GridSettings
            {
                MinCell = centerOnOrigin ? -cells / 2 : int2.zero,
                ChunkCount = chunkCount,

                // Defaults chosen so the two classes differ by a factor the pathfinder can actually read:
                // against a 300-health building, low takes 30 shots and high takes 6, which at ten cost a
                // shot is "not worth it" against "worth a six-cell detour". Tuning, and it wants measuring.
                LowDamage = 10,
                HighDamage = 50,
            };
        }
    }
}
