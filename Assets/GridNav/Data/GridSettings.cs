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
            };
        }
    }
}
