using GridNav;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// The last damage band a building's grid entry was written at (design §14 step 13, amended).
    ///
    /// Present on every building that has <see cref="Health"/>, and it exists so that the grid is written
    /// when the band changes rather than when the health does.
    /// </summary>
    public struct StructureBand : IComponentData
    {
        public byte Value;
    }

    /// <summary>
    /// Quantising a building's health for the pathfinder.
    ///
    /// **Four bands over a building's life, not a number that follows every hit.** Re-costing per hit would
    /// bump the chunk's structure version on every hit, and every assault field covering a building under
    /// attack would rebuild for the length of the fight. Four bands is four grid edits across the whole life
    /// of the building, and the difference between "half down" and "half down less one shot" is not a
    /// difference any route should be recomputed for.
    /// </summary>
    public static class StructureBands
    {
        public const int BANDS = 4;

        /// <summary>Which quarter of its life the building is in: 4 is untouched, 1 is nearly gone.</summary>
        public static byte Of(in Health health)
        {
            if (health.Max <= 0 || !health.IsAlive)
            {
                return 0;
            }

            int band = (health.Current * BANDS + health.Max - 1) / health.Max;
            return (byte)math.clamp(band, 1, BANDS);
        }

        /// <summary>
        /// The health the grid is told about: the top of the band rather than the true figure, so that every
        /// value inside one band writes the same number and the second write is a no-op.
        /// </summary>
        public static ushort HealthOf(in Health health)
        {
            byte band = Of(health);
            if (band == 0)
            {
                return 0;
            }

            int banded = health.Max * band / BANDS;
            return (ushort)math.clamp(banded, 1, CellData.BLOCKED * 64);
        }
    }
}
