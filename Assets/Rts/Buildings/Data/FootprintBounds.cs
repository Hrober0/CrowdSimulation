using GridNav;
using Unity.Entities;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// The ground a building stands on, as a rectangle in simulation units.
    ///
    /// Everything that reaches for a building - a weapon's range, a soldier walking up to one, a leash
    /// deciding whether one is close enough to bother with - measures to its **nearest edge**, never to its
    /// middle. Measured to the middle, a sword that reaches 1.4 cells could not hit anything bigger than one
    /// cell: a soldier pressed against a 2x2 stands 1.42 from its centre, against a 3x3 1.92. The rule is the
    /// one a body would use, and it is also why a long wall can be fought along its whole length rather than
    /// only at the point opposite its middle.
    ///
    /// A rectangle rather than the cells, because a footprint is always one: rotation turns the rectangle
    /// but never makes it anything else.
    /// </summary>
    public readonly struct FootprintBounds
    {
        public readonly float2 Min;
        public readonly float2 Max;

        public FootprintBounds(float2 min, float2 max)
        {
            Min = min;
            Max = max;
        }

        public float2 Centre => (Min + Max) * 0.5f;

        /// <summary>The point of the footprint nearest to <paramref name="point"/> - itself, if it is inside.</summary>
        public float2 ClosestPoint(float2 point) => math.clamp(point, Min, Max);

        public float DistanceSq(float2 point) => math.distancesq(point, ClosestPoint(point));

        /// <summary>False for an empty footprint, which a building has until its system has laid it.</summary>
        public static bool TryOf(in DynamicBuffer<BuildingFootprintCell> footprint, out FootprintBounds bounds)
        {
            if (footprint.IsEmpty)
            {
                bounds = default;
                return false;
            }

            int2 min = footprint[0].Cell;
            int2 max = min;

            foreach (BuildingFootprintCell cell in footprint)
            {
                min = math.min(min, cell.Cell);
                max = math.max(max, cell.Cell);
            }

            // Cells are a unit wide, so the far edge is one past the last cell's own corner.
            float2 lowCorner = GridCoords.CellCenter(min) - 0.5f;
            float2 highCorner = GridCoords.CellCenter(max) + 0.5f;

            bounds = new FootprintBounds(lowCorner, highCorner);
            return true;
        }
    }
}
