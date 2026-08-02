namespace GridNav
{
    /// <summary>
    /// The two borders a chunk owns. Every border is shared by two chunks, so each is owned by exactly one
    /// of them - the one on the lower side - and there is no chance of building the same gate twice.
    /// </summary>
    public enum GateBorder : byte
    {
        East = 0,
        North = 1,
    }
}
