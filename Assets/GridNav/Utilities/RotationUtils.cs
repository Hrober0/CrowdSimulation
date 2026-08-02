using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Rotating footprint offsets and directions on the grid. Everything here is exact integer maths - a
    /// footprint rotated four times is the footprint it started as, which floats would not guarantee.
    /// </summary>
    public static class RotationUtils
    {
        /// <summary>Rotates an offset around the origin cell.</summary>
        public static int2 Rotate(int2 offset, GridRotation rotation) => rotation switch
        {
            GridRotation.Clockwise90 => new int2(offset.y, -offset.x),
            GridRotation.Clockwise180 => -offset,
            GridRotation.CounterClockwise90 => new int2(-offset.y, offset.x),
            _ => offset,
        };

        public static Direction Rotate(Direction direction, GridRotation rotation) =>
            (Direction)(((int)direction + (int)rotation) & 3);

        public static GridRotation Inverse(GridRotation rotation) => (GridRotation)((4 - (int)rotation) & 3);
    }
}
