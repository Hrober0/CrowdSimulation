namespace GridNav
{
    /// <summary>
    /// The four grid neighbours, in clockwise order so that the opposite of a direction is
    /// <c>(direction + 2) % 4</c>. The order is also the bit order of the cell exit mask.
    /// </summary>
    public enum Direction : byte
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }
}
