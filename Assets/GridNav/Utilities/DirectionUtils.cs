using Unity.Mathematics;

namespace GridNav
{
    /// <summary>
    /// Direction offsets and exit-mask bits. The exit mask is 4 bits, one per <see cref="Direction"/>,
    /// and it is the whole of the one-way road feature (design §3).
    /// </summary>
    public static class DirectionUtils
    {
        public const int DIRECTION_COUNT = 4;

        /// <summary>Every direction allowed - the default of an unauthored cell, so one-way needs no authoring.</summary>
        public const byte ALL_EXITS = 0b1111;

        public const byte NO_EXITS = 0;

        public static int2 Offset(Direction direction) => direction switch
        {
            Direction.North => new int2(0, 1),
            Direction.East => new int2(1, 0),
            Direction.South => new int2(0, -1),
            _ => new int2(-1, 0),
        };

        public static byte Bit(Direction direction) => (byte)(1 << (int)direction);

        public static Direction Opposite(Direction direction) => (Direction)(((int)direction + 2) & 3);

        public static bool Allows(byte exits, Direction direction) => (exits & Bit(direction)) != 0;

        public static byte Allow(byte exits, Direction direction) => (byte)(exits | Bit(direction));

        public static byte Forbid(byte exits, Direction direction) => (byte)(exits & ~Bit(direction));
    }
}
