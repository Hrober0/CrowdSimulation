using GridNav;
using Unity.Mathematics;

namespace Rts
{
    /// <summary>
    /// Where a building's cells and doorways land once it has been put down and turned.
    ///
    /// Two callers have to agree on this exactly: the rule that decides whether a building may stand here,
    /// and <see cref="BuildingFootprintSystem"/>, which takes the cells when it does. They were written
    /// separately and drifted - the system honoured rotation from the start, the placement check ignored it -
    /// so a turned building was approved against the cells of an unturned one. It would be waved onto ground
    /// it was not going to occupy and refused ground it was, and its doorstep was checked in the wrong place
    /// entirely, which is how a building ends up sealed the moment it is finished.
    ///
    /// Nothing here needs to know what kind of building it is. A footprint offset and a wall are geometry.
    /// </summary>
    public static class BuildingGeometry
    {
        /// <summary>The cell a footprint offset lands on.</summary>
        public static int2 CellOf(int2 origin, int2 offset, GridRotation rotation) =>
            origin + RotationUtils.Rotate(offset, rotation);

        /// <summary>Which way a wall faces once the building has been turned.</summary>
        public static Direction SideOf(Direction side, GridRotation rotation) =>
            RotationUtils.Rotate(side, rotation);

        /// <summary>
        /// The cell an agent stands in to use a door: one step out from the wall the door is cut into, on the
        /// side it faces. Outside the footprint by construction, which is what makes it somewhere to stand.
        /// </summary>
        public static int2 DoorstepOf(int2 origin, int2 wallOffset, Direction side, GridRotation rotation) =>
            CellOf(origin, wallOffset, rotation) + DirectionUtils.Offset(SideOf(side, rotation));

        /// <summary>The next quarter turn clockwise, wrapping back to <see cref="GridRotation.None"/>.</summary>
        public static GridRotation NextClockwise(GridRotation rotation) =>
            (GridRotation)(((int)rotation + 1) & 3);
    }
}
