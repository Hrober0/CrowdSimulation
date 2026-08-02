namespace Rts
{
    /// <summary>
    /// What a world object is, for the rules that care - harvest targeting, selection, land clearing.
    /// Navigation does not look at this: a cell is expensive or blocked because of its cost, never because
    /// of what kind of thing is standing on it.
    /// </summary>
    public enum ObjectKind : byte
    {
        None = 0,
        Tree = 1,
        Rock = 2,
    }
}
