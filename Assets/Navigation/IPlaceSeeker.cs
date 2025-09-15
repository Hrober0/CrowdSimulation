namespace Navigation
{
    public interface IPlaceSeeker<T, TAttribute>
        where T : unmanaged, IPlaceSeeker<T, TAttribute>
        where TAttribute : unmanaged
    {
        bool IsValid(in TAttribute attribute);
    }
}