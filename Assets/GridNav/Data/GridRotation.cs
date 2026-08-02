namespace GridNav
{
    /// <summary>
    /// Quarter turns clockwise. The values match <see cref="Direction"/> steps, so rotating a direction is
    /// an addition (see <see cref="RotationUtils"/>).
    /// </summary>
    public enum GridRotation : byte
    {
        None = 0,
        Clockwise90 = 1,
        Clockwise180 = 2,
        CounterClockwise90 = 3,
    }
}
